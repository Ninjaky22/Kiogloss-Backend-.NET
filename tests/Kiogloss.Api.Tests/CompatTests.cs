using System.Text.Json;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Configuration;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Models;
using Kiogloss.Api.Security;
using Kiogloss.Api.Services;
using Microsoft.Extensions.Options;

namespace Kiogloss.Api.Tests;

/// <summary>
/// Expected values come from the Java side: jjwt 0.12.3 / BCryptPasswordEncoder output, and
/// responses captured from the running Spring backend.
/// </summary>
public class JwtCompatibilityTests
{
    // Generated with jjwt 0.12.3 exactly like com.todoteg.security.JwtUtil#createToken
    // (claims {user_id=42}, sub, issuedAt = 1792369610123 ms, expiration = +300000 ms).
    private const string Secret = "kiogloss-test-secret-only-for-unit-tests-0123456789-abcdefghijklmnop";
    private const string SpringToken =
        "eyJhbGciOiJIUzUxMiJ9.eyJ1c2VyX2lkIjo0Miwic3ViIjoiY2xpZW50ZStwcnVlYmFzQGtpb2dsb3NzLmNvbSIsImlhdCI6MTc5MjM2OTYxMCwiZXhwIjoxNzkyMzY5OTEwfQ" +
        ".OGNrYU4_45p4TDQCcmmCdd89GtbsO1icduKgv89AA4C0hbb0I9SaiQG4uan11BFMlv0hMPRC-lTQ7RQdj8mj4g";

    private static readonly UserProfile User = new() { Id = 42, Email = "cliente+pruebas@kiogloss.com" };

    private static JwtUtil Util(long nowMillis) =>
        new(Options.Create(new JwtOptions { Secret = Secret }), new FixedTime(nowMillis));

    [Fact]
    public void Generates_byte_identical_tokens_to_jjwt()
    {
        Assert.Equal(SpringToken, Util(1792369610123).GenerateAccessToken(User));
    }

    [Fact]
    public void Accepts_tokens_issued_by_spring()
    {
        var util = Util(1792369700000);
        Assert.Equal("cliente+pruebas@kiogloss.com", util.ExtractUsername(SpringToken));
        Assert.Equal(42, util.ExtractUserId(SpringToken));
        Assert.True(util.ValidateToken(SpringToken, User));
    }

    [Fact]
    public void Expired_token_fails_like_jjwt()
    {
        var ex = Assert.Throws<JavaException>(() => Util(1792369911000).ExtractUsername(SpringToken));
        Assert.Equal("io.jsonwebtoken.ExpiredJwtException", ex.JavaType);
        Assert.Equal("JWT expired 1000 milliseconds ago at 2026-10-19T00:31:50.000Z. Current time: 2026-10-19T00:31:51.000Z. " +
                     "Allowed clock skew: 0 milliseconds.", ex.Message);
    }

    [Fact]
    public void Tampered_and_malformed_tokens_fail_like_jjwt()
    {
        var util = Util(1792369700000);
        var tampered = SpringToken[..^2] + (SpringToken[^2] == 'A' ? "Bg" : "Ag");
        Assert.Equal("io.jsonwebtoken.security.SignatureException", Assert.Throws<JavaException>(() => util.ExtractUsername(tampered)).JavaType);

        var malformed = Assert.Throws<JavaException>(() => util.ExtractUsername("abc"));
        Assert.Equal("io.jsonwebtoken.MalformedJwtException", malformed.JavaType);
        Assert.Equal("Invalid compact JWT string: Compact JWSs must contain exactly 2 period characters, and compact JWEs must contain exactly 4.  Found: 0", malformed.Message);

        Assert.Equal("CharSequence cannot be null or empty.", Assert.Throws<Compat.IllegalArgumentException>(() => util.ExtractUsername("")).Message);
    }

    private sealed class FixedTime(long millis) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(millis);
    }
}

public class PasswordEncoderTests
{
    // BCryptPasswordEncoder (spring-security-crypto 6.5.6).encode("Secret123!")
    private const string SpringHash = "$2a$10$EzMoqp0E9oaPON85VSbKROsQ58Lc/oIrj/gFmZUMpbU3zEh4fq0QK";

    [Fact]
    public void Matches_hashes_produced_by_spring()
    {
        var encoder = new PasswordEncoder();
        Assert.True(encoder.Matches("Secret123!", SpringHash));
        Assert.False(encoder.Matches("secret123!", SpringHash));
    }

    [Fact]
    public void Encodes_in_springs_format_and_rejects_non_bcrypt_hashes()
    {
        var encoder = new PasswordEncoder();
        var hash = encoder.Encode("Secret123!");
        Assert.StartsWith("$2a$10$", hash);
        Assert.True(encoder.Matches("Secret123!", hash));
        // e.g. a leftover Django hash: Spring logs "Encoded password does not look like BCrypt" and returns false
        Assert.False(encoder.Matches("x", "pbkdf2_sha256$600000$abc$def"));
    }
}

public class JavaFormatTests
{
    [Theory]
    [InlineData(2026, 10, 18, 19, 27, 0, 0, "2026-10-18T19:27")]
    [InlineData(2026, 10, 18, 19, 27, 5, 0, "2026-10-18T19:27:05")]
    [InlineData(2026, 10, 18, 19, 27, 21, 1200000, "2026-10-18T19:27:21.120")]
    [InlineData(2026, 10, 18, 19, 27, 21, 8608380, "2026-10-18T19:27:21.860838")]
    [InlineData(2026, 10, 18, 19, 27, 21, 8608381, "2026-10-18T19:27:21.860838100")]
    public void LocalDateTime_toString(int y, int mo, int d, int h, int mi, int s, long ticks, string expected)
    {
        var value = new DateTime(y, mo, d, h, mi, s).AddTicks(ticks);
        Assert.Equal(expected, JavaFormat.LocalDateTimeToString(value));
    }

    [Theory]
    [InlineData(2026, 10, 18, 19, 27, 0, 0, "2026-10-18T19:27:00")]
    [InlineData(2026, 10, 18, 19, 27, 37, 2738550, "2026-10-18T19:27:37.273855")]
    [InlineData(2026, 10, 18, 19, 27, 37, 2000000, "2026-10-18T19:27:37.2")]
    public void Jackson_iso_local_date_time(int y, int mo, int d, int h, int mi, int s, long ticks, string expected)
    {
        Assert.Equal(expected, JavaFormat.IsoLocalDateTime(new DateTime(y, mo, d, h, mi, s).AddTicks(ticks)));
    }

    [Theory]
    [InlineData(90000.0, "90000.0")]
    [InlineData(45000.5, "45000.5")]
    [InlineData(0.001, "0.001")]
    [InlineData(1e7, "1.0E7")]
    [InlineData(12345678.9, "1.23456789E7")]
    [InlineData(1.5e-5, "1.5E-5")]
    [InlineData(0.0, "0.0")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(100.0, "100.0")]
    public void Double_toString(double value, string expected) => Assert.Equal(expected, JavaFormat.Double(value));

    [Fact]
    public void Address_toString_prints_null_like_java()
    {
        Assert.Equal("Calle 1 2-3 Centro, Neiva", new Address { Street = "Calle 1", StreetNumber = "2-3", Distric = "Centro", City = "Neiva" }.ToString());
        Assert.Equal("null 2 Sur", new Address { Street = null!, StreetNumber = "2", Distric = "Sur" }.ToString());
    }
}

public class JavaHashTests
{
    [Fact]
    public void String_hash_codes_match_java() // values from java.lang.String#hashCode
    {
        Assert.Equal(2484017, JavaHash.Of("Peso"));
        Assert.Equal(-1924752694, JavaHash.Of("Origen"));
        Assert.Equal(0, JavaHash.Of(""));
    }

    [Theory] // attribute orders returned by the Spring backend for maps loaded by Hibernate
    [InlineData(new[] { "Marca", "Peso" }, new[] { "Peso", "Marca" })]
    [InlineData(new[] { "Origen", "Peso" }, new[] { "Origen", "Peso" })]
    [InlineData(new[] { "Alfa", "Peso", "Zeta" }, new[] { "Alfa", "Zeta", "Peso" })]
    [InlineData(new[] { "Color", "Garantia", "Marca", "Material", "Origen", "Peso", "Talla" },
        new[] { "Garantia", "Peso", "Marca", "Color", "Talla", "Material", "Origen" })]
    public void Loaded_map_order_matches_hibernate(string[] rowsInResultOrder, string[] expected)
    {
        Assert.Equal(expected, JavaHash.LoadedOrder(rowsInResultOrder, JavaHash.Of));
    }

    [Fact]
    public void Cart_item_map_order_matches_spring() // GET /user/cart (new HashMap<>())
    {
        string[] inserted = ["id", "productId", "variantId", "name", "price", "slug", "quantity", "variantDetails", "stock"];
        Assert.Equal(["quantity", "variantDetails", "productId", "price", "name", "id", "variantId", "stock", "slug"],
            JavaHash.DefaultMapOrder(inserted));
    }
}

public class OrderStatusTests
{
    [Theory]
    [InlineData("PENDING", OrderStatus.PENDING)]
    [InlineData("pendiente", OrderStatus.PENDING)]
    [InlineData("En Preparacion", OrderStatus.PROCESSING)]
    [InlineData("Procesando", OrderStatus.PROCESSING)]
    [InlineData("shipped", OrderStatus.SHIPPED)]
    public void FromString_accepts_names_and_descriptions(string value, OrderStatus expected) =>
        Assert.Equal(expected, OrderStatusExtensions.FromString(value));

    [Fact]
    public void Errors_have_the_java_messages()
    {
        Assert.Equal("Unknown status: xyz", Assert.Throws<Compat.IllegalArgumentException>(() => OrderStatusExtensions.FromString("xyz")).Message);
        Assert.Equal("No enum constant com.todoteg.model.OrderStatus.PENDIENTE",
            Assert.Throws<Compat.IllegalArgumentException>(() => OrderStatusExtensions.ValueOf("PENDIENTE")).Message);
    }

    [Fact]
    public void Database_values_fall_back_to_pending()
    {
        Assert.Equal(OrderStatus.SHIPPED, OrderStatusExtensions.FromDatabase("Enviado"));
        Assert.Equal(OrderStatus.SHIPPED, OrderStatusExtensions.FromDatabase("SHIPPED"));
        Assert.Equal(OrderStatus.PROCESSING, OrderStatusExtensions.FromDatabase("En Preparacion"));
        Assert.Equal(OrderStatus.PENDING, OrderStatusExtensions.FromDatabase("legacy"));
    }
}

public class JsonCompatibilityTests
{
    [Fact]
    public void Spring_page_json_has_pageimpl_shape()
    {
        var page = new SpringPage<int>([1, 2], PageRequest.Of(0, 8, sorted: true), 2);
        Assert.Equal(
            """{"content":[1,2],"pageable":{"pageNumber":0,"pageSize":8,"sort":{"sorted":true,"unsorted":false,"empty":false},"offset":0,"paged":true,"unpaged":false},"totalElements":2,"totalPages":1,"last":true,"size":8,"number":0,"sort":{"sorted":true,"unsorted":false,"empty":false},"numberOfElements":2,"first":true,"empty":false}""",
            JsonSerializer.Serialize(page, JsonConfig.Options));
    }

    [Fact]
    public void Empty_page_like_spring()
    {
        var json = JsonSerializer.Serialize(new SpringPage<int>([], PageRequest.Of(0, 20), 0), JsonConfig.Options);
        Assert.Contains("\"totalElements\":0,\"totalPages\":0,\"last\":true", json);
        Assert.Contains("\"sort\":{\"sorted\":false,\"unsorted\":true,\"empty\":true}", json);
    }

    [Fact]
    public void Page_request_validation_messages()
    {
        Assert.Equal("Page index must not be less than zero", Assert.Throws<Compat.IllegalArgumentException>(() => PageRequest.Of(-1, 8)).Message);
        Assert.Equal("Page size must not be less than one", Assert.Throws<Compat.IllegalArgumentException>(() => PageRequest.Of(0, 0)).Message);
    }

    [Fact]
    public void Jackson_style_output_and_lenient_input()
    {
        var detail = new OrderDetailResponse { Id = 1, Amount = 90000.0, Date = new DateOnly(2026, 10, 18), Status = "Pendiente" };
        Assert.Equal("""{"id":1,"shopping":null,"user":null,"amount":90000.0,"date":"2026-10-18","status":"Pendiente"}""",
            JsonSerializer.Serialize(detail, JsonConfig.Options));

        var fav = JsonSerializer.Deserialize<FavoriteCreateRequest>("""{"account":"","product":"7"}""", JsonConfig.Options)!;
        Assert.Null(fav.Account);
        Assert.Equal(7, fav.Product);

        var user = JsonSerializer.Deserialize<UserCreateRequest>("""{"name":123,"isSuperuser":null}""", JsonConfig.Options)!;
        Assert.Equal("123", user.Name);
        Assert.Null(user.IsSuperuser);
        Assert.False(JsonSerializer.Deserialize<UserCreateRequest>("{}", JsonConfig.Options)!.IsSuperuser);
    }

    [Fact]
    public void Fields_with_json_property_come_last_like_jackson()
    {
        var json = JsonSerializer.Serialize(new FavoriteProductDTO { Id = 2, IdFa = 1 }, JsonConfig.Options);
        Assert.StartsWith("{\"id\":2,", json);
        Assert.EndsWith("\"idFa\":1}", json);
    }
}

public class ValidationTests
{
    [Fact]
    public void Message_matches_method_argument_not_valid_exception()
    {
        var ex = Assert.Throws<JavaException>(() => RequestValidator.Validate(new FavoriteCreateRequest { Product = 1 }, "favoriteCreateRequest",
            "public org.springframework.http.ResponseEntity<java.util.Map<java.lang.String, java.lang.Long>> com.todoteg.controller.FavoriteController.addFavorite(com.todoteg.dto.FavoriteCreateRequest)"));
        Assert.Equal("org.springframework.web.bind.MethodArgumentNotValidException", ex.JavaType);
        // captured from the Spring backend
        Assert.Equal(
            "Validation failed for argument [0] in public org.springframework.http.ResponseEntity<java.util.Map<java.lang.String, java.lang.Long>> " +
            "com.todoteg.controller.FavoriteController.addFavorite(com.todoteg.dto.FavoriteCreateRequest): [Field error in object 'favoriteCreateRequest' " +
            "on field 'account': rejected value [null]; codes [NotNull.favoriteCreateRequest.account,NotNull.account,NotNull.java.lang.Long,NotNull]; " +
            "arguments [org.springframework.context.support.DefaultMessageSourceResolvable: codes [favoriteCreateRequest.account,account]; arguments []; " +
            "default message [account]]; default message [no debe ser nulo]] ",
            ex.Message);
    }

    [Fact]
    public void Several_errors_are_counted()
    {
        var ex = Assert.Throws<JavaException>(() => RequestValidator.Validate(new OrderCreateRequest { Account = 2 }, "orderCreateRequest", "sig"));
        Assert.StartsWith("Validation failed for argument [0] in sig with 3 errors: [Field error", ex.Message);
    }

    [Theory]
    [InlineData("user@kiogloss.com", true)]
    [InlineData("a@b", true)]
    [InlineData("notanemail", false)]
    [InlineData("a@@b.com", false)]
    [InlineData("", true)]
    public void Hibernate_email_rules(string email, bool valid) => Assert.Equal(valid, HibernateEmail.IsValid(email));

    [Fact]
    public void Phone_pattern_is_checked_on_persist()
    {
        EntityValidator.ValidateUserOnPersist("a@b.com", "3001234567");
        var ex = Assert.Throws<JavaException>(() => EntityValidator.ValidateUserOnPersist("a@b.com", "12ab"));
        Assert.Equal("jakarta.validation.ConstraintViolationException", ex.JavaType);
        Assert.Contains("propertyPath=phoneNumber", ex.Message);
        Assert.Equal("org.springframework.transaction.TransactionSystemException",
            Assert.Throws<JavaException>(() => EntityValidator.ValidateUserOnUpdate("a@b.com", "12ab")).JavaType);
    }
}
