namespace CoffeeNChill.Gateway.Models;

/// <summary>
/// Order lifecycle: Received → Preparing → Ready → Collected, plus Cancelled.
/// The queue processor (Functions app) automates Received → Preparing → Ready;
/// staff use PUT /api/orders/status for the rest.
/// </summary>
public static class OrderStatus
{
    public const string Received = "Received";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Collected = "Collected";
    public const string Cancelled = "Cancelled";

    private static readonly string[] Lifecycle = { Received, Preparing, Ready, Collected };

    public static readonly IReadOnlyList<string> All = new[] { Received, Preparing, Ready, Collected, Cancelled };

    /// <summary>Returns the canonical status name (case-insensitive) or null if unknown.</summary>
    public static string? Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Orders only move forward (steps may be skipped, e.g. Received → Ready when the
    /// barista is quick). Any open order can be Cancelled. Collected and Cancelled are final.
    /// Setting the current status again is a no-op and allowed.
    /// </summary>
    public static bool CanTransition(string from, string to)
    {
        if (from == to) return true;
        if (from is Collected or Cancelled) return false;
        if (to == Cancelled) return true;

        var fromIndex = Array.IndexOf(Lifecycle, from);
        var toIndex = Array.IndexOf(Lifecycle, to);
        return fromIndex >= 0 && toIndex > fromIndex;
    }
}
