using Azure;
using Azure.Data.Tables;

namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Azure Table Storage entity for the "Orders" table.
/// PartitionKey = order date (yyyy-MM-dd, UTC), RowKey = order ID (e.g. "ORD-2026-8801").
/// The same schema is used by the Part 3 gateway.
/// </summary>
public class OrderEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty; // Order date yyyy-MM-dd
    public string RowKey { get; set; } = string.Empty;       // Order ID
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Table Storage has no array type, so SKUs are stored as a JSON array string.</summary>
    public string SelectedItemSKUs { get; set; } = "[]";

    public double TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
    public string Status { get; set; } = OrderStatus.Received;
    public DateTimeOffset LastUpdated { get; set; }
    public string UpdatedBy { get; set; } = "system";
}
