using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Options;
using Microsoft.Extensions.Options;

namespace CoffeeNChill.Gateway.Data;

/// <summary>
/// Holds the Azure Storage SDK clients, all built from one connection string
/// (Azurite in docker-compose, or a real storage account).
/// </summary>
public class AzureStorageClients
{
    public AzureStorageClients(IOptions<StorageOptions> options)
    {
        var o = options.Value;
        var tables = new TableServiceClient(o.ConnectionString);

        Users = tables.GetTableClient(o.UsersTableName);
        Menu = tables.GetTableClient(o.MenuTableName);
        Orders = tables.GetTableClient(o.OrdersTableName);
        Documents = new BlobServiceClient(o.ConnectionString).GetBlobContainerClient(o.DocumentsContainerName);

        // Base64 so the Functions queue trigger (default messageEncoding) can read it.
        OrderQueue = new QueueClient(o.ConnectionString, o.OrderQueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    }

    public TableClient Users { get; }
    public TableClient Menu { get; }
    public TableClient Orders { get; }
    public BlobContainerClient Documents { get; }
    public QueueClient OrderQueue { get; }
}

public class AzureStorageInitializer : IStorageInitializer
{
    private readonly AzureStorageClients _clients;

    public AzureStorageInitializer(AzureStorageClients clients) => _clients = clients;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await _clients.Users.CreateIfNotExistsAsync(cancellationToken);
        await _clients.Menu.CreateIfNotExistsAsync(cancellationToken);
        await _clients.Orders.CreateIfNotExistsAsync(cancellationToken);
        await _clients.Documents.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        await _clients.OrderQueue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}

public class TableUserRepository : IUserRepository
{
    private readonly TableClient _table;

    public TableUserRepository(AzureStorageClients clients) => _table = clients.Users;

    public async Task<UserEntity?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var key = email.Trim().ToLowerInvariant();
        // RowKey filter without a PartitionKey scans the three role partitions - fine for a canteen-sized table.
        await foreach (var user in _table.QueryAsync<UserEntity>(u => u.RowKey == key, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return user;
        }

        return null;
    }

    public async Task<bool> TryAddAsync(UserEntity user, CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.AddEntityAsync(user, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return false;
        }
    }
}

public class TableMenuRepository : IMenuRepository
{
    private readonly TableClient _table;

    public TableMenuRepository(AzureStorageClients clients) => _table = clients.Menu;

    public async Task<IReadOnlyList<MenuItemEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MenuItemEntity>();
        await foreach (var item in _table.QueryAsync<MenuItemEntity>(cancellationToken: cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    public async Task<IReadOnlyList<MenuItemEntity>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        var items = new List<MenuItemEntity>();
        await foreach (var item in _table.QueryAsync<MenuItemEntity>(e => e.PartitionKey == category, cancellationToken: cancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    public async Task<MenuItemEntity?> GetAsync(string category, string sku, CancellationToken cancellationToken = default)
    {
        var result = await _table.GetEntityIfExistsAsync<MenuItemEntity>(category, sku, cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public async Task<MenuItemEntity?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        await foreach (var item in _table.QueryAsync<MenuItemEntity>(e => e.RowKey == sku, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return item;
        }

        return null;
    }

    public async Task<bool> TryAddAsync(MenuItemEntity item, CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.AddEntityAsync(item, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task UpdateAsync(MenuItemEntity item, CancellationToken cancellationToken = default)
    {
        await _table.UpdateEntityAsync(item, item.ETag, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string category, string sku, CancellationToken cancellationToken = default)
    {
        if (await GetAsync(category, sku, cancellationToken) is null)
        {
            return false;
        }

        await _table.DeleteEntityAsync(category, sku, cancellationToken: cancellationToken);
        return true;
    }
}

public class TableOrderRepository : IOrderRepository
{
    private readonly TableClient _table;

    public TableOrderRepository(AzureStorageClients clients) => _table = clients.Orders;

    public async Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var orders = new List<OrderEntity>();
        await foreach (var order in _table.QueryAsync<OrderEntity>(cancellationToken: cancellationToken))
        {
            orders.Add(order);
        }

        return orders;
    }

    public async Task<OrderEntity?> GetAsync(string orderDate, string orderId, CancellationToken cancellationToken = default)
    {
        var result = await _table.GetEntityIfExistsAsync<OrderEntity>(orderDate, orderId, cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public async Task<bool> TryAddAsync(OrderEntity order, CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.AddEntityAsync(order, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(OrderEntity order, CancellationToken cancellationToken = default)
    {
        try
        {
            await _table.UpdateEntityAsync(order, order.ETag, TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }
}

public class StorageOrderQueue : IOrderQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QueueClient _queue;

    public StorageOrderQueue(AzureStorageClients clients) => _queue = clients.OrderQueue;

    public async Task EnqueueAsync(OrderQueueMessage message, CancellationToken cancellationToken = default)
    {
        await _queue.SendMessageAsync(JsonSerializer.Serialize(message, JsonOptions), cancellationToken);
    }
}

public class BlobDocumentStore : IDocumentStore
{
    private readonly BlobContainerClient _container;

    public BlobDocumentStore(AzureStorageClients clients) => _container = clients.Documents;

    public async Task<IReadOnlyList<StaffDocumentResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var documents = new List<StaffDocumentResponse>();
        await foreach (var blob in _container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            documents.Add(new StaffDocumentResponse
            {
                FileName = blob.Name,
                SizeInBytes = blob.Properties.ContentLength ?? 0,
                ContentType = blob.Properties.ContentType ?? "application/octet-stream",
                LastModified = blob.Properties.LastModified
            });
        }

        return documents;
    }

    public async Task UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _container.GetBlobClient(fileName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);
    }

    public async Task<(Stream Content, string ContentType)?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(fileName);
        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var contentType = string.IsNullOrEmpty(download.Value.Details.ContentType)
                ? "application/octet-stream"
                : download.Value.Details.ContentType;
            return (download.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var result = await _container.GetBlobClient(fileName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return result.Value;
    }
}
