// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Los tipos de error que usa el sistema: "no se encontró", "dato inválido", "sin permiso", etc.
// Cuando algo sale mal, el código lanza uno de estos errores y el sistema lo convierte en un mensaje claro para el usuario.
// ======================================

namespace Kiogloss.Api.Compat;

/// <summary>
/// Exception that carries the fully-qualified Java class name the Spring backend threw in the
/// same situation. GlobalExceptionMiddleware renders it exactly like Spring's
/// GlobalExceptionHandler did ("Error interno del servidor: " + ex.toString()), because the
/// frontends show that text to the user.
/// </summary>
public class JavaException : Exception
{
    private readonly bool _hasMessage;

    public string JavaType { get; }

    public JavaException(string javaType, string? message = null, Exception? inner = null)
        : base(message ?? javaType, inner)
    {
        JavaType = javaType;
        _hasMessage = message != null;
    }

    /// <summary>Message as Java's getMessage() returns it (null when constructed without one).</summary>
    public string? JavaMessage => _hasMessage ? Message : null;

    /// <summary>Equivalent of java.lang.Throwable#toString().</summary>
    public string JavaToString() => _hasMessage ? $"{JavaType}: {Message}" : JavaType;
}

/// <summary>java.lang.RuntimeException — the generic business error used across the services (HTTP 500).</summary>
public class RuntimeException(string message) : JavaException("java.lang.RuntimeException", message);

/// <summary>java.lang.IllegalArgumentException and subclasses (HTTP 400 with the raw message).</summary>
public class IllegalArgumentException(string? message, string javaType = "java.lang.IllegalArgumentException")
    : JavaException(javaType, message)
{
    public static IllegalArgumentException NumberFormat(string input) =>
        new($"For input string: \"{input}\"", "java.lang.NumberFormatException");
}

/// <summary>com.todoteg.exception.ResourceNotFoundException (HTTP 404).</summary>
public class ResourceNotFoundException(string message)
    : JavaException("com.todoteg.exception.ResourceNotFoundException", message);

/// <summary>org.springframework.security.access.AccessDeniedException (HTTP 403 envelope).</summary>
public class AccessDeniedException(string message)
    : JavaException("org.springframework.security.access.AccessDeniedException", message);

/// <summary>Factory for the framework exceptions Spring MVC / Security / jjwt raise.</summary>
public static class JavaErrors
{
    public const string TypeMismatchType = "org.springframework.web.method.annotation.MethodArgumentTypeMismatchException";

    public static JavaException NoSuchElement() => new("java.util.NoSuchElementException", "No value present");

    public static JavaException NoResourceFound(string resourcePath) =>
        new("org.springframework.web.servlet.resource.NoResourceFoundException", $"No static resource {resourcePath}.");

    public static JavaException MethodNotSupported(string method) =>
        new("org.springframework.web.HttpRequestMethodNotSupportedException", $"Request method '{method}' is not supported");

    public static JavaException NotAcceptable() =>
        new("org.springframework.web.HttpMediaTypeNotAcceptableException", "No acceptable representation");

    public static JavaException MissingParameter(string name, string javaSimpleType) =>
        new("org.springframework.web.bind.MissingServletRequestParameterException",
            $"Required request parameter '{name}' for method parameter type {javaSimpleType} is not present");

    public static JavaException MissingPart(string name) =>
        new("org.springframework.web.multipart.support.MissingServletRequestPartException", $"Required part '{name}' is not present.");

    public static JavaException NotMultipart() =>
        new("org.springframework.web.multipart.MultipartException", "Current request is not a multipart request");

    public static JavaException TypeMismatch(string name, string javaType, string value) =>
        new(TypeMismatchType,
            $"Method parameter '{name}': Failed to convert value of type 'java.lang.String' to required type '{javaType}'; For input string: \"{value}\"");

    public static JavaException DateTypeMismatch(string name, string value) =>
        new(TypeMismatchType,
            $"Method parameter '{name}': Failed to convert value of type 'java.lang.String' to required type 'java.time.LocalDate'; " +
            $"Failed to convert from type [java.lang.String] to type [@org.springframework.web.bind.annotation.RequestParam " +
            $"@org.springframework.format.annotation.DateTimeFormat java.time.LocalDate] for value [{value}]");

    public static JavaException BodyMissing(string signature) =>
        new("org.springframework.http.converter.HttpMessageNotReadableException", $"Required request body is missing: {signature}");

    public static JavaException JsonParse(string detail) =>
        new("org.springframework.http.converter.HttpMessageNotReadableException", $"JSON parse error: {detail}");

    public static JavaException BadCredentials() =>
        new("org.springframework.security.authentication.BadCredentialsException", "Bad credentials");

    public static JavaException Disabled() =>
        new("org.springframework.security.authentication.DisabledException", "User is disabled");

    public static JavaException UsernameNotFound(string message) =>
        new("org.springframework.security.core.userdetails.UsernameNotFoundException", message);

    public static JavaException NullPointer(string? message = null) => new("java.lang.NullPointerException", message);

    public static JavaException PropertyReference(string property, string type) =>
        new("org.springframework.data.mapping.PropertyReferenceException", $"No property '{property}' found for type '{type}'");

    /// <summary>Spring Data repositories translate IllegalArgumentException (e.g. findById(null)).</summary>
    public static JavaException InvalidDataAccessApiUsage(string message) =>
        new("org.springframework.dao.InvalidDataAccessApiUsageException", message);

    public static JavaException TransactionSystem() =>
        new("org.springframework.transaction.TransactionSystemException", "Could not commit JPA transaction");
}
