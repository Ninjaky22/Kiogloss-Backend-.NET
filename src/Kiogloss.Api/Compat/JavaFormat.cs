// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Ayudas para escribir números y fechas igual que lo hacía el sistema anterior (hecho en Java).
// Así las pantallas siguen viendo los mismos textos, por ejemplo "10.0" en vez de "10".
// ======================================

using System.Globalization;
using System.Text;

namespace Kiogloss.Api.Compat;

/// <summary>
/// Reproduces the textual formats the Java code produced (Object#toString, Double#toString,
/// LocalDateTime#toString, Jackson's ISO formatter), so string fields come out identical.
/// </summary>
public static class JavaFormat
{
    /// <summary>Java string concatenation renders null as "null".</summary>
    public static string Str(object? value) => value switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        decimal d => BigDecimal(d),
        double d => Double(d),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };

    /// <summary>java.math.BigDecimal#toString for the plain-notation range (keeps the scale: "850000.00").</summary>
    public static string BigDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>java.time.LocalDateTime#toString: drops ":00" seconds and prints the fraction in groups of 3/6/9 digits.</summary>
    public static string LocalDateTimeToString(DateTime value)
    {
        var sb = new StringBuilder(29);
        sb.Append(value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
        var nanos = (int)(value.Ticks % TimeSpan.TicksPerSecond) * 100;
        if (value.Second > 0 || nanos > 0)
        {
            sb.Append(':').Append(value.Second.ToString("00", CultureInfo.InvariantCulture));
            if (nanos > 0)
            {
                if (nanos % 1_000_000 == 0)
                    sb.Append('.').Append((nanos / 1_000_000).ToString("000", CultureInfo.InvariantCulture));
                else if (nanos % 1_000 == 0)
                    sb.Append('.').Append((nanos / 1_000).ToString("000000", CultureInfo.InvariantCulture));
                else
                    sb.Append('.').Append(nanos.ToString("000000000", CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    /// <summary>DateTimeFormatter.ISO_LOCAL_DATE_TIME (what Jackson writes for LocalDateTime): seconds always, fraction trimmed.</summary>
    public static string IsoLocalDateTime(DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var nanos = (int)(value.Ticks % TimeSpan.TicksPerSecond) * 100;
        return nanos == 0 ? text : text + "." + nanos.ToString("000000000", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    /// <summary>java.lang.Double#toString (also what Jackson writes for double values): "90000.0", "1.0E7", "1.5E-4".</summary>
    public static string Double(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";

        // Shortest round-trip digits and decimal exponent of the value.
        var (digits, exponent) = Decompose(Math.Abs(value).ToString("R", CultureInfo.InvariantCulture));

        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);
        if (abs >= 1e-3 && abs < 1e7)
        {
            // exponent = position of the decimal point relative to the first digit
            string intPart, fracPart;
            if (exponent >= 0)
            {
                var intLen = exponent + 1;
                intPart = digits.Length >= intLen ? digits[..intLen] : digits.PadRight(intLen, '0');
                fracPart = digits.Length > intLen ? digits[intLen..] : "";
            }
            else
            {
                intPart = "0";
                fracPart = new string('0', -exponent - 1) + digits;
            }
            if (fracPart.Length == 0) fracPart = "0";
            return sign + intPart + "." + fracPart;
        }

        var mantissaFrac = digits.Length > 1 ? digits[1..] : "0";
        return sign + digits[0] + "." + mantissaFrac + "E" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    private static (string Digits, int Exponent) Decompose(string text)
    {
        var mantissa = text;
        var exp = 0;
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            mantissa = text[..e];
            exp = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
        var dot = mantissa.IndexOf('.');
        var intPart = dot >= 0 ? mantissa[..dot] : mantissa;
        var fracPart = dot >= 0 ? mantissa[(dot + 1)..] : "";
        var all = intPart + fracPart;
        var leadingZeros = all.Length - all.TrimStart('0').Length;
        var digits = all.TrimStart('0').TrimEnd('0');
        // decimal exponent of the first significant digit
        var exponent = intPart.Length - 1 - leadingZeros + exp;
        return (digits, exponent);
    }
}
