using System.Net.Http.Headers;
using System.Net.Http.Json;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoffeeNChill.Gateway.Tests;

/// <summary>
/// Hosts the real gateway (real JWT auth, real [Authorize] rules, real controllers)
/// in memory, with the Azure Storage repositories swapped for in-memory fakes.
/// </summary>
public class GatewayFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "manager@coffeenchill.ac.za";
    public const string AdminPassword = "Manager#2026!";

    public InMemoryUserRepository Users { get; } = new();
    public InMemoryMenuRepository Menu { get; } = new();
    public InMemoryOrderRepository Orders { get; } = new();
    public InMemoryOrderQueue Queue { get; } = new();
    public InMemoryDocumentStore Documents { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdefghijklmnop");
        builder.UseSetting("SeedAdmin:Email", AdminEmail);
        builder.UseSetting("SeedAdmin:Password", AdminPassword);
        builder.UseSetting("RateLimiting:AuthPermitLimitPerMinute", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStorageInitializer>();
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IMenuRepository>();
            services.RemoveAll<IOrderRepository>();
            services.RemoveAll<IOrderQueue>();
            services.RemoveAll<IDocumentStore>();
            services.RemoveAll<AzureStorageClients>();

            services.AddSingleton<IStorageInitializer, NoopStorageInitializer>();
            services.AddSingleton<IUserRepository>(Users);
            services.AddSingleton<IMenuRepository>(Menu);
            services.AddSingleton<IOrderRepository>(Orders);
            services.AddSingleton<IOrderQueue>(Queue);
            services.AddSingleton<IDocumentStore>(Documents);
        });
    }

    /// <summary>Logs in and returns a client that sends "Authorization: Bearer {token}".</summary>
    public async Task<HttpClient> CreateClientAsAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    public Task<HttpClient> CreateAdminClientAsync() => CreateClientAsAsync(AdminEmail, AdminPassword);

    /// <summary>Registers (as Admin) and logs in a user with the given role.</summary>
    public async Task<HttpClient> CreateClientWithRoleAsync(string role)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@coffeenchill.ac.za";
        const string password = "Passw0rd!2026";

        var admin = await CreateAdminClientAsync();
        var registered = await admin.PostAsJsonAsync("/api/auth/register",
            new { email, password, fullName = $"Test {role}", role });
        registered.EnsureSuccessStatusCode();

        return await CreateClientAsAsync(email, password);
    }
}
