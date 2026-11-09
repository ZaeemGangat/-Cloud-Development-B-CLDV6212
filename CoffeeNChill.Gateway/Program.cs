using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Options;
using CoffeeNChill.Gateway.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------
builder.Services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));
builder.Services.Configure<SeedAdminOptions>(config.GetSection(SeedAdminOptions.SectionName));

// Fail fast on startup rather than run with a weak or missing signing key.
builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection(JwtOptions.SectionName))
    .Validate(o => Encoding.UTF8.GetByteCount(o.Key ?? string.Empty) >= JwtOptions.MinimumKeyBytes,
        $"Jwt:Key must be at least {JwtOptions.MinimumKeyBytes} bytes. " +
        "Set it with the Jwt__Key environment variable (JWT_KEY in the VPS .env file).")
    .ValidateOnStart();

// ---------------------------------------------------------------------------
// Azure Storage (Users / MenuItems / Orders tables, staff-docs blobs, order queue)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<AzureStorageClients>();
builder.Services.AddSingleton<IStorageInitializer, AzureStorageInitializer>();
builder.Services.AddSingleton<IUserRepository, TableUserRepository>();
builder.Services.AddSingleton<IMenuRepository, TableMenuRepository>();
builder.Services.AddSingleton<IOrderRepository, TableOrderRepository>();
builder.Services.AddSingleton<IOrderQueue, StorageOrderQueue>();
builder.Services.AddSingleton<IDocumentStore, BlobDocumentStore>();

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddHostedService<StartupInitializer>();

// ---------------------------------------------------------------------------
// JWT Bearer authentication + role-based authorization
// ---------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured from the bound JwtOptions (read when the first request is authenticated,
// so values from environment variables and test hosts are always picked up).
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        // Keep claim types exactly as issued (ClaimTypes.Name/Email/Role URIs).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.Key),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();

// Limit login/register attempts per client IP (brute-force protection).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("RateLimiting:AuthPermitLimitPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// CORS for the student web portal / staff tills. Empty list = allow any origin (demo).
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length == 0) policy.AllowAnyOrigin();
    else policy.WithOrigins(allowedOrigins);
    policy.AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------------------
// Swagger / OpenAPI with XML docs and a Bearer "Authorize" button
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CoffeeNChill API Gateway",
        Version = "v3.0",
        Description =
            "Unified, JWT-secured gateway for the CoffeeNChill campus canteen (CLDV6212 Part 3).\n\n" +
            "**Public:** view the menu, place and track orders.\n\n" +
            "**Admin/Staff:** change the menu, manage staff documents, see all orders and update order status.\n\n" +
            "1. `POST /api/auth/login` (seed Admin or a Staff account).\n" +
            "2. Click **Authorize** and paste the `token` value.\n" +
            "3. Call the protected endpoints."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Paste the JWT from POST /api/auth/login (without the word 'Bearer').",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

// Swagger stays on in Production for the VPS demo (Swagger__Enabled=false hides it).
if (config.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CoffeeNChill Gateway v3.0");
        options.DocumentTitle = "CoffeeNChill API Gateway";
        options.EnablePersistAuthorization();
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CoffeeNChill.Gateway", utc = DateTime.UtcNow }))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.Run();

/// <summary>Rate-limiter policy names.</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
}

/// <summary>Exposed so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program { }
