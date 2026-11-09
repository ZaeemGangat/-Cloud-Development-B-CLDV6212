using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Options;
using CoffeeNChill.Gateway.Services;
using CoffeeNChill.Gateway.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace CoffeeNChill.Gateway.Tests;

public class OrderStatusTests
{
    [Theory]
    [InlineData("Received", "Preparing", true)]
    [InlineData("Preparing", "Ready", true)]
    [InlineData("Ready", "Collected", true)]
    [InlineData("Received", "Ready", true)]       // skipping ahead is fine
    [InlineData("Ready", "Cancelled", true)]
    [InlineData("Ready", "Ready", true)]          // no-op
    [InlineData("Ready", "Preparing", false)]     // no going back
    [InlineData("Collected", "Cancelled", false)] // final
    [InlineData("Cancelled", "Received", false)]  // final
    public void Transitions(string from, string to, bool allowed) =>
        Assert.Equal(allowed, OrderStatus.CanTransition(from, to));

    [Fact]
    public void Normalize_is_case_insensitive() => Assert.Equal("Collected", OrderStatus.Normalize("collected"));
}

public class JwtTokenServiceTests
{
    private const string Key = "unit-test-signing-key-0123456789-abcdefghijklmnop";

    [Fact]
    public void Token_contains_name_email_and_role_claims_and_validates()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new JwtOptions { Key = Key, Issuer = "iss", Audience = "aud", ExpiryMinutes = 30 });
        var service = new JwtTokenService(options, TimeProvider.System);
        var user = new UserEntity { PartitionKey = Roles.Staff, RowKey = "barista@coffeenchill.ac.za", FullName = "Thandi Barista" };

        var result = service.CreateToken(user);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(result.Token, new TokenValidationParameters
        {
            ValidIssuer = "iss",
            ValidAudience = "aud",
            IssuerSigningKey = JwtTokenService.CreateSigningKey(Key),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        }, out _);

        Assert.Equal("Thandi Barista", principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("barista@coffeenchill.ac.za", principal.FindFirstValue(ClaimTypes.Email));
        Assert.True(principal.IsInRole(Roles.Staff));
        Assert.Equal("Bearer", result.TokenType);
        Assert.InRange(result.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(new JwtOptions { Key = Key }), TimeProvider.System);
        var token = service.CreateToken(new UserEntity { PartitionKey = Roles.Admin, RowKey = "a@b.c", FullName = "A" }).Token;

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = JwtTokenService.CreateSigningKey("a-completely-different-key-0123456789-abcdef")
        }, out _));
    }
}

public class UserServiceTests
{
    private readonly InMemoryUserRepository _repo = new();
    private readonly UserService _service;

    public UserServiceTests() =>
        _service = new UserService(_repo, new PasswordHasher<UserEntity>(), TimeProvider.System);

    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    private static ClaimsPrincipal AdminCaller => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Role, Roles.Admin) }, "Test", ClaimTypes.Name, ClaimTypes.Role));

    private static RegisterRequest Request(string email, string? role = null) =>
        new() { Email = email, Password = "Secret#2026", FullName = "Test User", Role = role };

    [Fact]
    public async Task Registers_customer_by_default_with_hashed_password_and_lowercase_email()
    {
        var result = await _service.RegisterAsync(Request("Student@CoffeeNChill.ac.za"), Anonymous);

        Assert.Equal(RegistrationStatus.Created, result.Status);
        var stored = _repo.Users["student@coffeenchill.ac.za"];
        Assert.Equal(Roles.Customer, stored.PartitionKey);
        Assert.NotEqual("Secret#2026", stored.PasswordHash);
        Assert.Equal(DateTimeKind.Utc, stored.CreatedDate.Kind);
    }

    [Fact]
    public async Task Only_admin_can_create_staff()
    {
        Assert.Equal(RegistrationStatus.Forbidden, (await _service.RegisterAsync(Request("s1@x.com", "Staff"), Anonymous)).Status);
        Assert.Equal(RegistrationStatus.Created, (await _service.RegisterAsync(Request("s2@x.com", "staff"), AdminCaller)).Status);
        Assert.Equal(Roles.Staff, _repo.Users["s2@x.com"].PartitionKey);
    }

    [Fact]
    public async Task Rejects_unknown_role_and_duplicate_email()
    {
        Assert.Equal(RegistrationStatus.InvalidRole, (await _service.RegisterAsync(Request("r@x.com", "Barista"), AdminCaller)).Status);

        await _service.RegisterAsync(Request("dup@x.com"), Anonymous);
        Assert.Equal(RegistrationStatus.EmailTaken, (await _service.RegisterAsync(Request("DUP@x.com"), Anonymous)).Status);
    }

    [Fact]
    public async Task Validates_credentials()
    {
        await _service.RegisterAsync(Request("login@x.com"), Anonymous);

        Assert.NotNull(await _service.ValidateCredentialsAsync("LOGIN@x.com", "Secret#2026"));
        Assert.Null(await _service.ValidateCredentialsAsync("login@x.com", "wrong"));
        Assert.Null(await _service.ValidateCredentialsAsync("nobody@x.com", "Secret#2026"));
    }

    [Fact]
    public async Task Seed_admin_is_created_once()
    {
        Assert.True(await _service.EnsureAdminAsync("boss@x.com", "Boss#2026!", "Boss"));
        Assert.False(await _service.EnsureAdminAsync("boss@x.com", "Boss#2026!", "Boss"));
        Assert.Equal(Roles.Admin, _repo.Users["boss@x.com"].PartitionKey);
    }
}

public class OrderServiceTests
{
    private readonly InMemoryMenuRepository _menu = new();
    private readonly InMemoryOrderRepository _orders = new();
    private readonly InMemoryOrderQueue _queue = new();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _menu.Seed("Hot Drinks", "COF-001", 25.50);
        _menu.Seed("Pastries", "PAS-104", 39.50);
        _menu.Seed("Pastries", "PAS-999", 20.00, available: false);
        _service = new OrderService(_menu, _orders, _queue, TimeProvider.System);
    }

    [Fact]
    public async Task Prices_order_from_menu_saves_received_and_enqueues()
    {
        var result = await _service.SubmitAsync(new SubmitOrderRequest
        {
            OrderId = "ORD-2026-8801",
            CustomerName = "Jane Smith",
            SelectedItemSKUs = new() { "COF-001", "pas-104", "COF-001" },
            OrderTimestamp = DateTimeOffset.Parse("2026-10-12T10:15:00Z")
        });

        Assert.Equal(OrderOutcome.Ok, result.Outcome);
        Assert.Equal(90.50, result.Value!.TotalPrice);
        Assert.Equal("2026-10-12", result.Value.OrderDate);
        Assert.Equal(OrderStatus.Received, (await _orders.GetAsync("2026-10-12", "ORD-2026-8801"))!.Status);
        Assert.Single(_queue.Messages);
    }

    [Fact]
    public async Task Rejects_unknown_and_unavailable_items()
    {
        var unknown = await _service.SubmitAsync(new SubmitOrderRequest { CustomerName = "X", SelectedItemSKUs = new() { "NOPE-1" } });
        Assert.Equal(OrderOutcome.Invalid, unknown.Outcome);

        var unavailable = await _service.SubmitAsync(new SubmitOrderRequest { CustomerName = "X", SelectedItemSKUs = new() { "PAS-999" } });
        Assert.Equal(OrderOutcome.Conflict, unavailable.Outcome);

        Assert.Empty(_queue.Messages);
    }

    [Fact]
    public async Task Duplicate_order_id_is_a_conflict()
    {
        var request = new SubmitOrderRequest { OrderId = "ORD-1", CustomerName = "X", SelectedItemSKUs = new() { "COF-001" } };
        Assert.Equal(OrderOutcome.Ok, (await _service.SubmitAsync(request)).Outcome);
        Assert.Equal(OrderOutcome.Conflict, (await _service.SubmitAsync(request)).Outcome);
    }

    [Fact]
    public async Task Status_update_follows_lifecycle_and_records_staff_member()
    {
        var accepted = (await _service.SubmitAsync(new SubmitOrderRequest { CustomerName = "X", SelectedItemSKUs = new() { "COF-001" } })).Value!;
        UpdateOrderStatusRequest Req(string status) => new() { OrderDate = accepted.OrderDate, OrderId = accepted.OrderId, Status = status };

        // Simulate the Functions queue processor moving it on in the meantime.
        _orders.SetStatusDirectly(accepted.OrderDate, accepted.OrderId, OrderStatus.Ready);

        var collected = await _service.UpdateStatusAsync(Req("collected"), "barista@coffeenchill.ac.za");
        Assert.Equal(OrderOutcome.Ok, collected.Outcome);
        Assert.Equal(OrderStatus.Collected, collected.Value!.Status);
        Assert.Equal("barista@coffeenchill.ac.za", collected.Value.UpdatedBy);

        Assert.Equal(OrderOutcome.Conflict, (await _service.UpdateStatusAsync(Req("Preparing"), "b")).Outcome);
        Assert.Equal(OrderOutcome.Invalid, (await _service.UpdateStatusAsync(Req("Eaten"), "b")).Outcome);
        Assert.Equal(OrderOutcome.NotFound, (await _service.UpdateStatusAsync(
            new UpdateOrderStatusRequest { OrderDate = "2000-01-01", OrderId = "none", Status = "Ready" }, "b")).Outcome);
    }
}
