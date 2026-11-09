using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeNChill.Gateway.Controllers;

/// <summary>
/// The digital menu ("MenuItems" Azure Table). Reading is public; changing
/// items or prices requires an Admin or Staff token.
/// </summary>
[ApiController]
[Route("api/menu")]
[Produces("application/json")]
[Tags("2. Menu")]
public class MenuController : ControllerBase
{
    private readonly IMenuRepository _menu;

    public MenuController(IMenuRepository menu) => _menu = menu;

    /// <summary>Get the full menu (public).</summary>
    /// <response code="200">All menu items, ordered by category then name.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<MenuItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _menu.GetAllAsync(cancellationToken);
        return Ok(items.OrderBy(i => i.PartitionKey).ThenBy(i => i.Name).Select(MenuItemResponse.FromEntity));
    }

    /// <summary>Get the menu items in one category (public).</summary>
    /// <param name="category">e.g. "Hot Drinks"</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Items in the category (empty if none).</response>
    [HttpGet("category/{category}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<MenuItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory(string category, CancellationToken cancellationToken)
    {
        var items = await _menu.GetByCategoryAsync(category, cancellationToken);
        return Ok(items.OrderBy(i => i.Name).Select(MenuItemResponse.FromEntity));
    }

    /// <summary>Get one menu item (public).</summary>
    /// <param name="category">e.g. "Hot Drinks"</param>
    /// <param name="sku">e.g. "COF-001"</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">The item.</response>
    /// <response code="404">No such item.</response>
    [HttpGet("{category}/{sku}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MenuItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string category, string sku, CancellationToken cancellationToken)
    {
        var item = await _menu.GetAsync(category, sku, cancellationToken);
        return item is null
            ? NotFound(new ApiError($"No menu item '{sku}' found in category '{category}'."))
            : Ok(MenuItemResponse.FromEntity(item));
    }

    /// <summary>Add a menu item (Admin/Staff).</summary>
    /// <response code="201">Item created.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Token is valid but the role is not Admin or Staff.</response>
    /// <response code="409">SKU already exists in that category.</response>
    [HttpPost]
    [Authorize(Roles = Roles.AdminOrStaff)]
    [ProducesResponseType(typeof(MenuItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] MenuItemCreateRequest request, CancellationToken cancellationToken)
    {
        var entity = new MenuItemEntity
        {
            PartitionKey = request.Category.Trim(),
            RowKey = request.Sku.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            Price = Math.Round(request.Price, 2),
            IsAvailable = request.IsAvailable
        };

        if (await _menu.FindBySkuAsync(entity.RowKey, cancellationToken) is { } existing)
        {
            // SKUs must be unique across categories so orders can be priced by SKU alone.
            return Conflict(new ApiError($"SKU '{entity.RowKey}' already exists in category '{existing.PartitionKey}'."));
        }

        if (!await _menu.TryAddAsync(entity, cancellationToken))
        {
            return Conflict(new ApiError($"SKU '{entity.RowKey}' already exists in category '{entity.PartitionKey}'."));
        }

        var response = MenuItemResponse.FromEntity(entity);
        return CreatedAtAction(nameof(Get), new { category = response.Category, sku = response.Sku }, response);
    }

    /// <summary>Update price, availability, name or description (Admin/Staff).</summary>
    /// <param name="category">e.g. "Hot Drinks"</param>
    /// <param name="sku">e.g. "COF-001"</param>
    /// <param name="request">Only supplied fields are changed.</param>
    /// <param name="cancellationToken"></param>
    /// <response code="200">Updated item.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    /// <response code="404">No such item.</response>
    [HttpPut("{category}/{sku}")]
    [Authorize(Roles = Roles.AdminOrStaff)]
    [ProducesResponseType(typeof(MenuItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string category, string sku, [FromBody] MenuItemUpdateRequest request, CancellationToken cancellationToken)
    {
        var item = await _menu.GetAsync(category, sku, cancellationToken);
        if (item is null)
        {
            return NotFound(new ApiError($"No menu item '{sku}' found in category '{category}'."));
        }

        if (request.Price.HasValue) item.Price = Math.Round(request.Price.Value, 2);
        if (request.IsAvailable.HasValue) item.IsAvailable = request.IsAvailable.Value;
        if (!string.IsNullOrWhiteSpace(request.Name)) item.Name = request.Name.Trim();
        if (request.Description is not null) item.Description = request.Description.Trim();

        await _menu.UpdateAsync(item, cancellationToken);
        return Ok(MenuItemResponse.FromEntity(item));
    }

    /// <summary>Remove a menu item (Admin/Staff).</summary>
    /// <param name="category">e.g. "Hot Drinks"</param>
    /// <param name="sku">e.g. "COF-001"</param>
    /// <param name="cancellationToken"></param>
    /// <response code="204">Deleted.</response>
    /// <response code="401">No or invalid token.</response>
    /// <response code="403">Role is not Admin or Staff.</response>
    /// <response code="404">No such item.</response>
    [HttpDelete("{category}/{sku}")]
    [Authorize(Roles = Roles.AdminOrStaff)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string category, string sku, CancellationToken cancellationToken)
    {
        return await _menu.DeleteAsync(category, sku, cancellationToken)
            ? NoContent()
            : NotFound(new ApiError($"No menu item '{sku}' found in category '{category}'."));
    }
}
