using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Azure Table Storage entity for the "MenuItems" table.
/// PartitionKey = Category (e.g. "Hot Drinks"), RowKey = Item SKU/ID (e.g. "COF-001").
/// </summary>
public class MenuItemEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // Category
    public string RowKey { get; set; } = string.Empty;       // SKU / ID
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool IsAvailable { get; set; }
}
