namespace CoffeeNChill.Functions.Models;

/// <summary>
/// Order status lifecycle: Received → Preparing → Ready → Collected.
/// The queue-triggered function advances Received → Preparing → Ready
/// automatically; staff mark orders Collected (or Cancelled) through the
/// Part 3 gateway's PUT /api/orders/status endpoint.
/// </summary>
public static class OrderStatus
{
    public const string Received = "Received";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Collected = "Collected";
    public const string Cancelled = "Cancelled";
}
