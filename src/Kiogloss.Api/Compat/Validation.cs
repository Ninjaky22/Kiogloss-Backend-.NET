// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Revisa que los datos recibidos sean correctos: campo obligatorio, correo bien escrito, largo máximo...
// Si algo no cumple, se rechaza antes de hacer cualquier cosa.
// ======================================

using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Kiogloss.Api.Compat;

/// <summary>Base of the jakarta.validation constraints used by the request DTOs.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public abstract class JavaConstraintAttribute : Attribute
{
    /// <summary>Simple name of the jakarta constraint (NotBlank, NotNull, Email, Size).</summary>
    public abstract string Code { get; }

    /// <summary>Custom message (message = "...") or null for the default Hibernate Validator (es) message.</summary>
    public string? Message { get; set; }

    public abstract string DefaultMessage { get; }

    /// <summary>Extra constraint attributes Spring prints after the field resolvable (alphabetical by attribute name).</summary>
    public virtual string ExtraArguments => "";

    public abstract bool IsValid(object? value);
}

public sealed class NotBlankAttribute : JavaConstraintAttribute
{
    public override string Code => "NotBlank";
    public override string DefaultMessage => "no debe estar vacío";
    public override bool IsValid(object? value) => value is string s && !string.IsNullOrWhiteSpace(s);
}

public sealed class NotNullAttribute : JavaConstraintAttribute
{
    public override string Code => "NotNull";
    public override string DefaultMessage => "no debe ser nulo";
    public override bool IsValid(object? value) => value is not null;
}

public sealed class EmailAttribute : JavaConstraintAttribute
{
    public override string Code => "Email";
    public override string DefaultMessage => "debe ser una dirección de correo electrónico con formato correcto";
    public override string ExtraArguments => ",[Ljakarta.validation.constraints.Pattern$Flag;@4e0b2b8f,.*";
    public override bool IsValid(object? value) => value is not string s || HibernateEmail.IsValid(s);
}

public sealed class SizeAttribute(int min = 0, int max = int.MaxValue) : JavaConstraintAttribute
{
    public override string Code => "Size";
    public override string DefaultMessage => $"el tamaño debe estar entre {min} y {max}";
    public override string ExtraArguments => $",{max},{min}";
    public override bool IsValid(object? value) => value switch
    {
        null => true,
        string s => s.Length >= min && s.Length <= max,
        ICollection c => c.Count >= min && c.Count <= max,
        _ => true,
    };
}

/// <summary>
/// Runs the constraints of a @Valid @RequestBody and throws the same
/// MethodArgumentNotValidException message Spring produced.
/// </summary>
public static class RequestValidator
{
    public static void Validate(object body, string objectName, string signature)
    {
        var errors = new List<string>();
        foreach (var prop in body.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(body);
            foreach (var constraint in prop.GetCustomAttributes<JavaConstraintAttribute>())
            {
                if (constraint.IsValid(value)) continue;
                var field = JsonName(prop);
                var javaType = JavaTypeName(prop.PropertyType);
                var c = constraint.Code;
                errors.Add(
                    $"Field error in object '{objectName}' on field '{field}': rejected value [{JavaFormat.Str(value)}]; " +
                    $"codes [{c}.{objectName}.{field},{c}.{field},{c}.{javaType},{c}]; " +
                    $"arguments [org.springframework.context.support.DefaultMessageSourceResolvable: codes [{objectName}.{field},{field}]; " +
                    $"arguments []; default message [{field}]{constraint.ExtraArguments}]; default message [{constraint.Message ?? constraint.DefaultMessage}]");
            }
        }
        if (errors.Count == 0) return;

        var sb = new StringBuilder("Validation failed for argument [0] in ").Append(signature);
        if (errors.Count > 1) sb.Append(" with ").Append(errors.Count).Append(" errors");
        sb.Append(": ");
        foreach (var e in errors) sb.Append('[').Append(e).Append("] ");
        throw new JavaException("org.springframework.web.bind.MethodArgumentNotValidException", sb.ToString());
    }

    private static string JsonName(PropertyInfo prop) =>
        prop.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name ?? prop.Name;

    private static string JavaTypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "java.lang.String";
        if (type == typeof(long)) return "java.lang.Long";
        if (type == typeof(int)) return "java.lang.Integer";
        if (type == typeof(double)) return "java.lang.Double";
        if (type == typeof(decimal)) return "java.math.BigDecimal";
        if (type == typeof(bool)) return "java.lang.Boolean";
        if (typeof(IList).IsAssignableFrom(type)) return "java.util.List";
        if (typeof(IDictionary).IsAssignableFrom(type)) return "java.util.Map";
        return "java.lang.Object";
    }
}

/// <summary>
/// Hibernate Validator's entity-level checks on UserProfile (@Email email, @Pattern phoneNumber),
/// which run when the entity is flushed.
/// </summary>
public static partial class EntityValidator
{
    [GeneratedRegex(@"^\+?1?\d{8,15}$")]
    private static partial Regex PhonePattern();

    private const string PhoneViolation =
        "\tConstraintViolationImpl{interpolatedMessage='debe coincidir con \"^\\+?1?\\d{8,15}$\"', propertyPath=phoneNumber, " +
        "rootBeanClass=class com.todoteg.model.UserProfile, messageTemplate='{jakarta.validation.constraints.Pattern.message}'}\n";

    private const string EmailViolation =
        "\tConstraintViolationImpl{interpolatedMessage='debe ser una dirección de correo electrónico con formato correcto', propertyPath=email, " +
        "rootBeanClass=class com.todoteg.model.UserProfile, messageTemplate='{jakarta.validation.constraints.Email.message}'}\n";

    /// <summary>Returns the violations text, or null when the entity is valid.</summary>
    private static string? Violations(string? email, string? phoneNumber)
    {
        var sb = new StringBuilder();
        if (email != null && !HibernateEmail.IsValid(email)) sb.Append(EmailViolation);
        if (phoneNumber != null && !PhonePattern().IsMatch(phoneNumber)) sb.Append(PhoneViolation);
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Validation during persist (insert): jakarta.validation.ConstraintViolationException.</summary>
    public static void ValidateUserOnPersist(string? email, string? phoneNumber)
    {
        var v = Violations(email, phoneNumber);
        if (v == null) return;
        throw new JavaException("jakarta.validation.ConstraintViolationException",
            "Validation failed for classes [com.todoteg.model.UserProfile] during persist time for groups " +
            "[jakarta.validation.groups.Default, ]\nList of constraint violations:[\n" + v + "]");
    }

    /// <summary>Validation during the commit flush of an update: wrapped by Spring in TransactionSystemException.</summary>
    public static void ValidateUserOnUpdate(string? email, string? phoneNumber)
    {
        if (Violations(email, phoneNumber) != null) throw JavaErrors.TransactionSystem();
    }
}

/// <summary>Port of org.hibernate.validator.internal.constraintvalidators.AbstractEmailValidator.</summary>
public static partial class HibernateEmail
{
    private const string LocalPartAtom = "[a-z0-9!#$%&'*+/=?^_`{|}~\u0080-￿-]";
    private const string LocalPartInsideQuotesAtom = "(?:[a-z0-9!#$%&'*.(),<>\\[\\]:;  @+/=?^_`{|}~\u0080-￿-]|\\\\\\\\|\\\\\")";

    [GeneratedRegex("^(?:" + LocalPartAtom + "+|\"" + LocalPartInsideQuotesAtom + "+\")(?:\\." + "(?:" + LocalPartAtom + "+|\"" + LocalPartInsideQuotesAtom + "+\"))*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LocalPart();

    [GeneratedRegex("^(?:" + "[a-z0-9\u0080-￿](?:[a-z0-9\u0080-￿-]{0,61}[a-z0-9\u0080-￿])?" +
                    "(?:\\.[a-z0-9\u0080-￿](?:[a-z0-9\u0080-￿-]{0,61}[a-z0-9\u0080-￿])?)*" +
                    "|\\[[0-9]{1,3}(?:\\.[0-9]{1,3}){3}\\]|\\[IPv6:[0-9a-f:.]+\\])$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Domain();

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        var split = value.LastIndexOf('@');
        if (split < 0) return false;
        var local = value[..split];
        var domain = value[(split + 1)..];
        if (local.Length > 64 || !LocalPart().IsMatch(local)) return false;
        if (domain.Length == 0 || domain.EndsWith('.') || domain.Length > 255) return false;
        return Domain().IsMatch(domain);
    }
}
