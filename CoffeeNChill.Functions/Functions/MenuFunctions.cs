using System.Net;
using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Functions.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions.Functions;

/// <summary>
/// HTTP-triggered functions that back the digital menu, replacing CoffeeNChill's
/// chalkboards. All entities live in the Azure Table Storage "MenuItems" table:
/// PartitionKey = Category, RowKey = SKU/ID.
/// </summary>
public class MenuFunctions
{
    private readonly TableClient _menuTable;
    private readonly ILogger<MenuFunctions> _logger;

    public MenuFunctions(TableClient menuTable, ILogger<MenuFunctions> logger)
    {
        _menuTable = menuTable;
        _logger = logger;
    }

    // POST /api/menu
    [Function("CreateMenuItem")]
    public async Task<HttpResponseData> CreateMenuItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequestData req)
    {
        MenuItemCreateRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<MenuItemCreateRequest>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed JSON body on CreateMenuItem");
            return await BadRequest(req, "Request body must be valid JSON.");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Category) || string.IsNullOrWhiteSpace(body.Sku))
        {
            return await BadRequest(req, "Both 'category' and 'sku' are required.");
        }

        var entity = new MenuItemEntity
        {
            PartitionKey = body.Category,
            RowKey = body.Sku,
            Name = body.Name,
            Description = body.Description,
            Price = body.Price,
            IsAvailable = body.IsAvailable
        };

        try
        {
            await _menuTable.AddEntityAsync(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.Conflict)
        {
            return await BadRequest(req, $"A menu item with SKU '{body.Sku}' already exists in category '{body.Category}'.", HttpStatusCode.Conflict);
        }

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(MenuItemResponse.FromEntity(entity), HttpStatusCode.Created);
        return response;
    }

    // GET /api/menu
    [Function("GetAllMenuItems")]
    public async Task<HttpResponseData> GetAllMenuItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequestData req)
    {
        var items = new List<MenuItemResponse>();
        await foreach (var entity in _menuTable.QueryAsync<MenuItemEntity>())
        {
            items.Add(MenuItemResponse.FromEntity(entity));
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items, HttpStatusCode.OK);
        return response;
    }

    // GET /api/menu/category/{category}
    [Function("GetMenuItemsByCategory")]
    public async Task<HttpResponseData> GetMenuItemsByCategory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequestData req,
        string category)
    {
        var items = new List<MenuItemResponse>();
        await foreach (var entity in _menuTable.QueryAsync<MenuItemEntity>(e => e.PartitionKey == category))
        {
            items.Add(MenuItemResponse.FromEntity(entity));
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(items, HttpStatusCode.OK);
        return response;
    }

    // PUT /api/menu/{category}/{id}
    [Function("UpdateMenuItem")]
    public async Task<HttpResponseData> UpdateMenuItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "menu/{category}/{id}")] HttpRequestData req,
        string category, string id)
    {
        MenuItemEntity existing;
        try
        {
            var existingResponse = await _menuTable.GetEntityAsync<MenuItemEntity>(category, id);
            existing = existingResponse.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return await NotFound(req, $"No menu item '{id}' found in category '{category}'.");
        }

        MenuItemUpdateRequest? body;
        try
        {
            body = await req.ReadFromJsonAsync<MenuItemUpdateRequest>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Malformed JSON body on UpdateMenuItem");
            return await BadRequest(req, "Request body must be valid JSON.");
        }

        if (body is null)
        {
            return await BadRequest(req, "Request body is required.");
        }

        if (body.Price.HasValue) existing.Price = body.Price.Value;
        if (body.IsAvailable.HasValue) existing.IsAvailable = body.IsAvailable.Value;
        if (!string.IsNullOrWhiteSpace(body.Name)) existing.Name = body.Name;
        if (!string.IsNullOrWhiteSpace(body.Description)) existing.Description = body.Description;

        await _menuTable.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(MenuItemResponse.FromEntity(existing), HttpStatusCode.OK);
        return response;
    }

    // DELETE /api/menu/{category}/{id}
    [Function("DeleteMenuItem")]
    public async Task<HttpResponseData> DeleteMenuItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{id}")] HttpRequestData req,
        string category, string id)
    {
        try
        {
            await _menuTable.DeleteEntityAsync(category, id);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return await NotFound(req, $"No menu item '{id}' found in category '{category}'.");
        }

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError { Message = message }, status);
        return response;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.NotFound);
        await response.WriteAsJsonAsync(new ApiError { Message = message }, HttpStatusCode.NotFound);
        return response;
    }
}
