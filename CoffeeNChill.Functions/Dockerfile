# ---------------------------------------------------------------------------
# CoffeeNChill Azure Functions (dotnet-isolated) container image
#
# Part 1: menu + staff-document HTTP functions
# Part 2: order queue (orderprocessingqueue) + queue-triggered order processor
# Part 3: unchanged role - runs behind the ASP.NET Core gateway and processes
#         queued orders. In docker-compose.yml its port is NOT published, so
#         only the gateway is reachable from the internet.
#
# Build (from this folder):
#   docker build -t <dockerhub_username>/coffeenchill-functions:v3.0 .
# ---------------------------------------------------------------------------

# ---- Stage 1: build & publish the isolated worker project ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CoffeeNChill.Functions.csproj ./
RUN dotnet restore "CoffeeNChill.Functions.csproj"

COPY . .
RUN dotnet publish "CoffeeNChill.Functions.csproj" -c Release -o /home/site/wwwroot --no-restore

# ---- Stage 2: official Azure Functions dotnet-isolated runtime ----
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
    ASPNETCORE_URLS=http://+:80 \
    MenuTableName=MenuItems \
    OrdersTableName=Orders \
    OrderQueueName=orderprocessingqueue \
    OrderProcessing__StepDelaySeconds=2

COPY --from=build ["/home/site/wwwroot", "/home/site/wwwroot"]
