using System.ComponentModel.DataAnnotations;
using CoffeeNChill.Gateway.Models;

namespace CoffeeNChill.Gateway.Models.Dtos;

/// <summary>Request body for POST /api/auth/register.</summary>
public class RegisterRequest
{
    /// <summary>Email address - becomes the user's RowKey and login name.</summary>
    /// <example>barista@coffeenchill.ac.za</example>
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>At least 8 characters. Stored only as a salted PBKDF2 hash.</summary>
    /// <example>Barista#2026!</example>
    [Required, StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    /// <example>Thandi Barista</example>
    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// "Customer" (default, open to anyone), or "Staff"/"Admin" - which only an
    /// authenticated Admin may create.
    /// </summary>
    /// <example>Staff</example>
    public string? Role { get; set; }
}

/// <summary>Request body for POST /api/auth/login.</summary>
public class LoginRequest
{
    /// <example>manager@coffeenchill.ac.za</example>
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <example>Manager#2026!</example>
    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Returned by a successful login.</summary>
public class LoginResponse
{
    /// <summary>Signed JWT. Send it as "Authorization: Bearer {token}".</summary>
    public string Token { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAtUtc { get; set; }
    public UserResponse User { get; set; } = new();
}

/// <summary>Public view of a user profile (never includes the password hash).</summary>
public class UserResponse
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }

    public static UserResponse FromEntity(UserEntity entity) => new()
    {
        Email = entity.Email,
        FullName = entity.FullName,
        Role = entity.Role,
        CreatedDate = entity.CreatedDate
    };
}
