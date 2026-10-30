using Kiogloss.Api.Compat;

namespace Kiogloss.Api.Models;

/// <summary>store_address.</summary>
public class Address
{
    public long Id { get; set; }
    public string Street { get; set; } = null!;
    public string StreetNumber { get; set; } = null!;
    public string Distric { get; set; } = null!;
    public string? City { get; set; }

    /// <summary>Address#toString — used as the "address" string of several responses.</summary>
    public override string ToString() =>
        JavaFormat.Str(Street) + " " + JavaFormat.Str(StreetNumber) + " " + JavaFormat.Str(Distric) +
        (City != null ? ", " + City : "");
}
