using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Gateway.Models;

/// <summary>
/// User profile in the "Users" Azure Table.
/// PartitionKey = Role ("Admin", "Staff" or "Customer");
/// RowKey = email address in lower case (e.g. "barista@coffeenchill.ac.za").
/// </summary>
public class UserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // Role
    public string RowKey { get; set; } = string.Empty;       // Email / username
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    /// <summary>ASP.NET Core Identity PasswordHasher output (PBKDF2-HMAC-SHA512, salted). Never the plain password.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>UTC creation time (Table Storage requires DateTimeKind.Utc).</summary>
    public DateTime CreatedDate { get; set; }

    // Convenience aliases - [IgnoreDataMember] keeps them out of the table columns.
    [IgnoreDataMember] public string Role => PartitionKey;
    [IgnoreDataMember] public string Email => RowKey;
}

/// <summary>
/// Menu item in the "MenuItems" Azure Table (shared with the Functions app).
/// PartitionKey = Category (e.g. "Hot Drinks"), RowKey = SKU (e.g. "COF-001").
/// </summary>
public class MenuItemEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // Category
    public string RowKey { get; set; } = string.Empty;       // SKU
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool IsAvailable { get; set; }
}

/// <summary>
/// Order in the "Orders" Azure Table (shared with the Functions app's queue processor).
/// PartitionKey = order date (yyyy-MM-dd UTC), RowKey = order ID.
/// </summary>
public class OrderEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // yyyy-MM-dd
    public string RowKey { get; set; } = string.Empty;       // Order ID
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>JSON array string - Table Storage has no array type.</summary>
    public string SelectedItemSKUs { get; set; } = "[]";

    public double TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
    public string Status { get; set; } = OrderStatus.Received;
    public DateTimeOffset LastUpdated { get; set; }
    public string UpdatedBy { get; set; } = "system";
}
