using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Kiogloss.Api.Tests;

/// <summary>Starts PostgreSQL 15 with the schema Hibernate created (db/schema.sql) and the API on top of it.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:15-alpine").Build();
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "kiogloss-tests-" + Guid.NewGuid());

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        var schema = await File.ReadAllTextAsync(Path.Combine(FindRepoRoot(), "db", "schema.sql"));
        await using var conn = new NpgsqlConnection(_db.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(schema, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // applied after Program's sources (including a developer's appsettings.Local.json)
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _db.GetConnectionString(),
            ["Jwt:Secret"] = "kiogloss-test-secret-only-for-unit-tests-0123456789-abcdefghijklmnop",
            ["App:UploadDir"] = _uploads,
            ["App:SeedDatabase"] = "true",
            ["Cloudinary:CloudName"] = "dqxq7tupx",
        }));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient Api() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("http://localhost/API/V1.0/"),
        AllowAutoRedirect = false,
    });

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Kiogloss.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Kiogloss.sln not found");
    }
}

public class ApiContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<(string Access, long UserId)> Register(HttpClient api, string email, bool admin = false)
    {
        var phone = "3" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        var res = await api.PostAsync("user/", Json($$"""
            {"email":"{{email}}","name":"Test {{email}}","password":"Secret123!","phoneNumber":"{{phone}}",
             "address":{"street":"Calle 1","streetNumber":"2-3","distric":"Centro","city":"Neiva"},
             "account":{"pointsPerPurchase":0,"isActive":true},"isSuperuser":{{(admin ? "true" : "false")}}}
            """));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var access = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("access").GetString()!;
        var payload = access.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var userId = JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.GetProperty("user_id").GetInt64();
        return (access, userId);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_paths_get_an_empty_403_with_cors_headers()
    {
        var api = factory.Api();
        var req = new HttpRequestMessage(HttpMethod.Get, "user/cart");
        req.Headers.Add("Origin", "http://localhost:5173");
        var res = await api.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("", await res.Content.ReadAsStringAsync());
        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());

        // an expired / garbage token behaves the same (the admin frontend refreshes on 403)
        var garbage = await api.SendAsync(Authed(HttpMethod.Get, "user/notifications", "eyJhbGciOiJIUzUxMiJ9.e30.x"));
        Assert.Equal(HttpStatusCode.Forbidden, garbage.StatusCode);
    }

    [Fact]
    public async Task Preflight_is_answered_like_springs_cors_filter()
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "user/cart");
        req.Headers.Add("Origin", "http://localhost:5173");
        req.Headers.Add("Access-Control-Request-Method", "PATCH");
        req.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var res = await factory.Api().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("GET,POST,PUT,DELETE,PATCH,OPTIONS", res.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Equal("authorization, content-type", res.Headers.GetValues("Access-Control-Allow-Headers").Single());
    }

    [Fact]
    public async Task Public_catalog_keeps_the_spring_json()
    {
        var api = factory.Api();
        var body = await api.GetStringAsync("products");
        Assert.Contains("\"price\":\"850000.00\"", body);
        Assert.Contains("\"pageable\":{\"pageNumber\":0,\"pageSize\":8,\"sort\":{\"sorted\":true,\"unsorted\":false,\"empty\":false}", body);

        var article = await api.GetStringAsync("article/tarjeta-grafica-gamer");
        Assert.Contains("\"tags\":[\"Tecnología\"]", article);
        Assert.Contains("{\"id\":1,\"typeName\":\"Color\",\"value\":\"Negro\",\"metaValue\":\"#000000\"}", article);

        var missing = await api.GetAsync("article/no-existe");
        Assert.Equal(HttpStatusCode.InternalServerError, missing.StatusCode);
        Assert.Equal("""{"success":false,"message":"Error interno del servidor: java.lang.RuntimeException: Product not found","data":null}""",
            await missing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Spring_routing_semantics()
    {
        var api = factory.Api();
        var slash = await api.GetAsync("products/");
        Assert.Equal(HttpStatusCode.InternalServerError, slash.StatusCode);
        Assert.Contains("NoResourceFoundException: No static resource products.", await slash.Content.ReadAsStringAsync());

        var verb = await api.GetAsync("login");
        Assert.Equal(HttpStatusCode.InternalServerError, verb.StatusCode);
        Assert.Contains("HttpRequestMethodNotSupportedException: Request method 'GET' is not supported", await verb.Content.ReadAsStringAsync());

        var outside = await factory.CreateClient().GetAsync("/products");
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
    }

    [Fact]
    public async Task Login_errors_and_user_detail_rules()
    {
        var api = factory.Api();
        var (token, userId) = await Register(api, "detail@test.com");

        var bad = await api.PostAsync("login", Json("""{"email":"detail@test.com","password":"nope"}"""));
        Assert.Equal(HttpStatusCode.InternalServerError, bad.StatusCode);
        Assert.Contains("org.springframework.security.authentication.BadCredentialsException: Bad credentials", await bad.Content.ReadAsStringAsync());

        var own = await api.SendAsync(Authed(HttpMethod.Get, $"user/{userId}", token));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var json = await own.Content.ReadAsStringAsync();
        Assert.Contains("\"profileImage\":\"http://res.cloudinary.com/dqxq7tupx/image/upload/profile_p8im0u.png\"", json);
        Assert.Contains("\"favorite\":\"No tienes favoritos\",\"address\":\"Calle 1 2-3 Centro, Neiva\",\"city\":\"Neiva\",\"pointsPerPurchase\":0,\"isActive\":true", json);

        var other = await api.SendAsync(Authed(HttpMethod.Get, $"user/{userId + 1000}", token));
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
        Assert.Equal("""{"detail":"Acceso no autorizado"}""", await other.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cart_items_keep_the_hashmap_key_order_and_decimal_scale()
    {
        var api = factory.Api();
        var (token, _) = await Register(api, "cart@test.com");
        var add = await api.SendAsync(Authed(HttpMethod.Post, "user/cart", token, Json("""{"variantId":1,"quantity":2}""")));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal("", await add.Content.ReadAsStringAsync());

        var cart = await (await api.SendAsync(Authed(HttpMethod.Get, "user/cart", token))).Content.ReadAsStringAsync();
        Assert.StartsWith("{\"grandTotal\":1700000.00,\"content\":[{\"quantity\":2,\"variantDetails\":\"Negro, GTX 1060\",\"productId\":1,\"price\":850000.00,", cart);
        Assert.EndsWith("\"totalElements\":1,\"totalPages\":1,\"number\":0}", cart);

        var noQty = await api.SendAsync(Authed(HttpMethod.Put, "user/cart/1", token));
        Assert.Contains("MissingServletRequestParameterException: Required request parameter 'quantity' for method parameter type Integer is not present",
            await noQty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Order_endpoints_keep_springs_errors()
    {
        var api = factory.Api();
        var (token, _) = await Register(api, "orders@test.com");

        var badDate = await api.SendAsync(Authed(HttpMethod.Get, "user/orders?rangeDate_after=2026-13-01", token));
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.Equal("""{"detail":"Formato de fecha inválido. Use el formato: YYYY-MM-DD (ejemplo: 2024-01-15)"}""", await badDate.Content.ReadAsStringAsync());

        // inherited Spring/PostgreSQL bug: every date-filtered query fails
        var dateFilter = await api.SendAsync(Authed(HttpMethod.Get, "user/orders?rangeDate_after=2026-01-01", token));
        Assert.Equal(HttpStatusCode.InternalServerError, dateFilter.StatusCode);
        Assert.Contains("could not determine data type of parameter $4", await dateFilter.Content.ReadAsStringAsync());

        var badStatus = await api.SendAsync(Authed(HttpMethod.Post, "user/order", token,
            Json("""{"account":1,"shopping":[],"amount":1,"status":"Pendiente"}""")));
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        Assert.Contains("No enum constant com.todoteg.model.OrderStatus.PENDIENTE", await badStatus.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_area_requires_superuser()
    {
        var api = factory.Api();
        var (user, _) = await Register(api, "plain@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await api.SendAsync(Authed(HttpMethod.Get, "admin/products", user))).StatusCode);

        var (admin, _) = await Register(api, "boss@test.com", admin: true);
        var res = await api.SendAsync(Authed(HttpMethod.Get, "admin/stats", admin));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("{\"success\":true,\"message\":\"Operación exitosa\",\"data\":{\"totalUsers\":", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sse_without_event_stream_accept_header_fails_like_spring()
    {
        var api = factory.Api();
        var (token, _) = await Register(api, "sse@test.com");
        var res = await api.SendAsync(Authed(HttpMethod.Get, "user/notifications/stream", token));
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Contains("HttpMediaTypeNotAcceptableException: No acceptable representation", await res.Content.ReadAsStringAsync());
    }
}
