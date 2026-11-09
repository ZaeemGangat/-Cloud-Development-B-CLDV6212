namespace CoffeeNChill.Gateway.Auth;

/// <summary>
/// Role names. These are also the PartitionKey values of the Users table.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";

    /// <summary>For [Authorize(Roles = Roles.AdminOrStaff)] - baristas and canteen managers.</summary>
    public const string AdminOrStaff = Admin + "," + Staff;

    public static readonly IReadOnlyList<string> All = new[] { Admin, Staff, Customer };

    /// <summary>Returns the canonical role name (case-insensitive match) or null if unknown.</summary>
    public static string? Normalize(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
}
