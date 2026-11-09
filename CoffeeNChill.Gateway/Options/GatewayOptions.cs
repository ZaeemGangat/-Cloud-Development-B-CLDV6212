namespace CoffeeNChill.Gateway.Options;

/// <summary>Azure Storage settings ("Storage" section).</summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Azurite ("UseDevelopmentStorage=true" or the docker-compose connection
    /// string) or a real Azure Storage account connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string UsersTableName { get; set; } = "Users";
    public string MenuTableName { get; set; } = "MenuItems";
    public string OrdersTableName { get; set; } = "Orders";
    public string DocumentsContainerName { get; set; } = "staff-docs";
    public string OrderQueueName { get; set; } = "orderprocessingqueue";
}

/// <summary>JWT signing/validation settings ("Jwt" section).</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Minimum signing key length for HMAC-SHA256 (256 bits).</summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "CoffeeNChill.Gateway";
    public string Audience { get; set; } = "CoffeeNChill.Clients";

    /// <summary>
    /// HMAC-SHA256 signing key. Never committed for production: supplied via
    /// the JWT_KEY value in the VPS .env file (environment variable Jwt__Key).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}

/// <summary>
/// First Admin account created on startup if it doesn't exist ("SeedAdmin" section).
/// Admins then register Staff accounts through POST /api/auth/register.
/// </summary>
public class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = "Canteen Manager";
}
