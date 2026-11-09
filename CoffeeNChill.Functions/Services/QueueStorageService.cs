using System.Text.Json;
using Azure.Storage.Queues;
using CoffeeNChill.Functions.Models;

namespace CoffeeNChill.Functions.Services;

/// <summary>
/// Sends order messages to the "orderprocessingqueue" Azure Storage Queue.
/// Messages are Base64-encoded because that is what the Functions queue
/// trigger expects by default (host.json → extensions.queues.messageEncoding).
/// </summary>
public class QueueStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QueueClient _queueClient;

    public QueueStorageService(string connectionString, string queueName)
    {
        _queueClient = new QueueClient(connectionString, queueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        _queueClient.CreateIfNotExists();
    }

    public async Task SendOrderAsync(OrderQueueMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _queueClient.SendMessageAsync(json, cancellationToken);
    }
}
