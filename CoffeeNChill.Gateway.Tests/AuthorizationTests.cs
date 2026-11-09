using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Models.Dtos;

namespace CoffeeNChill.Gateway.Tests;

/// <summary>
/// End-to-end RBAC checks through the real HTTP pipeline:
/// anonymous → 401 on protected endpoints, Customer → 403, Admin/Staff → allowed.
/// </summary>
public class AuthorizationTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public AuthorizationTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Menu.Seed("Hot Drinks", "COF-001", 25.50);
        _factory.Menu.Seed("Pastries", "PAS-104", 39.50);
    }

    private static object NewMenuItem(string sku) => new
    {
        category = "Cold Drinks", sku, name = "Iced Latte", description = "Test", price = 32.00, isAvailable = true
    };

    // ---------- Public endpoints ----------

    [Fact]
    public async Task Anonymous_can_view_menu()
    {
        var response = await _factory.CreateClient().GetAsync("/api/menu");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<MenuItemResponse>>();
        Assert.Contains(items!, i => i.Sku == "COF-001");
    }

    [Fact]
    public async Task Anonymous_can_place_and_track_order()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/orders/queue", new
        {
            customerName = "Jane Smith",
            selectedItemSKUs = new[] { "COF-001", "PAS-104" },
            totalPrice = 1.00 // ignored - priced server-side
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<OrderAcceptedResponse>();
        Assert.Equal(65.00, accepted!.TotalPrice);
        Assert.Contains(_factory.Queue.Messages, m => m.OrderId == accepted.OrderId);

        var tracked = await client.GetFromJsonAsync<OrderResponse>(accepted.TrackingUrl);
        Assert.Equal("Received", tracked!.Status);
    }

    // ---------- Unauthenticated → 401 ----------

    [Theory]
    [InlineData("POST", "/api/menu")]
    [InlineData("PUT", "/api/menu/Hot Drinks/COF-001")]
    [InlineData("DELETE", "/api/menu/Hot Drinks/COF-001")]
    [InlineData("GET", "/api/documents")]
    [InlineData("DELETE", "/api/documents/x.pdf")]
    [InlineData("GET", "/api/orders")]
    [InlineData("PUT", "/api/orders/status")]
    public async Task Protected_endpoints_require_a_token(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = JsonContent.Create(new { })
        };

        var response = await _factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        var response = await client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Customer → 403 ----------

    [Fact]
    public async Task Customer_cannot_change_menu_or_see_documents()
    {
        var customer = await _factory.CreateClientWithRoleAsync(Roles.Customer);

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/menu", NewMenuItem("CUS-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/orders")).StatusCode);
    }

    // ---------- Staff / Admin → allowed ----------

    [Fact]
    public async Task Staff_can_create_update_and_delete_menu_items()
    {
        var staff = await _factory.CreateClientWithRoleAsync(Roles.Staff);

        var created = await staff.PostAsJsonAsync("/api/menu", NewMenuItem("ICE-201"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var updated = await staff.PutAsJsonAsync("/api/menu/Cold Drinks/ICE-201", new { price = 35.00, isAvailable = false });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(35.00, (await updated.Content.ReadFromJsonAsync<MenuItemResponse>())!.Price);

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync("/api/menu/Cold Drinks/ICE-201")).StatusCode);
    }

    [Fact]
    public async Task Staff_can_upload_list_download_and_delete_documents()
    {
        var staff = await _factory.CreateClientWithRoleAsync(Roles.Staff);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("Espresso: 18g in, 36g out, 28s"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "recipe-sheet.txt");

        Assert.Equal(HttpStatusCode.Created, (await staff.PostAsync("/api/documents", form)).StatusCode);

        var list = await staff.GetFromJsonAsync<List<StaffDocumentResponse>>("/api/documents");
        Assert.Contains(list!, d => d.FileName == "recipe-sheet.txt");

        var download = await staff.GetAsync("/api/documents/recipe-sheet.txt");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("18g in", await download.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync("/api/documents/recipe-sheet.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync("/api/documents/recipe-sheet.txt")).StatusCode);
    }

    [Fact]
    public async Task Disallowed_file_types_are_rejected()
    {
        var staff = await _factory.CreateClientWithRoleAsync(Roles.Staff);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "malware.exe");

        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsync("/api/documents", form)).StatusCode);
    }

    [Fact]
    public async Task Staff_can_list_orders_and_mark_collected()
    {
        var accepted = await (await _factory.CreateClient().PostAsJsonAsync("/api/orders/queue",
                new { customerName = "Sipho", selectedItemSKUs = new[] { "COF-001" } }))
            .Content.ReadFromJsonAsync<OrderAcceptedResponse>();

        var staff = await _factory.CreateClientWithRoleAsync(Roles.Staff);
        var all = await staff.GetFromJsonAsync<List<OrderResponse>>("/api/orders");
        Assert.Contains(all!, o => o.OrderId == accepted!.OrderId);

        var update = await staff.PutAsJsonAsync("/api/orders/status",
            new { orderDate = accepted!.OrderDate, orderId = accepted.OrderId, status = "Collected" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var order = await update.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.Equal("Collected", order!.Status);
        Assert.StartsWith("staff-", order.UpdatedBy);

        // Collected is final.
        var back = await staff.PutAsJsonAsync("/api/orders/status",
            new { orderDate = accepted.OrderDate, orderId = accepted.OrderId, status = "Preparing" });
        Assert.Equal(HttpStatusCode.Conflict, back.StatusCode);
    }

    // ---------- Registration rules ----------

    [Fact]
    public async Task Anyone_can_register_as_customer_but_not_as_staff()
    {
        var client = _factory.CreateClient();
        var email = $"student-{Guid.NewGuid():N}@coffeenchill.ac.za";

        var customer = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Student#2026", fullName = "A Student" });
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        Assert.Equal("Customer", (await customer.Content.ReadFromJsonAsync<UserResponse>())!.Role);

        var staff = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "sneaky@coffeenchill.ac.za", password = "Sneaky#2026", fullName = "Sneaky", role = "Staff" });
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Student#2026", fullName = "A Student" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        var email = $"hash-{Guid.NewGuid():N}@coffeenchill.ac.za";
        await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email, password = "PlainText#2026", fullName = "Hash Check" });

        var stored = _factory.Users.Users[email];
        Assert.NotEqual("PlainText#2026", stored.PasswordHash);
        Assert.DoesNotContain("PlainText", stored.PasswordHash);
        Assert.True(stored.PasswordHash.Length > 40);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = GatewayFactory.AdminEmail, password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_endpoint_echoes_token_claims()
    {
        var admin = await _factory.CreateAdminClientAsync();
        var me = await admin.GetFromJsonAsync<Dictionary<string, object?>>("/api/auth/me");
        Assert.Equal(GatewayFactory.AdminEmail, me!["email"]?.ToString());
        Assert.Equal("Admin", me["role"]?.ToString());
    }

    [Fact]
    public async Task Swagger_document_is_served_with_bearer_scheme()
    {
        var json = await _factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("\"bearer\"", json);
        Assert.Contains("/api/auth/login", json);
        Assert.Contains("Place an order", json); // XML doc summary made it into OpenAPI
    }
}
