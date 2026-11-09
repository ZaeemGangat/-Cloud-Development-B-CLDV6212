using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Functions.Models;

namespace CoffeeNChill.Functions.Services;

/// <summary>
/// Data access for the "Orders" Azure Table.
/// </summary>
public class OrderTableStorageService
{
    private readonly TableClient _ordersTable;

    public OrderTableStorageService(string connectionString, string tableName)
    {
        _ordersTable = new TableServiceClient(connectionString).GetTableClient(tableName);
        _ordersTable.CreateIfNotExists();
    }

    public async Task<OrderEntity?> GetAsync(string orderDate, string orderId, CancellationToken cancellationToken = default)
    {
        var result = await _ordersTable.GetEntityIfExistsAsync<OrderEntity>(orderDate, orderId, cancellationToken: cancellationToken);
        return result.HasValue ? result.Value : null;
    }

    public async Task<List<OrderEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var orders = new List<OrderEntity>();
        await foreach (var entity in _ordersTable.QueryAsync<OrderEntity>(cancellationToken: cancellationToken))
        {
            orders.Add(entity);
        }

        return orders.OrderByDescending(o => o.OrderTimestamp).ToList();
    }

    /// <summary>Adds a new order. Returns false if an order with the same date + ID already exists.</summary>
    public async Task<bool> TryAddAsync(OrderEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            await _ordersTable.AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    /// <summary>
    /// Replaces the order using optimistic concurrency (ETag). Returns false if
    /// someone else (e.g. a barista via the gateway) changed it in the meantime.
    /// </summary>
    public async Task<bool> TryUpdateAsync(OrderEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            await _ordersTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
    }
}
