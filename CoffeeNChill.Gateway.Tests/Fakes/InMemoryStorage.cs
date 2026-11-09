using System.Collections.Concurrent;
using Azure;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;

namespace CoffeeNChill.Gateway.Tests.Fakes;

// In-memory stand-ins for the Azure Storage repositories, so tests run without Azurite.

public class NoopStorageInitializer : IStorageInitializer
{
    public Task EnsureCreatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class InMemoryUserRepository : IUserRepository
{
    public ConcurrentDictionary<string, UserEntity> Users { get; } = new();

    public Task<UserEntity?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.TryGetValue(email.Trim().ToLowerInvariant(), out var user) ? user : null);

    public Task<bool> TryAddAsync(UserEntity user, CancellationToken cancellationToken = default) =>
        Task.FromResult(Users.TryAdd(user.RowKey, user));
}

public class InMemoryMenuRepository : IMenuRepository
{
    public ConcurrentDictionary<(string Category, string Sku), MenuItemEntity> Items { get; } = new();

    public void Seed(string category, string sku, double price, bool available = true) =>
        Items[(category, sku)] = new MenuItemEntity
        {
            PartitionKey = category, RowKey = sku, Name = sku, Price = price, IsAvailable = available
        };

    public Task<IReadOnlyList<MenuItemEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MenuItemEntity>>(Items.Values.ToList());

    public Task<IReadOnlyList<MenuItemEntity>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MenuItemEntity>>(Items.Values.Where(i => i.PartitionKey == category).ToList());

    public Task<MenuItemEntity?> GetAsync(string category, string sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.TryGetValue((category, sku), out var item) ? item : null);

    public Task<MenuItemEntity?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.Values.FirstOrDefault(i => i.RowKey == sku));

    public Task<bool> TryAddAsync(MenuItemEntity item, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.TryAdd((item.PartitionKey, item.RowKey), item));

    public Task UpdateAsync(MenuItemEntity item, CancellationToken cancellationToken = default)
    {
        Items[(item.PartitionKey, item.RowKey)] = item;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string category, string sku, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.TryRemove((category, sku), out _));
}

/// <summary>Emulates Table Storage ETag optimistic concurrency.</summary>
public class InMemoryOrderRepository : IOrderRepository
{
    private readonly object _lock = new();
    private readonly Dictionary<(string Date, string Id), OrderEntity> _orders = new();

    public Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<OrderEntity>>(_orders.Values.Select(Clone).ToList());
    }

    public Task<OrderEntity?> GetAsync(string orderDate, string orderId, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_orders.TryGetValue((orderDate, orderId), out var o) ? Clone(o) : null);
    }

    public Task<bool> TryAddAsync(OrderEntity order, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_orders.ContainsKey((order.PartitionKey, order.RowKey))) return Task.FromResult(false);
            order.ETag = new ETag(Guid.NewGuid().ToString());
            _orders[(order.PartitionKey, order.RowKey)] = Clone(order);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryUpdateAsync(OrderEntity order, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_orders.TryGetValue((order.PartitionKey, order.RowKey), out var current) || current.ETag != order.ETag)
            {
                return Task.FromResult(false);
            }

            order.ETag = new ETag(Guid.NewGuid().ToString());
            _orders[(order.PartitionKey, order.RowKey)] = Clone(order);
            return Task.FromResult(true);
        }
    }

    /// <summary>Simulates the queue processor changing an order behind the gateway's back.</summary>
    public void SetStatusDirectly(string orderDate, string orderId, string status)
    {
        lock (_lock)
        {
            var order = _orders[(orderDate, orderId)];
            order.Status = status;
            order.ETag = new ETag(Guid.NewGuid().ToString());
        }
    }

    private static OrderEntity Clone(OrderEntity o) => new()
    {
        PartitionKey = o.PartitionKey, RowKey = o.RowKey, ETag = o.ETag, Timestamp = o.Timestamp,
        CustomerName = o.CustomerName, SelectedItemSKUs = o.SelectedItemSKUs, TotalPrice = o.TotalPrice,
        OrderTimestamp = o.OrderTimestamp, Status = o.Status, LastUpdated = o.LastUpdated, UpdatedBy = o.UpdatedBy
    };
}

public class InMemoryOrderQueue : IOrderQueue
{
    public ConcurrentQueue<OrderQueueMessage> Messages { get; } = new();

    public Task EnqueueAsync(OrderQueueMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}

public class InMemoryDocumentStore : IDocumentStore
{
    public ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> Files { get; } = new();

    public Task<IReadOnlyList<StaffDocumentResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StaffDocumentResponse>>(Files.Select(f => new StaffDocumentResponse
        {
            FileName = f.Key, SizeInBytes = f.Value.Bytes.Length, ContentType = f.Value.ContentType
        }).ToList());

    public async Task UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[fileName] = (buffer.ToArray(), contentType);
    }

    public Task<(Stream Content, string ContentType)?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<(Stream, string)?>(Files.TryGetValue(fileName, out var f) ? (new MemoryStream(f.Bytes), f.ContentType) : null);

    public Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.TryRemove(fileName, out _));
}
