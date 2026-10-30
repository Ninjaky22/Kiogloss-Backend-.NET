using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kiogloss.Api.Compat;

/// <summary>com.todoteg.dto.admin.ApiResponseDTO — envelope of the admin endpoints and of every error.</summary>
public sealed class ApiResponseDTO<T>(bool success, string? message, T? data)
{
    [JsonPropertyName("success")] public bool Success { get; } = success;
    [JsonPropertyName("message")] public string? Message { get; } = message;
    [JsonPropertyName("data")] public T? Data { get; } = data;

    public static ApiResponseDTO<T> Ok(T data) => new(true, "Operación exitosa", data);
    public static ApiResponseDTO<T> Ok(string message, T data) => new(true, message, data);
}

public static class ApiResponseDTO
{
    public static ApiResponseDTO<object> Error(string? message) => new(false, message, null);
}

/// <summary>com.todoteg.dto.ErrorResponse ({"detail": "..."}).</summary>
public sealed class ErrorResponse(string detail)
{
    [JsonPropertyName("detail")] public string Detail { get; } = detail;
}

/// <summary>Port of com.todoteg.exception.GlobalExceptionHandler.</summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private const string IntegrityMessage =
        "No se puede eliminar este recurso porque está asociado a pedidos, favoritos u otros registros. Te recomendamos cambiar su estado a 'Borrador'.";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, message) = Translate(ex);
            if (status == 500) logger.LogWarning(ex, "Request {Method} {Path} failed", context.Request.Method, context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponseDTO.Error(message), JsonConfig.Options));
        }
    }

    public static (int Status, string? Message) Translate(Exception ex)
    {
        if (IsDataIntegrityViolation(ex)) return (409, IntegrityMessage);
        return ex switch
        {
            ResourceNotFoundException nf => (404, nf.JavaMessage),
            IllegalArgumentException ia => (400, ia.JavaMessage),
            AccessDeniedException => (403, "No tienes permisos para realizar esta acción"),
            JavaException je => (500, "Error interno del servidor: " + je.JavaToString()),
            _ => (500, "Error interno del servidor: " + ex.GetType().FullName + ": " + ex.Message),
        };
    }

    /// <summary>org.springframework.dao.DataIntegrityViolationException: unique / FK / not-null violations (SQLSTATE class 23).</summary>
    private static bool IsDataIntegrityViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException pg } && pg.SqlState.StartsWith("23", StringComparison.Ordinal)
        || ex is PostgresException pg2 && pg2.SqlState.StartsWith("23", StringComparison.Ordinal);
}

/// <summary>
/// Spring MVC routing semantics on top of ASP.NET endpoint routing:
/// no trailing-slash matching (except the real "/user/" mappings), unknown paths -> NoResourceFoundException,
/// wrong verb -> HttpRequestMethodNotSupportedException. Both end up as HTTP 500 through the global handler.
/// </summary>
public sealed class SpringRoutingMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var endpoint = context.GetEndpoint();
        var trailingSlash = path.Length > 1 && path.EndsWith('/') && path != "/user/";
        if (trailingSlash || path == "/user" || endpoint is null)
            throw JavaErrors.NoResourceFound(path.Trim('/'));
        if (endpoint.DisplayName == "405 HTTP Method Not Supported")
            throw JavaErrors.MethodNotSupported(context.Request.Method);
        return next(context);
    }
}

/// <summary>Default headers written by Spring Security's HeaderWriterFilter.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-XSS-Protection"] = "0";
            if (!h.ContainsKey("Cache-Control"))
            {
                h["Cache-Control"] = "no-cache, no-store, max-age=0, must-revalidate";
                h["Pragma"] = "no-cache";
                h["Expires"] = "0";
            }
            h["X-Frame-Options"] = "DENY";
            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>
/// Port of SecurityConfig#corsConfigurationSource processed by Spring's DefaultCorsProcessor:
/// origins *, methods GET,POST,PUT,DELETE,PATCH,OPTIONS, any header, no credentials.
/// Preflight requests end here with 200, as Spring's CorsFilter does.
/// </summary>
public sealed class SpringCorsMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"];

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers.Append("Vary", "Origin");
        response.Headers.Append("Vary", "Access-Control-Request-Method");
        response.Headers.Append("Vary", "Access-Control-Request-Headers");

        var origin = request.Headers.Origin.ToString();
        var isCors = origin.Length > 0 && !IsSameOrigin(request, origin);
        var isPreflight = HttpMethods.IsOptions(request.Method) && origin.Length > 0
                          && request.Headers.ContainsKey("Access-Control-Request-Method");

        if (isCors)
        {
            if (isPreflight)
            {
                var requestedMethod = request.Headers["Access-Control-Request-Method"].ToString();
                if (!AllowedMethods.Contains(requestedMethod, StringComparer.OrdinalIgnoreCase))
                {
                    response.StatusCode = 403;
                    await response.WriteAsync("Invalid CORS request");
                    return;
                }
                response.Headers["Access-Control-Allow-Origin"] = "*";
                response.Headers["Access-Control-Allow-Methods"] = string.Join(',', AllowedMethods);
                var requestedHeaders = request.Headers["Access-Control-Request-Headers"].ToString();
                if (requestedHeaders.Length > 0)
                {
                    var list = requestedHeaders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    response.Headers["Access-Control-Allow-Headers"] = string.Join(", ", list);
                }
                response.StatusCode = 200;
                return;
            }
            response.Headers["Access-Control-Allow-Origin"] = "*";
        }
        else if (isPreflight)
        {
            response.StatusCode = 200;
            return;
        }

        await next(context);
    }

    private static bool IsSameOrigin(HttpRequest request, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var host = request.Host;
        var port = host.Port ?? (request.IsHttps ? 443 : 80);
        return string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == port;
    }
}
