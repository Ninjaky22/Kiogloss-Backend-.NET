// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Crea y revisa las "llaves" (tokens) que prueban que el usuario inició sesión.
// Hay dos: una de corta duración para usar la API y otra más larga para pedir una nueva sin escribir la contraseña otra vez.
// ======================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Configuration;
using Kiogloss.Api.Models;
using Microsoft.Extensions.Options;

namespace Kiogloss.Api.Security;

/// <summary>
/// Port of com.todoteg.security.JwtUtil (jjwt 0.12). Tokens are byte-compatible with the ones the
/// Spring backend issues: header {"alg":"HS512"}, claims user_id, sub, iat, exp — so sessions keep
/// working across the migration in both directions.
/// </summary>
public sealed class JwtUtil(IOptions<JwtOptions> options, TimeProvider clock)
{
    // Jackson does not escape '+' or non-ASCII characters (e-mails end up in the "sub" claim)
    private static readonly JsonSerializerOptions TokenJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.Secret);
    private readonly long _accessTokenExpiration = options.Value.AccessTokenExpiration;
    private readonly long _refreshTokenExpiration = options.Value.RefreshTokenExpiration;

    public sealed record Claims(string? Subject, JsonElement Payload)
    {
        public long? UserId =>
            Payload.TryGetProperty("user_id", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    }

    public string? ExtractUsername(string token) => ExtractAllClaims(token).Subject;

    public long? ExtractUserId(string token) => ExtractAllClaims(token).UserId;

    public string GenerateAccessToken(UserProfile user) => CreateToken(user.Id, user.Email, _accessTokenExpiration);

    public string GenerateRefreshToken(UserProfile user) => CreateToken(user.Id, user.Email, _refreshTokenExpiration);

    /// <summary>validateToken: subject matches and not expired (expiry is also enforced by the parser).</summary>
    public bool ValidateToken(string token, UserProfile user)
    {
        var claims = ExtractAllClaims(token);
        return claims.Subject is not null && claims.Subject == user.Email && !IsExpired(claims.Payload);
    }

    private string CreateToken(long userId, string subject, long expirationMillis)
    {
        var nowMillis = clock.GetUtcNow().ToUnixTimeMilliseconds();
        var header = new Dictionary<string, object> { ["alg"] = AlgorithmForKey() };
        var payload = new Dictionary<string, object>
        {
            ["user_id"] = userId,
            ["sub"] = subject,
            ["iat"] = nowMillis / 1000,
            ["exp"] = (nowMillis + expirationMillis) / 1000,
        };
        var signingInput = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header, TokenJson)) + "." +
                           Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload, TokenJson));
        return signingInput + "." + Base64Url(Sign(AlgorithmForKey(), Encoding.ASCII.GetBytes(signingInput)));
    }

    /// <summary>Keys.hmacShaKeyFor + signWith(key): jjwt picks the strongest HMAC the key allows.</summary>
    private string AlgorithmForKey() => _key.Length switch
    {
        >= 64 => "HS512",
        >= 48 => "HS384",
        >= 32 => "HS256",
        _ => throw new JavaException("io.jsonwebtoken.security.WeakKeyException",
            $"The specified key byte array is {_key.Length * 8} bits which is not secure enough for any JWT HMAC-SHA algorithm."),
    };

    private byte[] Sign(string alg, byte[] data) => alg switch
    {
        "HS256" => HMACSHA256.HashData(_key, data),
        "HS384" => HMACSHA384.HashData(_key, data),
        "HS512" => HMACSHA512.HashData(_key, data),
        _ => throw new JavaException("io.jsonwebtoken.security.SignatureException",
            "JWT signature does not match locally computed signature. JWT validity cannot be asserted and should not be trusted."),
    };

    /// <summary>Jwts.parser().verifyWith(key).build().parseSignedClaims(token).getPayload()</summary>
    private Claims ExtractAllClaims(string? token)
    {
        if (string.IsNullOrEmpty(token))
            throw new Compat.IllegalArgumentException("CharSequence cannot be null or empty.");

        var periods = token.Count(c => c == '.');
        if (periods != 2 && periods != 4)
            throw Malformed("Invalid compact JWT string: Compact JWSs must contain exactly 2 period characters, " +
                            $"and compact JWEs must contain exactly 4.  Found: {periods}");
        if (periods == 4)
            throw new JavaException("io.jsonwebtoken.UnsupportedJwtException",
                "Cannot decrypt JWE payloads without a configured key.");

        var parts = token.Split('.');
        JsonElement header, payload;
        try
        {
            header = JsonSerializer.Deserialize<JsonElement>(FromBase64Url(parts[0]));
        }
        catch (Exception)
        {
            throw Malformed("Invalid protected header: Unable to deserialize Base64Url-encoded content.");
        }
        try
        {
            payload = JsonSerializer.Deserialize<JsonElement>(FromBase64Url(parts[1]));
        }
        catch (Exception)
        {
            throw Malformed("Malformed JWT JSON: Unable to deserialize Base64Url-encoded content.");
        }

        var alg = header.ValueKind == JsonValueKind.Object && header.TryGetProperty("alg", out var a) ? a.GetString() : null;
        if (alg is null or "none" || parts[2].Length == 0)
            throw new JavaException("io.jsonwebtoken.UnsupportedJwtException",
                "Unsecured JWSs (those with an 'alg' (Algorithm) header value of 'none') are disallowed by default as mandated by " +
                "https://www.rfc-editor.org/rfc/rfc7518.html#section-3.6. If you wish to allow them to be parsed, call the " +
                "JwtParserBuilder.unsecured() method, but please read the security considerations covered in that method's JavaDoc " +
                "before doing so. Header: {alg=none}");

        byte[] signature;
        try
        {
            signature = FromBase64Url(parts[2]);
        }
        catch (Exception)
        {
            throw Malformed("Invalid signature: Unable to decode Base64Url-encoded content.");
        }
        var expected = Sign(alg, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
            throw new JavaException("io.jsonwebtoken.security.SignatureException",
                "JWT signature does not match locally computed signature. JWT validity cannot be asserted and should not be trusted.");

        if (payload.ValueKind != JsonValueKind.Object)
            throw Malformed("Malformed JWT JSON: payload is not a JSON object.");

        var nowMillis = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (TryGetSeconds(payload, "exp", out var exp) && nowMillis > exp * 1000)
        {
            var expIso = DateTimeOffset.FromUnixTimeMilliseconds(exp * 1000).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var nowIso = DateTimeOffset.FromUnixTimeMilliseconds(nowMillis).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            throw new JavaException("io.jsonwebtoken.ExpiredJwtException",
                $"JWT expired {nowMillis - exp * 1000} milliseconds ago at {expIso}. Current time: {nowIso}. Allowed clock skew: 0 milliseconds.");
        }
        if (TryGetSeconds(payload, "nbf", out var nbf) && nowMillis < nbf * 1000)
            throw new JavaException("io.jsonwebtoken.PrematureJwtException", "JWT early.");

        var sub = payload.TryGetProperty("sub", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        return new Claims(sub, payload.Clone());
    }

    private bool IsExpired(JsonElement payload) =>
        TryGetSeconds(payload, "exp", out var exp) && exp * 1000 < clock.GetUtcNow().ToUnixTimeMilliseconds();

    private static bool TryGetSeconds(JsonElement payload, string name, out long seconds)
    {
        seconds = 0;
        return payload.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out seconds);
    }

    private static JavaException Malformed(string message) => new("io.jsonwebtoken.MalformedJwtException", message);

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
