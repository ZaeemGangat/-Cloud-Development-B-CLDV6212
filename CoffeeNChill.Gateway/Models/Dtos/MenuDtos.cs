using System.ComponentModel.DataAnnotations;
using CoffeeNChill.Gateway.Models;

namespace CoffeeNChill.Gateway.Models.Dtos;

/// <summary>Request body for POST /api/menu.</summary>
public class MenuItemCreateRequest
{
    /// <summary>Menu category - the table PartitionKey.</summary>
    /// <example>Hot Drinks</example>
    [Required, StringLength(50, MinimumLength = 2)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Unique item code - the table RowKey.</summary>
    /// <example>COF-001</example>
    [Required, RegularExpression(@"^[A-Za-z0-9\-]{2,20}$", ErrorMessage = "SKU may only contain letters, digits and dashes (2-20 characters).")]
    public string Sku { get; set; } = string.Empty;

    /// <example>Espresso</example>
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    /// <example>Single shot of rich espresso</example>
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Price in Rand.</summary>
    /// <example>25.50</example>
    [Range(0, 10000)]
    public double Price { get; set; }

    /// <example>true</example>
    public bool IsAvailable { get; set; } = true;
}

/// <summary>Request body for PUT /api/menu/{category}/{sku}. Only supplied fields change.</summary>
public class MenuItemUpdateRequest
{
    /// <example>28.00</example>
    [Range(0, 10000)]
    public double? Price { get; set; }

    /// <example>false</example>
    public bool? IsAvailable { get; set; }

    [StringLength(100, MinimumLength = 2)]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }
}

/// <summary>A menu item as returned to clients.</summary>
public class MenuItemResponse
{
    public string Category { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool IsAvailable { get; set; }
    public DateTimeOffset? LastModified { get; set; }

    public static MenuItemResponse FromEntity(MenuItemEntity entity) => new()
    {
        Category = entity.PartitionKey,
        Sku = entity.RowKey,
        Name = entity.Name,
        Description = entity.Description,
        Price = entity.Price,
        IsAvailable = entity.IsAvailable,
        LastModified = entity.Timestamp
    };
}
