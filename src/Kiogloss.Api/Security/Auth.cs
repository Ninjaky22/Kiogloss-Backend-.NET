using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiogloss.Api.Security;

/// <summary>BCryptPasswordEncoder (strength 10, $2a$) — compatible with every hash Spring stored.</summary>
public sealed class PasswordEncoder
{
    public string Encode(string rawPassword)
    {
        if (Encoding.UTF8.GetByteCount(rawPassword) > 72)
            throw new Compat.IllegalArgumentException("password cannot be more than 72 bytes");
        return BCrypt.Net.BCrypt.HashPassword(rawPassword, BCrypt.Net.BCrypt.GenerateSalt(10, 'a'));
    }

    public bool Matches(string rawPassword, string? encodedPassword)
    {
        if (string.IsNullOrEmpty(encodedPassword) || Encoding.UTF8.GetByteCount(rawPassword) > 72) return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(rawPassword, encodedPassword);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // "Encoded password does not look like BCrypt"
            return false;
        }
    }
}

/// <summary>
/// Port of JwtAuthenticationFilter: Bearer header, or ?token= on paths ending in /stream.
/// Any failure leaves the request anonymous (it never answers 401 by itself).
/// </summary>
public sealed class SpringJwtAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    JwtUtil jwtUtil,
    KioglossDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "SpringJwt";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        string? tokenFromQuery = null;
        var uri = Request.PathBase + Request.Path;
        if (uri.Value?.EndsWith("/stream", StringComparison.Ordinal) == true)
            tokenFromQuery = Request.Query["token"].FirstOrDefault();

        string jwt;
        if (authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            jwt = authHeader[7..];
        else if (!string.IsNullOrWhiteSpace(tokenFromQuery))
            jwt = tokenFromQuery;
        else
            return AuthenticateResult.NoResult();

        try
        {
            var userEmail = jwtUtil.ExtractUsername(jwt);
            if (userEmail is null) return AuthenticateResult.NoResult();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == userEmail)
                       ?? throw JavaErrors.UsernameNotFound("User not found with email: " + userEmail);
            if (!jwtUtil.ValidateToken(jwt, user)) return AuthenticateResult.NoResult();

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Email),
            };
            claims.AddRange(user.GetAuthorities().Select(a => new Claim(ClaimTypes.Role, a)));
            Context.Items[typeof(UserProfile)] = user;
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (Exception e)
        {
            Logger.LogDebug("Cannot set user authentication: {Error}", e.Message);
            return AuthenticateResult.NoResult();
        }
    }
}

/// <summary>
/// Port of SecurityConfig#securityFilterChain authorizeHttpRequests (same matcher order).
/// Spring answered every denial with an empty 403 (Http403ForbiddenEntryPoint / AccessDeniedHandlerImpl);
/// the admin frontend relies on that 403 to refresh its token.
/// </summary>
public sealed class UrlAuthorizationMiddleware(RequestDelegate next)
{
    private static readonly string[] PermitExact = ["/login", "/refresh", "/user/", "/auth/forgot-password", "/auth/reset-password"];
    private static readonly string[] PermitPrefixes = ["/products", "/article", "/tags", "/videos", "/uploads"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var user = context.User;
        var authenticated = user.Identity?.IsAuthenticated == true;

        bool allowed;
        if (PermitExact.Contains(path, StringComparer.Ordinal) || PermitPrefixes.Any(p => MatchesPrefix(path, p)))
            allowed = true;
        else if (MatchesPrefix(path, "/admin"))
            allowed = authenticated && user.IsInRole("ROLE_ADMIN");
        else
            allowed = authenticated;

        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context);
    }

    /// <summary>Ant pattern "/prefix/**": matches "/prefix" and anything below it.</summary>
    private static bool MatchesPrefix(string path, string prefix) =>
        path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
}

public static class HttpContextUserExtensions
{
    /// <summary>(UserProfile) authentication.getPrincipal()</summary>
    public static UserProfile Principal(this HttpContext context) =>
        context.Items[typeof(UserProfile)] as UserProfile
        ?? throw JavaErrors.NullPointer("Cannot invoke \"org.springframework.security.core.Authentication.getPrincipal()\" because \"auth\" is null");
}
