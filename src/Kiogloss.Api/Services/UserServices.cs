// ===== GUÍA RÁPIDA DE ESTE ARCHIVO =====
// Reglas de usuarios: iniciar sesión, registrarse, editar el perfil, roles y recuperar la contraseña por correo.
// ======================================

using Kiogloss.Api.Compat;
using Kiogloss.Api.Data;
using Kiogloss.Api.Dtos;
using Kiogloss.Api.Dtos.Admin;
using Kiogloss.Api.Models;
using Kiogloss.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Kiogloss.Api.Services;

/// <summary>Inicio de sesión: revisa el correo y la contraseña y entrega las llaves (tokens).</summary>
public sealed class AuthService(KioglossDbContext db, JwtUtil jwtUtil, PasswordEncoder passwordEncoder)
{
    /// <summary>Autentica con email y contraseña y devuelve los tokens de acceso y de refresco.</summary>
    public async Task<TokenResponse> Login(LoginRequest request)
    {
        await Authenticate(request.Email, request.Password ?? "");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
                   ?? throw JavaErrors.UsernameNotFound("User not found");

        var accessToken = jwtUtil.GenerateAccessToken(user);
        var refreshToken = jwtUtil.GenerateRefreshToken(user);
        return new TokenResponse(accessToken, refreshToken);
    }

    /// <summary>Emite un nuevo token de acceso a partir de un refresh token válido.</summary>
    public async Task<TokenResponse> Refresh(string? refreshToken)
    {
        var email = jwtUtil.ExtractUsername(refreshToken!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email)
                   ?? throw JavaErrors.UsernameNotFound("User not found");

        if (!jwtUtil.ValidateToken(refreshToken!, user))
            throw new Compat.RuntimeException("Invalid refresh token");

        var newAccessToken = jwtUtil.GenerateAccessToken(user);
        return new TokenResponse(newAccessToken, refreshToken!);
    }

    /// <summary>
    /// authenticationManager.authenticate(...) with DaoAuthenticationProvider: unknown user -> Bad credentials,
    /// disabled account checked before the password, then BCrypt match.
    /// </summary>
    private async Task Authenticate(string? email, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) throw JavaErrors.BadCredentials();
        if (!user.IsEnabled()) throw JavaErrors.Disabled();
        if (!passwordEncoder.Matches(password, user.Password)) throw JavaErrors.BadCredentials();
        _ = user.GetAuthorities();
    }
}

/// <summary>Manejo de usuarios: registro, perfil, lista con búsqueda, roles y activar o desactivar.</summary>
public sealed class UserService(
    KioglossDbContext db,
    PasswordEncoder passwordEncoder,
    CloudinaryService cloudinaryService)
{
    private static readonly Dictionary<string, System.Linq.Expressions.Expression<Func<UserProfile, object?>>> SortableProperties = new()
    {
        ["id"] = u => u.Id,
        ["email"] = u => u.Email,
        ["name"] = u => u.Name,
        ["password"] = u => u.Password,
        ["isActive"] = u => u.IsActive,
        ["isStaff"] = u => u.IsStaff,
        ["isSuperuser"] = u => u.IsSuperuser,
        ["profileImage"] = u => u.ProfileImage,
        ["phoneNumber"] = u => u.PhoneNumber,
    };

    /// <summary>Registra un usuario con su cuenta; valida que el email sea único.</summary>
    public async Task<UserProfile> CreateUser(UserCreateRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        // Validar email único
        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            throw new Compat.RuntimeException("Email already exists");

        // Crear Address solo si viene en la petición
        Address? address = null;
        if (request.Address != null)
        {
            address = new Address
            {
                Street = request.Address.Street!,
                StreetNumber = request.Address.StreetNumber!,
                Distric = request.Address.Distric!,
                City = request.Address.City,
            };
            db.Addresses.Add(address);
            await db.SaveChangesAsync();
        }

        var user = new UserProfile
        {
            Email = request.Email!,
            Name = request.Name!,
            Password = passwordEncoder.Encode(request.Password!),
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            IsStaff = false,
            // Registro público: nunca se acepta isSuperuser del cliente; los admins se asignan desde el panel
            IsSuperuser = false,
        };

        // Manejar imagen de perfil Base64
        if (!string.IsNullOrEmpty(request.ProfileImage))
            user.ProfileImage = await cloudinaryService.UploadBase64Image(request.ProfileImage);

        EntityValidator.ValidateUserOnPersist(user.Email, user.PhoneNumber);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var account = new Account { UserId = user.Id, AddressId = address?.Id };
        if (request.Account != null)
        {
            account.PointsPerPurchase = request.Account.PointsPerPurchase ?? 0;
            account.IsActive = request.Account.IsActive ?? true;
        }
        else
        {
            account.PointsPerPurchase = 0;
            account.IsActive = true;
        }
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        await tx.CommitAsync();
        return user;
    }

    /// <summary>Obtiene el detalle completo del usuario con su cuenta y dirección.</summary>
    public async Task<UserDetailResponse> GetUserDetail(long userId)
    {
        var user = await db.Users.FindAsync(userId) ?? throw new Compat.RuntimeException("User not found");
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == userId)
                      ?? throw new Compat.RuntimeException("Account not found");

        // Obtener favoritos
        var favoritos = await db.Favorites.Where(f => f.AccountId == account.Id).ToListAsync();

        object favoriteData;
        if (favoritos.Count == 0)
        {
            favoriteData = "No tienes favoritos";
        }
        else
        {
            var list = new List<FavoriteProductDTO>();
            foreach (var fav in favoritos)
            {
                var product = fav.Product;
                var images = await db.Images.Where(i => i.ProductId == product.Id).ToListAsync();
                var hasRealVariants = product.Variants.Any(v => v.Options.Count > 0);
                long? defaultVariantId = null;
                if (!hasRealVariants && product.Variants.Count > 0)
                    defaultVariantId = product.Variants.OrderBy(v => v.Id).First().Id;

                list.Add(new FavoriteProductDTO
                {
                    IdFa = fav.Id,
                    Id = product.Id,
                    Name = product.Title,
                    Price = JavaFormat.BigDecimal(product.Price),
                    Slug = product.Slug,
                    Images = images.Select(img => cloudinaryService.GetImageUrl(img.Image)).ToList(),
                    HasVariants = hasRealVariants,
                    DefaultVariantId = defaultVariantId,
                });
            }
            favoriteData = list;
        }

        return new UserDetailResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            ProfileImage = cloudinaryService.GetImageUrl(user.ProfileImage),
            PhoneNumber = user.PhoneNumber,
            IsSuperuser = user.IsSuperuser,
            Authorities = user.GetAuthorities(),
            Account = new AccountInfoDTO
            {
                Id = account.Id,
                Favorite = favoriteData,
                PointsPerPurchase = account.PointsPerPurchase,
                Address = account.Address != null ? account.Address.ToString() : "",
                City = account.Address != null ? account.Address.City : "",
                IsActive = account.IsActive,
            },
        };
    }

    /// <summary>Actualiza los datos del usuario, su cuenta y su dirección.</summary>
    public async Task UpdateUser(long userId, UserUpdateRequest request)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await db.Users.FindAsync(userId) ?? throw new Compat.RuntimeException("User not found");

        if (request.Name != null) user.Name = request.Name;
        if (request.Password != null) user.Password = passwordEncoder.Encode(request.Password);
        if (request.PhoneNumber != null) user.PhoneNumber = request.PhoneNumber;
        if (!string.IsNullOrEmpty(request.ProfileImage))
            user.ProfileImage = await cloudinaryService.UploadBase64Image(request.ProfileImage);

        // Actualizar dirección si viene en la petición
        if (request.Address != null)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.UserId == userId)
                          ?? throw new Compat.RuntimeException("Account not found");
            var address = account.Address ?? new Address();
            address.Street = request.Address.Street!;
            address.StreetNumber = request.Address.StreetNumber!;
            address.Distric = request.Address.Distric!;
            address.City = request.Address.City;
            if (address.Id == 0)
            {
                // new address: persisted immediately (IDENTITY insert), like addressRepository.save
                db.Addresses.Add(address);
                await db.SaveChangesAsync();
            }
            account.Address = address;
        }

        // flush at commit: Hibernate validates the entity and Spring wraps failures in TransactionSystemException
        EntityValidator.ValidateUserOnUpdate(user.Email, user.PhoneNumber);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>Buscar todos los usuarios con paginación y búsqueda opcional.</summary>
    public Task<SpringPage<UserProfile>> FindAllUsers(string? search, PageRequest pageable, string sortBy, bool descending)
    {
        IQueryable<UserProfile> query = db.Users;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + EscapeLike(search) + "%";
            query = query.Where(u => EF.Functions.ILike(u.Name, pattern, "\\") || EF.Functions.ILike(u.Email, pattern, "\\"));
        }
        if (!SortableProperties.TryGetValue(sortBy, out var key))
            throw JavaErrors.PropertyReference(sortBy, "UserProfile");
        // desempate por Id: orden estable entre páginas aunque haya valores repetidos
        return query.OrderByProperty(key, descending).ThenBy(u => u.Id).ToSpringPageAsync(pageable);
    }

    /// <summary>Obtiene un usuario por id; lanza <see cref="ResourceNotFoundException"/> si no existe.</summary>
    public async Task<UserProfile> FindById(long id) =>
        await db.Users.FindAsync(id) ?? throw new ResourceNotFoundException("Usuario no encontrado con ID: " + id);

    /// <summary>Actualiza los indicadores de rol (staff, superusuario, activo) que vengan informados.</summary>
    public async Task<UserProfile> UpdateUserRoles(long id, UpdateUserRolesDTO updateDTO)
    {
        var user = await FindById(id);
        if (updateDTO.IsStaff != null) user.IsStaff = updateDTO.IsStaff;
        if (updateDTO.IsSuperuser != null) user.IsSuperuser = updateDTO.IsSuperuser;
        if (updateDTO.IsActive != null) user.IsActive = updateDTO.IsActive;
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Activa o desactiva un usuario.</summary>
    public async Task<UserProfile> ToggleUserStatus(long id)
    {
        var user = await FindById(id);
        user.IsActive = !user.IsEnabled();
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Calcula las estadísticas del dashboard (usuarios totales y activos).</summary>
    public async Task<DashboardStatsDTO> GetDashboardStats() => new()
    {
        TotalUsers = await db.Users.LongCountAsync(),
        ActiveUsers = await db.Users.LongCountAsync(u => u.IsActive == true),
    };

    /// <summary>Spring Data escapes LIKE wildcards for *Containing* derived queries.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

/// <summary>Recuperar la contraseña en 2 pasos: pedir un enlace por correo y confirmar con el código recibido.</summary>
public sealed class PasswordResetService(
    KioglossDbContext db,
    PasswordEncoder passwordEncoder,
    EmailService emailService,
    ILogger<PasswordResetService> log)
{
    /// <summary>
    /// Paso 1: El usuario pide el reset. Generamos un token, lo guardamos y enviamos el email.
    /// Siempre respondemos igual para no revelar si el email existe.
    /// </summary>
    public async Task RequestReset(string? email)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null)
        {
            // Eliminar tokens anteriores del usuario para que solo haya uno vigente
            await db.PasswordResetTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();

            // Generar token único con expiración de 30 minutos
            var token = Guid.NewGuid().ToString();
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Token = token,
                UserId = user.Id,
                ExpiresAt = DateTime.Now.AddMinutes(30),
                Used = false,
            });
            await db.SaveChangesAsync();

            await emailService.SendPasswordResetEmail(user.Email, user.Name, token);
        }
        await tx.CommitAsync();
    }

    /// <summary>Paso 2: El usuario manda el token + nueva contraseña.</summary>
    public async Task ConfirmReset(string? token, string? newPassword)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var resetToken = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.Token == token)
                         ?? throw new Compat.RuntimeException("Token inválido o no encontrado");

        if (resetToken.Used)
            throw new Compat.RuntimeException("Este enlace ya fue utilizado");

        if (resetToken.ExpiresAt < DateTime.Now)
            throw new Compat.RuntimeException("El enlace ha expirado. Solicita uno nuevo");

        var user = resetToken.User;
        user.Password = passwordEncoder.Encode(newPassword!);
        resetToken.Used = true;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        log.LogInformation("Contraseña restablecida para: {Email}", user.Email);
    }
}
