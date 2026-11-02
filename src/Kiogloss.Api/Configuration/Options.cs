// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Lista de ajustes del sistema: claves de acceso, correo, imágenes, etc.
// Los valores no se escriben aquí; se ponen en el archivo appsettings.Local.json o en las variables de entorno.
// ======================================

namespace Kiogloss.Api.Configuration;

/// <summary>jwt.* properties.</summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    /// <summary>Milliseconds (jwt.access-token-expiration).</summary>
    public long AccessTokenExpiration { get; set; } = 300000;
    /// <summary>Milliseconds (jwt.refresh-token-expiration).</summary>
    public long RefreshTokenExpiration { get; set; } = 86400000;
}

/// <summary>cloudinary.* properties.</summary>
public sealed class CloudinaryOptions
{
    public string CloudName { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
}

/// <summary>spring.mail.* properties.</summary>
public sealed class MailOptions
{
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool StartTls { get; set; } = true;
}

/// <summary>app.* properties plus runtime settings that mirror the JVM environment.</summary>
public sealed class AppOptions
{
    /// <summary>app.upload.dir</summary>
    public string UploadDir { get; set; } = "uploads";
    /// <summary>app.frontend-url (link of the password reset email)</summary>
    public string FrontendUrl { get; set; } = "http://localhost:5173";
    /// <summary>JVM default locale used by String.format in notification messages.</summary>
    public string Culture { get; set; } = "es-CO";
    /// <summary>Run DatabaseSeeder on startup (Spring's CommandLineRunner).</summary>
    public bool SeedDatabase { get; set; } = true;
}

/// <summary>notifications.* properties.</summary>
public sealed class NotificationOptions
{
    public int LowStockThreshold { get; set; } = 5;
}
