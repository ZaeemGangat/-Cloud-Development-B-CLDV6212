using System.Text.Json;
using Azure.Core.Serialization;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using CoffeeNChill.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // camelCase JSON in/out, matching the gateway and the Postman collections.
        services.Configure<WorkerOptions>(workerOptions =>
        {
            workerOptions.Serializer = new JsonObjectSerializer(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        });

        // Points at Azurite locally / in docker-compose, or at a real Azure
        // Storage account when a real connection string is supplied.
        var storageConnectionString =
            Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "UseDevelopmentStorage=true";

        // Table names are shared with the Part 3 gateway, so both read the same
        // settings. Part 1 accidentally used "Menultems" (lower-case L); Part 3
        // standardises on "MenuItems" for both services.
        var menuTableName = Environment.GetEnvironmentVariable("MenuTableName") ?? "MenuItems";
        var ordersTableName = Environment.GetEnvironmentVariable("OrdersTableName") ?? "Orders";
        var orderQueueName = Environment.GetEnvironmentVariable("OrderQueueName") ?? "orderprocessingqueue";

        // Part 1: menu items (Azure Table Storage)
        services.AddSingleton(_ =>
        {
            var tableClient = new TableServiceClient(storageConnectionString).GetTableClient(menuTableName);
            tableClient.CreateIfNotExists();
            return tableClient;
        });

        // Part 1: staff documents (Blob Storage - Azurite does not emulate Azure Files)
        services.AddSingleton(_ =>
        {
            var containerClient = new BlobServiceClient(storageConnectionString).GetBlobContainerClient("staff-docs");
            containerClient.CreateIfNotExists();
            return containerClient;
        });

        // Part 2: order tracking table + order processing queue
        services.AddSingleton(_ => new OrderTableStorageService(storageConnectionString, ordersTableName));
        services.AddSingleton(_ => new QueueStorageService(storageConnectionString, orderQueueName));
    })
    .Build();

host.Run();
