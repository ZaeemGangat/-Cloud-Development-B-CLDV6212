# ---------------------------------------------------------------------------
# CoffeeNChill ASP.NET Core Web API Gateway (Part 3)
#
# Build (from this folder):
#   docker build -t <dockerhub_username>/coffeenchill-gateway:v3.0 .
# Run (needs a JWT key of 32+ characters and a storage connection string):
#   docker run -p 5000:8080 -e Jwt__Key=... -e Storage__ConnectionString=... \
#     <dockerhub_username>/coffeenchill-gateway:v3.0
# ---------------------------------------------------------------------------

# ---- Stage 1: restore, build and publish ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CoffeeNChill.Gateway.csproj ./
RUN dotnet restore "CoffeeNChill.Gateway.csproj"

COPY . .
RUN dotnet publish "CoffeeNChill.Gateway.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Stage 2: small ASP.NET Core runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Run as the non-root "app" user that ships with the .NET 8 images.
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "CoffeeNChill.Gateway.dll"]
