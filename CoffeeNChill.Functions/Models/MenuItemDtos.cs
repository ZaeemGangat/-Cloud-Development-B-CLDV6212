namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Request body for CreateMenuItem (POST /api/menu).
/// </summary>
public class MenuItemCreateRequest
{
    public string Category { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool IsAvailable { get; set; } = true;
}

/// <summary>
/// Request body for UpdateMenuItem (PUT /api/menu/{category}/{id}).
/// Only Price and/or IsAvailable need to be supplied; both are optional so a
/// caller can update just one field at a time as the spec requires.
/// </summary>
public class MenuItemUpdateRequest
{
    public double? Price { get; set; }
    public bool? IsAvailable { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Response DTO returned to clients - keeps the wire format clean of
/// Azure Table Storage bookkeeping fields (ETag, Timestamp) beyond what's useful.
/// </summary>
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

public class ApiError
{
    public string Message { get; set; } = string.Empty;
}
