using System.Security.Claims;
using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeNChill.Gateway.Controllers;

/// <summary>
/// Orders. Students place and track orders without logging in; staff see all
/// orders and update their status.
/// </summary>
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
[Tags("4. Orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IOrderRepository _orders;

    public OrdersController(IOrderService orderService, IOrderRepository orders)
    {
        _orderService = orderService;
        _orders = orders;
    }

    /// <summary>Place an order (public - no login needed).</summary>
    /// <remarks>
    /// The gateway checks every SKU against the live menu, calculates the total
    /// from menu prices (any client-side price is ignored), saves the order as
    /// **Received** in the Orders table and puts it on `orderprocessingqueue`.
    /// The queue-triggered Azure Function then moves it to Preparing and Ready.
    /// </remarks>
    /// <response code="202">Order queued. Poll trackingUrl for status.</response>
    /// <response code="400">Validation failed or unknown SKU.</response>
    /// <response code="409">Duplicate orderId, or an item is currently unavailable.</response>
    [HttpPost("queue")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(OrderAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromBody] SubmitOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _orderService.SubmitAsync(request, cancellationToken);
        return result.Outcome switch
        {
            OrderOutcome.Ok => Accepted(result.Value!.TrackingUrl, result.Value),
            OrderOutcome.Invalid => BadRequest(new ApiError(result.Error!)),
            _ => Conflict(new ApiError(result.Error!))
        };
    }

    /// <summary>Track one order (public - students check their own order).</summary>
    /// <param name="orderDate">yyyy-MM-dd, from the submit response.</param>
    /// <param name="orderId">e.g. ORD-2026-8801</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The order and its current status.</response>
    /// <response code="404">No such order.</response>
    [HttpGet("{orderDate}/{orderId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string orderDate, string orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(orderDate, orderId, cancellationToken);
        return order is null
            ? NotFound(new ApiError($"Order '{orderId}' was not found for {orderDate}."))
            : Ok(OrderResponse.FromEntity(order));
    }

    /// <summary>List all orders, newest first (Admin/Staff).</summary>
    /// <param name="status">Optional filter, e.g. "Ready".</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Orders.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    [HttpGet]
    [Authorize(Roles = Roles.AdminOrStaff)]
    [ProducesResponseType(typeof(IEnumerable<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetAllAsync(cancellationToken);
        var filtered = string.IsNullOrWhiteSpace(status)
            ? orders
            : orders.Where(o => string.Equals(o.Status, status.Trim(), StringComparison.OrdinalIgnoreCase));

        return Ok(filtered.OrderByDescending(o => o.OrderTimestamp).Select(OrderResponse.FromEntity));
    }

    /// <summary>Update an order's status (Admin/Staff).</summary>
    /// <remarks>
    /// Lifecycle: Received → Preparing → Ready → Collected. Orders only move
    /// forward; any open order can be Cancelled; Collected and Cancelled are final.
    /// The staff member's email is recorded in `updatedBy`.
    /// </remarks>
    /// <response code="200">Updated order.</response>
    /// <response code="400">Unknown status or validation failed.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    /// <response code="404">No such order.</response>
    /// <response code="409">Transition not allowed (e.g. Collected → Preparing).</response>
    [HttpPut("status")]
    [Authorize(Roles = Roles.AdminOrStaff)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus([FromBody] UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var updatedBy = User.FindFirstValue(ClaimTypes.Email) ?? "staff";
        var result = await _orderService.UpdateStatusAsync(request, updatedBy, cancellationToken);
        return result.Outcome switch
        {
            OrderOutcome.Ok => Ok(result.Value),
            OrderOutcome.Invalid => BadRequest(new ApiError(result.Error!)),
            OrderOutcome.NotFound => NotFound(new ApiError(result.Error!)),
            _ => Conflict(new ApiError(result.Error!))
        };
    }
}
