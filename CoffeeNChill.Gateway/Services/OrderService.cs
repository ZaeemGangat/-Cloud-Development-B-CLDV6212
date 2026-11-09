using System.Text.Json;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;

namespace CoffeeNChill.Gateway.Services;

public enum OrderOutcome { Ok, Invalid, NotFound, Conflict }

public record OrderResult<T>(OrderOutcome Outcome, T? Value = default, string? Error = null);

public interface IOrderService
{
    Task<OrderResult<OrderAcceptedResponse>> SubmitAsync(SubmitOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderResult<OrderResponse>> UpdateStatusAsync(UpdateOrderStatusRequest request, string updatedBy, CancellationToken cancellationToken = default);
}

/// <summary>
/// Order workflow:
/// 1. Validates the SKUs against the live menu and prices the order server-side.
/// 2. Writes the order to the "Orders" table as Received (so it can be tracked immediately).
/// 3. Drops a message on "orderprocessingqueue"; the Functions queue trigger
///    then advances it Received → Preparing → Ready.
/// 4. Staff move it to Collected/Cancelled via <see cref="UpdateStatusAsync"/>.
/// </summary>
public class OrderService : IOrderService
{
    private readonly IMenuRepository _menu;
    private readonly IOrderRepository _orders;
    private readonly IOrderQueue _queue;
    private readonly TimeProvider _clock;

    public OrderService(IMenuRepository menu, IOrderRepository orders, IOrderQueue queue, TimeProvider clock)
    {
        _menu = menu;
        _orders = orders;
        _queue = queue;
        _clock = clock;
    }

    public async Task<OrderResult<OrderAcceptedResponse>> SubmitAsync(SubmitOrderRequest request, CancellationToken cancellationToken = default)
    {
        // SKUs are stored upper-case by MenuController.Create.
        var skus = request.SelectedItemSKUs.Select(s => s?.Trim().ToUpperInvariant() ?? string.Empty).ToList();
        if (skus.Count == 0 || skus.Any(string.IsNullOrEmpty))
        {
            return new(OrderOutcome.Invalid, Error: "selectedItemSKUs must contain at least one non-empty SKU.");
        }

        // Price from the menu, never from the client.
        var total = 0d;
        var unknown = new List<string>();
        var unavailable = new List<string>();
        var priceCache = new Dictionary<string, MenuItemEntity?>(StringComparer.Ordinal);

        foreach (var sku in skus)
        {
            if (!priceCache.TryGetValue(sku, out var item))
            {
                item = await _menu.FindBySkuAsync(sku, cancellationToken);
                priceCache[sku] = item;
            }

            if (item is null) { unknown.Add(sku); continue; }
            if (!item.IsAvailable) { unavailable.Add(sku); continue; }
            total += item.Price;
        }

        if (unknown.Count > 0)
        {
            return new(OrderOutcome.Invalid, Error: $"Unknown menu item SKU(s): {string.Join(", ", unknown.Distinct())}.");
        }

        if (unavailable.Count > 0)
        {
            return new(OrderOutcome.Conflict, Error: $"Currently unavailable: {string.Join(", ", unavailable.Distinct())}.");
        }

        var timestamp = request.OrderTimestamp ?? _clock.GetUtcNow();
        var orderDate = timestamp.UtcDateTime.ToString("yyyy-MM-dd");
        var orderId = string.IsNullOrWhiteSpace(request.OrderId)
            ? $"ORD-{timestamp.UtcDateTime:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
            : request.OrderId.Trim();
        total = Math.Round(total, 2);

        var entity = new OrderEntity
        {
            PartitionKey = orderDate,
            RowKey = orderId,
            CustomerName = request.CustomerName.Trim(),
            SelectedItemSKUs = JsonSerializer.Serialize(skus),
            TotalPrice = total,
            OrderTimestamp = timestamp,
            Status = OrderStatus.Received,
            LastUpdated = _clock.GetUtcNow(),
            UpdatedBy = "gateway"
        };

        if (!await _orders.TryAddAsync(entity, cancellationToken))
        {
            return new(OrderOutcome.Conflict, Error: $"Order '{orderId}' already exists for {orderDate}.");
        }

        await _queue.EnqueueAsync(new OrderQueueMessage
        {
            OrderId = orderId,
            OrderDate = orderDate,
            CustomerName = entity.CustomerName,
            SelectedItemSKUs = skus,
            TotalPrice = total,
            OrderTimestamp = timestamp
        }, cancellationToken);

        return new(OrderOutcome.Ok, new OrderAcceptedResponse
        {
            OrderId = orderId,
            OrderDate = orderDate,
            Status = OrderStatus.Received,
            TotalPrice = total,
            TrackingUrl = $"/api/orders/{orderDate}/{Uri.EscapeDataString(orderId)}"
        });
    }

    public async Task<OrderResult<OrderResponse>> UpdateStatusAsync(UpdateOrderStatusRequest request, string updatedBy, CancellationToken cancellationToken = default)
    {
        var status = OrderStatus.Normalize(request.Status);
        if (status is null)
        {
            return new(OrderOutcome.Invalid, Error: $"Status must be one of: {string.Join(", ", OrderStatus.All)}.");
        }

        // One retry covers the race with the queue processor updating the same row.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var order = await _orders.GetAsync(request.OrderDate, request.OrderId.Trim(), cancellationToken);
            if (order is null)
            {
                return new(OrderOutcome.NotFound, Error: $"Order '{request.OrderId}' was not found for {request.OrderDate}.");
            }

            if (!OrderStatus.CanTransition(order.Status, status))
            {
                return new(OrderOutcome.Conflict, Error: $"Cannot change an order from '{order.Status}' to '{status}'.");
            }

            order.Status = status;
            order.LastUpdated = _clock.GetUtcNow();
            order.UpdatedBy = updatedBy;

            if (await _orders.TryUpdateAsync(order, cancellationToken))
            {
                return new(OrderOutcome.Ok, OrderResponse.FromEntity(order));
            }
        }

        return new(OrderOutcome.Conflict, Error: "The order was updated by someone else at the same time. Please try again.");
    }
}
