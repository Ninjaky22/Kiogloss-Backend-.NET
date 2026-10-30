using Kiogloss.Api.Background;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Configuration;
using Kiogloss.Api.Data;
using Kiogloss.Api.Mappers;
using Kiogloss.Api.Security;
using Kiogloss.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Local overrides with the real secrets (git-ignored), mirroring the untracked application.properties.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

const long OneGigabyte = 1L * 1024 * 1024 * 1024;
var contextPath = builder.Configuration["Server:ContextPath"] ?? "/API/V1.0";

// spring.servlet.multipart.max-file-size / max-request-size = 1GB
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = OneGigabyte);
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = OneGigabyte;
    o.ValueLengthLimit = int.MaxValue;
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<CloudinaryOptions>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.Configure<MailOptions>(builder.Configuration.GetSection("Mail"));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));

builder.Services.AddDbContext<KioglossDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseLazyLoadingProxies());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JwtUtil>();
builder.Services.AddSingleton<PasswordEncoder>();
builder.Services.AddSingleton<CloudinaryService>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<SseEmitterRegistry>();
builder.Services.AddSingleton<UserMapper>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddScoped<VideoService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddHostedService<SseHeartbeatService>();
builder.Services.AddHostedService<DatabaseSeeder>();

builder.Services.AddAuthentication(SpringJwtAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SpringJwtAuthenticationHandler>(SpringJwtAuthenticationHandler.SchemeName, null);
builder.Services.AddControllers().AddJsonOptions(o => JsonConfig.Configure(o.JsonSerializerOptions));

var app = builder.Build();

// Esquema incremental: dirección de entrega en órdenes (idempotente, para bases ya creadas)
try
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<KioglossDbContext>().Database
        .ExecuteSqlRaw("ALTER TABLE store_order ADD COLUMN IF NOT EXISTS shipping_address character varying(255)");
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "No se pudo asegurar la columna store_order.shipping_address");
}

// server.servlet.context-path: nothing is served outside of it
app.UsePathBase(contextPath);
app.Use((context, next) =>
{
    if (context.Request.PathBase.HasValue) return next(context);
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return Task.CompletedTask;
});

// Spring Security filter chain order: headers -> CORS -> JWT filter -> authorization -> DispatcherServlet
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<SpringCorsMiddleware>();
app.UseAuthentication();
app.UseMiddleware<UrlAuthorizationMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

// WebConfig#addResourceHandlers: /uploads/** -> app.upload.dir
var uploadDir = app.Services.GetRequiredService<FileStorageService>().UploadDir;
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".avif"] = "image/avif";
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/uploads",
    FileProvider = new PhysicalFileProvider(uploadDir),
    ContentTypeProvider = contentTypes,
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream",
});

app.UseRouting();
app.UseMiddleware<SpringRoutingMiddleware>();
app.MapControllers();

app.Run();

public partial class Program;
