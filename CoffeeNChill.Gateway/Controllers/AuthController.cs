using System.Security.Claims;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CoffeeNChill.Gateway.Controllers;

/// <summary>
/// Registration and login. User profiles live in the "Users" Azure Table
/// (PartitionKey = role, RowKey = email) with hashed passwords.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[Tags("1. Authentication")]
public class AuthController : ControllerBase
{
    private readonly IUserService _users;
    private readonly ITokenService _tokens;

    public AuthController(IUserService users, ITokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    /// <summary>Register a new user.</summary>
    /// <remarks>
    /// The password is hashed (PBKDF2 via ASP.NET Core Identity PasswordHasher)
    /// before the profile is written to the Users table.
    ///
    /// - Anyone may register a **Customer** (student) account - leave `role` empty.
    /// - **Staff** and **Admin** accounts can only be created by a logged-in Admin:
    ///   click *Authorize* and paste an Admin token first.
    /// </remarks>
    /// <response code="201">User created.</response>
    /// <response code="400">Validation failed or unknown role.</response>
    /// <response code="403">Only an Admin may create Staff/Admin accounts.</response>
    /// <response code="409">Email already registered.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _users.RegisterAsync(request, User, cancellationToken);
        return result.Status switch
        {
            RegistrationStatus.Created => StatusCode(StatusCodes.Status201Created, UserResponse.FromEntity(result.User!)),
            RegistrationStatus.InvalidRole => BadRequest(new ApiError(result.Error!)),
            RegistrationStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new ApiError(result.Error!)),
            _ => Conflict(new ApiError(result.Error!))
        };
    }

    /// <summary>Log in and receive a JWT Bearer token.</summary>
    /// <remarks>
    /// Validates the credentials against the Users table and returns a signed
    /// JWT containing ClaimTypes.Name, ClaimTypes.Email and ClaimTypes.Role.
    /// Use it as `Authorization: Bearer {token}`.
    /// </remarks>
    /// <response code="200">Credentials valid - token returned.</response>
    /// <response code="401">Invalid email or password.</response>
    /// <response code="429">Too many login attempts - wait a minute.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _users.ValidateCredentialsAsync(request.Email, request.Password, cancellationToken);
        if (user is null)
        {
            // Same message for unknown email and wrong password.
            return Unauthorized(new ApiError("Invalid email or password."));
        }

        return Ok(_tokens.CreateToken(user));
    }

    /// <summary>Show the claims in the current token.</summary>
    /// <remarks>Handy for demonstrating which name, email and role the gateway sees.</remarks>
    /// <response code="200">The authenticated user's claims.</response>
    /// <response code="401">No or invalid token.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me() => Ok(new
    {
        name = User.FindFirstValue(ClaimTypes.Name),
        email = User.FindFirstValue(ClaimTypes.Email),
        role = User.FindFirstValue(ClaimTypes.Role),
        expiresAtUtc = User.FindFirstValue("exp") is { } exp && long.TryParse(exp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : (DateTime?)null
    });
}
