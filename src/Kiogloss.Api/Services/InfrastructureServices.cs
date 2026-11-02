// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Conexión con cosas de afuera: Cloudinary (donde se guardan las imágenes), la carpeta de archivos y el envío de correos.
// ======================================

using System.Text;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Kiogloss.Api.Compat;
using Kiogloss.Api.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Kiogloss.Api.Services;

/// <summary>Sube imágenes a Cloudinary (un servicio externo para guardar imágenes) y obtiene su dirección web.</summary>
public class CloudinaryService(IOptions<CloudinaryOptions> options)
{
    private readonly CloudinaryOptions _options = options.Value;
    private readonly Lazy<Cloudinary> _cloudinary = new(() =>
        new Cloudinary(new CloudinaryDotNet.Account(options.Value.CloudName, options.Value.ApiKey, options.Value.ApiSecret)));

    /// <summary>
    /// Sube una imagen en Base64 a Cloudinary (puede incluir o no el prefijo data:image...) y devuelve su URL.
    /// </summary>
    public virtual async Task<string> UploadBase64Image(string base64Image)
    {
        // Remover el prefijo "data:image/...;base64," si existe
        var imageData = base64Image;
        if (base64Image.Contains(','))
            imageData = base64Image.Split(',')[1];

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(imageData);
        }
        catch (FormatException)
        {
            throw new Compat.IllegalArgumentException("Illegal base64 character");
        }

        var publicId = "uploads/images/" + Guid.NewGuid();
        using var stream = new MemoryStream(imageBytes);
        var result = await _cloudinary.Value.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(publicId, stream),
            PublicId = publicId,
            Folder = "uploads/images",
        });
        if (result.Error != null)
            throw new JavaException("java.lang.RuntimeException", result.Error.Message);
        return result.SecureUrl?.ToString() ?? throw JavaErrors.NullPointer();
    }

    /// <summary>
    /// Devuelve la URL completa de una imagen almacenada; si ya es una URL completa, la devuelve tal cual.
    /// Relative paths reproduce cloudinary-java's Url#generate (http, forced "v1/" for paths with folders).
    /// </summary>
    public string? GetImageUrl(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;
        if (imagePath.StartsWith("http://", StringComparison.Ordinal) || imagePath.StartsWith("https://", StringComparison.Ordinal))
            return imagePath;

        var version = imagePath.Contains('/') && !System.Text.RegularExpressions.Regex.IsMatch(imagePath, "^v[0-9]+/.*")
            ? "v1/"
            : "";
        return $"http://res.cloudinary.com/{_options.CloudName}/image/upload/{version}{SmartUrlEncode(imagePath)}";
    }

    /// <summary>SmartUrlEncoder.encode: URLEncoder.encode keeping '/' and ':' and using %20 for spaces.</summary>
    private static string SmartUrlEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '*' or '_' or '/' or ':') sb.Append(c);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}

/// <summary>Guarda los archivos subidos (por ejemplo videos) en la carpeta uploads/, que se puede ver en la ruta /uploads.</summary>
public sealed class FileStorageService
{
    private readonly string _uploadDir;

    public FileStorageService(IOptions<AppOptions> options)
    {
        _uploadDir = Path.GetFullPath(options.Value.UploadDir);
        try
        {
            Directory.CreateDirectory(_uploadDir);
        }
        catch (IOException e)
        {
            throw new JavaException("java.lang.RuntimeException", "No se pudo crear el directorio de uploads", e);
        }
    }

    /// <summary>Directorio local donde se almacenan los archivos subidos.</summary>
    public string UploadDir => _uploadDir;

    /// <summary>Guarda el archivo en el directorio de subidas con un nombre UUID y devuelve ese nombre.</summary>
    public async Task<string> Store(IFormFile file)
    {
        var originalName = file.FileName;
        var ext = "";
        if (!string.IsNullOrEmpty(originalName) && originalName.Contains('.'))
            ext = originalName[originalName.LastIndexOf('.')..];
        var filename = Guid.NewGuid() + ext;
        var target = Path.Combine(_uploadDir, filename);
        try
        {
            await using var output = File.Create(target);
            await file.CopyToAsync(output);
        }
        catch (IOException e)
        {
            throw new JavaException("java.lang.RuntimeException", "Error almacenando archivo", e);
        }
        return filename;
    }

    /// <summary>Elimina el archivo si <paramref name="url"/> apunta a una subida local; ignora URLs vacías o externas.</summary>
    public void DeleteIfLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var idx = url.IndexOf("/uploads/", StringComparison.Ordinal);
        if (idx < 0) return;
        var filename = url[(idx + "/uploads/".Length)..];
        try
        {
            var file = Path.GetFullPath(Path.Combine(_uploadDir, filename));
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("No se pudo eliminar archivo: " + filename + " - " + e.Message);
        }
    }
}

/// <summary>Envía correos electrónicos (por ahora, el enlace para recuperar la contraseña).</summary>
public class EmailService(IOptions<MailOptions> mailOptions, IOptions<AppOptions> appOptions, ILogger<EmailService> log)
{
    /// <summary>Envía el correo con el enlace para restablecer la contraseña.</summary>
    public virtual async Task SendPasswordResetEmail(string toEmail, string userName, string token)
    {
        var mail = mailOptions.Value;
        try
        {
            var resetLink = appOptions.Value.FrontendUrl + "/reset-password?token=" + token;
            var html = $"""
                    <div style="font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;">
                        <div style="background-color: #610361; padding: 24px; border-radius: 8px 8px 0 0;">
                            <h1 style="color: white; margin: 0; font-size: 24px;">Kiogloss</h1>
                        </div>
                        <div style="background-color: #f9f9f9; padding: 32px; border-radius: 0 0 8px 8px;">
                            <h2 style="color: #333;">Hola, {userName}</h2>
                            <p style="color: #555; font-size: 16px;">
                                Recibimos una solicitud para restablecer la contraseña de tu cuenta.
                                Haz clic en el botón de abajo para continuar.
                            </p>
                            <div style="text-align: center; margin: 32px 0;">
                                <a href="{resetLink}"
                                   style="background-color: #610361; color: white; padding: 14px 28px;
                                          border-radius: 6px; text-decoration: none; font-size: 16px;
                                          font-weight: bold;">
                                    Restablecer contraseña
                                </a>
                            </div>
                            <p style="color: #888; font-size: 14px;">
                                Este enlace expirará en <strong>30 minutos</strong>.<br>
                                Si no solicitaste este cambio, ignora este correo.
                            </p>
                        </div>
                    </div>

                    """;

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(mail.Username));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Restablecer contraseña — Kiogloss";
            message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(mail.Host, mail.Port, mail.StartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
            await client.AuthenticateAsync(mail.Username, mail.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            log.LogInformation("Email de reset enviado a: {Email}", toEmail);
        }
        catch (Exception e)
        {
            log.LogError("Error enviando email de reset a {Email}: {Error}", toEmail, e.Message);
            throw new Compat.RuntimeException("No se pudo enviar el email. Intenta de nuevo.");
        }
    }
}
