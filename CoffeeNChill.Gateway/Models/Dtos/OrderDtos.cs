using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CoffeeNChill.Gateway.Models;

namespace CoffeeNChill.Gateway.Models.Dtos;

/// <summary>Request body for POST /api/orders/queue (no login required).</summary>
public class SubmitOrderRequest
{
    /// <summary>Optional. Generated (e.g. ORD-20261012-3F9A2C) when omitted.</summary>
    /// <example>ORD-2026-8801</example>
    [RegularExpression(@"^[A-Za-z0-9\-]{3,40}$", ErrorMessage = "orderId may only contain letters, digits and dashes.")]
    public string? OrderId { get; set; }

    /// <example>Jane Smith</example>
    [Required, StringLength(100, MinimumLength = 2)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>SKUs of the items ordered. Repeat a SKU to order it more than once.</summary>
    /// <example>["COF-001", "PAS-104"]</example>
    [Required, MinLength(1), MaxLength(20)]
    public List<string> SelectedItemSKUs { get; set; } = new();

    /// <summary>Optional. Defaults to now (UTC).</summary>
    /// <example>2026-10-12T10:15:00Z</example>
    public DateTimeOffset? OrderTimestamp { get; set; }
}

/// <summary>Returned (202 Accepted) once an order is on the processing queue.</summary>
public class OrderAcceptedResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>Calculated by the gateway from current menu prices (client prices are never trusted).</summary>
    public double TotalPrice { get; set; }

    /// <summary>Relative URL the student can poll to track the order.</summary>
    public string TrackingUrl { get; set; } = string.Empty;
}

/// <summary>Request body for PUT /api/orders/status (Admin/Staff).</summary>
public class UpdateOrderStatusRequest
{
    /// <example>2026-10-12</example>
    [Required, RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "orderDate must be yyyy-MM-dd.")]
    public string OrderDate { get; set; } = string.Empty;

    /// <example>ORD-2026-8801</example>
    [Required]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>Received, Preparing, Ready, Collected or Cancelled.</summary>
    /// <example>Collected</example>
    [Required]
    public string Status { get; set; } = string.Empty;
}

/// <summary>An order as returned to clients.</summary>
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

/// <summary>Message placed on "orderprocessingqueue" - same JSON shape the Functions processor reads.</summary>
public class OrderQueueMessage
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderDate { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public List<string> SelectedItemSKUs { get; set; } = new();
    public double TotalPrice { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
}
