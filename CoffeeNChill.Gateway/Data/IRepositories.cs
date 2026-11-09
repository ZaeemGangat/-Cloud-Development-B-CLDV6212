using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;

namespace CoffeeNChill.Gateway.Data;

/// <summary>"Users" Azure Table.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by email across all role partitions.</summary>
    Task<UserEntity?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the (role, email) row already exists.</summary>
    Task<bool> TryAddAsync(UserEntity user, CancellationToken cancellationToken = default);
}

/// <summary>"MenuItems" Azure Table.</summary>
public interface IMenuRepository
{
    Task<IReadOnlyList<MenuItemEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuItemEntity>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default);
    Task<MenuItemEntity?> GetAsync(string category, string sku, CancellationToken cancellationToken = default);

    /// <summary>Finds an item by SKU in any category (used to price orders).</summary>
    Task<MenuItemEntity?> FindBySkuAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the (category, sku) row already exists.</summary>
    Task<bool> TryAddAsync(MenuItemEntity item, CancellationToken cancellationToken = default);

    Task UpdateAsync(MenuItemEntity item, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the item did not exist.</summary>
    Task<bool> DeleteAsync(string category, string sku, CancellationToken cancellationToken = default);
}

/// <summary>"Orders" Azure Table.</summary>
public interface IOrderRepository
{
    Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<OrderEntity?> GetAsync(string orderDate, string orderId, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the order already exists.</summary>
    Task<bool> TryAddAsync(OrderEntity order, CancellationToken cancellationToken = default);

    /// <summary>ETag-checked replace. Returns false if the order changed since it was read.</summary>
    Task<bool> TryUpdateAsync(OrderEntity order, CancellationToken cancellationToken = default);
}

/// <summary>"orderprocessingqueue" Azure Storage Queue.</summary>
public interface IOrderQueue
{
    Task EnqueueAsync(OrderQueueMessage message, CancellationToken cancellationToken = default);
}

/// <summary>"staff-docs" Blob container.</summary>
public interface IDocumentStore
{
    Task<IReadOnlyList<StaffDocumentResponse>> ListAsync(CancellationToken cancellationToken = default);
    Task UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Returns null if the document does not exist. Caller disposes the stream.</summary>
    Task<(Stream Content, string ContentType)?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Returns false if the document did not exist.</summary>
    Task<bool> DeleteAsync(string fileName, CancellationToken cancellationToken = default);
}

/// <summary>Creates tables, the blob container and the queue on startup.</summary>
public interface IStorageInitializer
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}
