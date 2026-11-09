using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Options;
using Microsoft.Extensions.Options;

namespace CoffeeNChill.Gateway.Services;

/// <summary>
/// On startup: creates the tables/container/queue and the seed Admin account.
/// Retries because in docker-compose the gateway can start before Azurite is
/// accepting connections (depends_on only waits for the container to start).
/// </summary>
public class StartupInitializer : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<StartupInitializer> _logger;

    public StartupInitializer(IServiceProvider services, ILogger<StartupInitializer> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await InitializeAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Storage not ready (attempt {Attempt}/{Max}): {Message}. Retrying in 3s...", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<IStorageInitializer>().EnsureCreatedAsync(cancellationToken);

        var seed = provider.GetRequiredService<IOptions<SeedAdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            _logger.LogWarning("No SeedAdmin configured - set SeedAdmin__Email and SeedAdmin__Password to create the first Admin.");
            return;
        }

        var created = await provider.GetRequiredService<IUserService>()
            .EnsureAdminAsync(seed.Email, seed.Password, seed.FullName, cancellationToken);
        if (created)
        {
            _logger.LogInformation("Seed Admin {Email} created.", seed.Email);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
