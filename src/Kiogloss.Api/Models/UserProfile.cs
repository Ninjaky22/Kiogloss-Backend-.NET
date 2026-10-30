using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

/// <summary>store_userprofile. Also plays the role of Spring Security's UserDetails.</summary>
public class UserProfile
{
    public long Id { get; set; }
    public string Email { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Password { get; set; } = null!;
    public bool? IsActive { get; set; } = true;
    public bool? IsStaff { get; set; } = false;
    public bool? IsSuperuser { get; set; } = false;
    public string? ProfileImage { get; set; } = "profile_p8im0u.png";
    public string? PhoneNumber { get; set; }

    public virtual Account? Account { get; set; }

    /// <summary>UserDetails#getAuthorities (unboxes isSuperuser like the Java code did).</summary>
    public IReadOnlyList<string> GetAuthorities() =>
        (IsSuperuser ?? throw JavaErrors.NullPointer(
            "Cannot invoke \"java.lang.Boolean.booleanValue()\" because \"this.isSuperuser\" is null"))
            ? ["ROLE_ADMIN", "ROLE_USER"]
            : ["ROLE_USER"];

    /// <summary>UserDetails#isEnabled.</summary>
    public bool IsEnabled() =>
        IsActive ?? throw JavaErrors.NullPointer(
            "Cannot invoke \"java.lang.Boolean.booleanValue()\" because \"this.isActive\" is null");
}
