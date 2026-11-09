using System.Text.Json;

namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Request body for POST /api/orders/queue on the Functions host.
/// </summary>
public class SubmitOrderRequest
{
    public string? OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public List<string> SelectedItemSKUs { get; set; } = new();
    public double TotalPrice { get; set; }
    public DateTimeOffset? OrderTimestamp { get; set; }
}

/// <summary>
/// Message placed on the "orderprocessingqueue" Azure Storage Queue.
/// The gateway sends exactly the same JSON shape.
/// </summary>
public class OrderQueueMessage
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderDate { get; set; } = string.Empty; // yyyy-MM-dd (table PartitionKey)
    public string CustomerName { get; set; } = string.Empty;
    public List<string> SelectedItemSKUs { get; set; } = new();
    public double TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
}

/// <summary>
/// Response DTO for an order (hides Table Storage bookkeeping fields).
/// </summary>
public class OrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderDate { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public List<string> SelectedItemSKUs { get; set; } = new();
    public double TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    public static OrderResponse FromEntity(OrderEntity entity) => new()
    {
        OrderId = entity.RowKey,
        OrderDate = entity.PartitionKey,
        CustomerName = entity.CustomerName,
        SelectedItemSKUs = JsonSerializer.Deserialize<List<string>>(entity.SelectedItemSKUs) ?? new(),
        TotalPrice = entity.TotalPrice,
        OrderTimestamp = entity.OrderTimestamp,
        Status = entity.Status,
        LastUpdated = entity.LastUpdated,
        UpdatedBy = entity.UpdatedBy
    };
}
