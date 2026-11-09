using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;
using CoffeeNChill.Gateway.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CoffeeNChill.Gateway.Services;

public interface ITokenService
{
    LoginResponse CreateToken(UserEntity user);
}

/// <summary>
/// Issues HMAC-SHA256 signed JWT Bearer tokens containing ClaimTypes.Name,
/// ClaimTypes.Email and ClaimTypes.Role (plus sub and jti).
/// </summary>
public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Signing key shared by token creation and validation (Program.cs).</summary>
    public static SymmetricSecurityKey CreateSigningKey(string key) => new(Encoding.UTF8.GetBytes(key));

    public LoginResponse CreateToken(UserEntity user)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.ExpiryMinutes);

        // Claim types are written exactly as given (long ClaimTypes URIs), and the
        // JwtBearer handler is configured with MapInboundClaims = false, so the
        // same claim types come back out on validation.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(CreateSigningKey(_options.Key), SecurityAlgorithms.HmacSha256));

        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            TokenType = "Bearer",
            ExpiresAtUtc = expires,
            User = UserResponse.FromEntity(user)
        };
    }
}
