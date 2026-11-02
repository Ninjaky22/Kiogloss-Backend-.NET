// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Base común de todos los controladores.
// Tiene herramientas para leer los datos que llegan, usar valores por defecto y responder siempre con el mismo formato.
// ======================================

using System.Globalization;
using System.Text;
using System.Text.Json;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Models;
using Kiogloss.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Kiogloss.Api.Controllers;

/// <summary>
/// Spring MVC argument resolution: @RequestBody (Jackson + @Valid), @RequestParam with defaultValue,
/// @PathVariable conversion — including the exact exceptions Spring raised when they fail.
/// </summary>
public abstract class JavaControllerBase : ControllerBase
{
    protected const string Spring = "public org.springframework.http.ResponseEntity";

    /// <summary>(UserProfile) authentication.getPrincipal()</summary>
    protected UserProfile CurrentUser => HttpContext.Principal();

    /// <summary>@RequestBody [@Valid] T</summary>
    protected async Task<T> ReadBody<T>(string signature, string? validatedObjectName = null) where T : class
    {
        string text;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            text = await reader.ReadToEndAsync();

        var contentType = Request.ContentType;
        if (string.IsNullOrEmpty(contentType))
        {
            if (text.Length == 0) throw JavaErrors.BodyMissing(signature);
            throw UnsupportedMediaType("application/octet-stream");
        }
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !(mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
              mediaType.MediaType.Value?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true))
            throw UnsupportedMediaType(SpringMediaType(contentType));

        if (string.IsNullOrWhiteSpace(text)) throw JavaErrors.BodyMissing(signature);

        T? body;
        try
        {
            body = JsonSerializer.Deserialize<T>(text, JsonConfig.Options);
        }
        catch (JsonException e)
        {
            throw JavaErrors.JsonParse(e.Message);
        }
        if (body == null) throw JavaErrors.BodyMissing(signature);

        if (validatedObjectName != null) RequestValidator.Validate(body, validatedObjectName, signature);
        return body;
    }

    /// <summary>ServletServerHttpRequest adds the request encoding (UTF-8) when the Content-Type has no charset.</summary>
    private static string SpringMediaType(string contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed)) return contentType;
        var text = parsed.MediaType.ToString().ToLowerInvariant();
        var hasCharset = false;
        foreach (var p in parsed.Parameters)
        {
            hasCharset |= p.Name.Equals("charset", StringComparison.OrdinalIgnoreCase);
            text += ";" + p.Name + "=" + p.Value;
        }
        return hasCharset ? text : text + ";charset=UTF-8";
    }

    private static JavaException UnsupportedMediaType(string contentType) =>
        new("org.springframework.web.HttpMediaTypeNotSupportedException", $"Content-Type '{contentType}' is not supported");

    /// <summary>@RequestParam(defaultValue = "...") int</summary>
    protected int IntParam(string name, int defaultValue)
    {
        var raw = Request.Query[name].ToString();
        if (raw.Length == 0) return defaultValue;
        return int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw JavaErrors.TypeMismatch(name, "int", raw);
    }

    /// <summary>@RequestParam Integer (required)</summary>
    protected int RequiredIntParam(string name)
    {
        var raw = Request.Query[name].ToString();
        if (raw.Length == 0) throw JavaErrors.MissingParameter(name, "Integer");
        return int.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw JavaErrors.TypeMismatch(name, "java.lang.Integer", raw);
    }

    /// <summary>@RequestParam(required = false) Long</summary>
    protected long? OptionalLongParam(string name)
    {
        var raw = Request.Query[name].ToString();
        if (raw.Length == 0) return null;
        return long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw JavaErrors.TypeMismatch(name, "java.lang.Long", raw);
    }

    /// <summary>@RequestParam(required = false) String</summary>
    protected string? OptionalParam(string name)
    {
        var values = Request.Query[name];
        return values.Count == 0 ? null : string.Join(',', values.ToArray());
    }

    /// <summary>@RequestParam(defaultValue = "...") String</summary>
    protected string Param(string name, string defaultValue)
    {
        var value = OptionalParam(name);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    /// <summary>@RequestParam String (required)</summary>
    protected string RequiredParam(string name)
    {
        var value = OptionalParam(name);
        return string.IsNullOrEmpty(value) ? throw JavaErrors.MissingParameter(name, "String") : value;
    }

    /// <summary>@PathVariable Long</summary>
    protected static long PathLong(string name, string raw) =>
        long.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw JavaErrors.TypeMismatch(name, "java.lang.Long", raw);

    /// <summary>JSON response with the Spring status code.</summary>
    protected static JsonResult Json(object? value, int status = 200) =>
        new(value, JsonConfig.Options) { StatusCode = status, ContentType = "application/json" };

    /// <summary>ResponseEntity.ok().build() / status(...).build()</summary>
    protected static StatusCodeResult EmptyStatus(int status) => new(status);
}
