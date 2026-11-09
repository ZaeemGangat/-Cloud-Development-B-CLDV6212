using System.Net;
using System.Text.Json;
using CoffeeNChill.Functions.Models;
using CoffeeNChill.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions.Functions;

/// <summary>
/// Part 2: replaces handwritten order slips with an asynchronous queue.
///
/// 1. POST /api/orders/queue records the order in the "Orders" table as
///    <c>Received</c> and drops a message on "orderprocessingqueue".
/// 2. <see cref="ProcessOrderFromQueue"/> picks the message up and moves the
///    order through Received → Preparing → Ready (simulated barista work).
/// 3. Part 3: staff mark the order <c>Collected</c> through the gateway's
///    PUT /api/orders/status endpoint (JWT protected).
///
/// In Part 3 the public entry point is the gateway, which writes to the same
/// table and queue; these HTTP functions stay for direct/internal testing.
/// </summary>
public class OrderFunctions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderTableStorageService _orders;
    private readonly QueueStorageService _queue;
    private readonly ILogger<OrderFunctions> _logger;

    public OrderFunctions(OrderTableStorageService orders, QueueStorageService queue, ILogger<OrderFunctions> logger)
    {
        _orders = orders;
        _queue = queue;
        _logger = logger;
    }

    // POST /api/orders/queue
    [Function("SubmitOrderToQueue")]
    public async Task<HttpResponseData> SubmitOrderToQueue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/queue")] HttpRequestData req)
    {
        SubmitOrderRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<SubmitOrderRequest>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed JSON body on SubmitOrderToQueue");
            return await Error(req, HttpStatusCode.BadRequest, "Request body must be valid JSON.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.CustomerName) || body.SelectedItemSKUs.Count == 0)
        {
            return await Error(req, HttpStatusCode.BadRequest, "'customerName' and at least one 'selectedItemSKUs' entry are required.");
        }

        var timestamp = body.OrderTimestamp ?? DateTimeOffset.UtcNow;
        var orderId = string.IsNullOrWhiteSpace(body.OrderId) ? NewOrderId(timestamp) : body.OrderId.Trim();

        var message = new OrderQueueMessage
        {
            OrderId = orderId,
            OrderDate = timestamp.UtcDateTime.ToString("yyyy-MM-dd"),
            CustomerName = body.CustomerName.Trim(),
            SelectedItemSKUs = body.SelectedItemSKUs,
            TotalPrice = body.TotalPrice,
            OrderTimestamp = timestamp
        };

        var entity = ToEntity(message);
        if (!await _orders.TryAddAsync(entity))
        {
            return await Error(req, HttpStatusCode.Conflict, $"Order '{orderId}' already exists for {message.OrderDate}.");
        }

        await _queue.SendOrderAsync(message);
        _logger.LogInformation("Order {OrderId} queued for processing", orderId);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            orderId,
            orderDate = message.OrderDate,
            status = OrderStatus.Received,
            message = "Order placed on orderprocessingqueue."
        }, HttpStatusCode.Accepted);
        return response;
    }

    // GET /api/orders
    [Function("GetAllOrders")]
    public async Task<HttpResponseData> GetAllOrders(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders")] HttpRequestData req)
    {
        var orders = (await _orders.GetAllAsync()).Select(OrderResponse.FromEntity).ToList();
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(orders, HttpStatusCode.OK);
        return response;
    }

    // GET /api/orders/{orderDate}/{orderId}
    [Function("GetOrder")]
    public async Task<HttpResponseData> GetOrder(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "orders/{orderDate}/{orderId}")] HttpRequestData req,
        string orderDate, string orderId)
    {
        var entity = await _orders.GetAsync(orderDate, orderId);
        if (entity is null)
        {
            return await Error(req, HttpStatusCode.NotFound, $"Order '{orderId}' was not found for {orderDate}.");
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(OrderResponse.FromEntity(entity), HttpStatusCode.OK);
        return response;
    }

    /// <summary>
    /// Queue-triggered processor for "orderprocessingqueue".
    /// Simulates the barista: Received → Preparing → Ready, pausing
    /// OrderProcessing__StepDelaySeconds (default 2s) between steps.
    /// Each step uses an ETag check and only moves forward from the status the
    /// processor expects, so it never overwrites a status a barista set manually
    /// (e.g. Cancelled or Collected) through the gateway.
    /// </summary>
    [Function("ProcessOrderFromQueue")]
    public async Task ProcessOrderFromQueue(
        [QueueTrigger("%OrderQueueName%", Connection = "AzureWebJobsStorage")] string queueMessage,
        CancellationToken cancellationToken)
    {
        OrderQueueMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<OrderQueueMessage>(queueMessage, JsonOptions);
        }
        catch (JsonException ex)
        {
            // Not retryable - log and drop instead of poisoning the queue 5 times.
            _logger.LogError(ex, "Discarding malformed order message: {Message}", queueMessage);
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.OrderId) || string.IsNullOrWhiteSpace(message.OrderDate))
        {
            _logger.LogError("Discarding order message without orderId/orderDate: {Message}", queueMessage);
            return;
        }

        _logger.LogInformation("Processing order {OrderId} ({OrderDate})", message.OrderId, message.OrderDate);

        // The gateway normally creates the row before queueing; create it here if
        // a message arrives on its own (e.g. sent with Azure Storage Explorer).
        if (await _orders.GetAsync(message.OrderDate, message.OrderId, cancellationToken) is null)
        {
            await _orders.TryAddAsync(ToEntity(message), cancellationToken);
        }

        var delay = TimeSpan.FromSeconds(ReadStepDelaySeconds());

        if (!await AdvanceAsync(message, OrderStatus.Received, OrderStatus.Preparing, delay, cancellationToken)) return;
        await AdvanceAsync(message, OrderStatus.Preparing, OrderStatus.Ready, delay, cancellationToken);
    }

    private async Task<bool> AdvanceAsync(OrderQueueMessage message, string expected, string next, TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);

        var entity = await _orders.GetAsync(message.OrderDate, message.OrderId, cancellationToken);
        if (entity is null || entity.Status != expected)
        {
            _logger.LogInformation("Order {OrderId} is '{Status}', not '{Expected}' - leaving it for staff.",
                message.OrderId, entity?.Status ?? "missing", expected);
            return false;
        }

        entity.Status = next;
        entity.LastUpdated = DateTimeOffset.UtcNow;
        entity.UpdatedBy = "order-processor";

        if (!await _orders.TryUpdateAsync(entity, cancellationToken))
        {
            _logger.LogInformation("Order {OrderId} changed concurrently - stopping automatic processing.", message.OrderId);
            return false;
        }

        _logger.LogInformation("Order {OrderId}: {From} → {To}", message.OrderId, expected, next);
        return true;
    }

    private static int ReadStepDelaySeconds() =>
        int.TryParse(Environment.GetEnvironmentVariable("OrderProcessing__StepDelaySeconds"), out var seconds) && seconds >= 0
            ? seconds
            : 2;

    private static OrderEntity ToEntity(OrderQueueMessage message) => new()
    {
        PartitionKey = message.OrderDate,
        RowKey = message.OrderId,
        CustomerName = message.CustomerName,
        SelectedItemSKUs = JsonSerializer.Serialize(message.SelectedItemSKUs),
        TotalPrice = message.TotalPrice,
        OrderTimestamp = message.OrderTimestamp,
        Status = OrderStatus.Received,
        LastUpdated = DateTimeOffset.UtcNow,
        UpdatedBy = "system"
    };

    private static string NewOrderId(DateTimeOffset timestamp) =>
        $"ORD-{timestamp.UtcDateTime:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError { Message = message }, status);
        return response;
    }
}
