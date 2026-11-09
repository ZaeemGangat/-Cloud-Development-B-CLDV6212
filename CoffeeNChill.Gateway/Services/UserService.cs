using System.Security.Claims;
using CoffeeNChill.Gateway.Auth;
using CoffeeNChill.Gateway.Data;
using CoffeeNChill.Gateway.Models;
using CoffeeNChill.Gateway.Models.Dtos;
using Microsoft.AspNetCore.Identity;

namespace CoffeeNChill.Gateway.Services;

public enum RegistrationStatus { Created, InvalidRole, Forbidden, EmailTaken }

public record RegistrationResult(RegistrationStatus Status, UserEntity? User = null, string? Error = null);

public interface IUserService
{
    /// <param name="request">New user details.</param>
    /// <param name="caller">The (possibly anonymous) user making the request - only Admins may create Staff/Admin accounts.</param>
    /// <param name="cancellationToken"></param>
    Task<RegistrationResult> RegisterAsync(RegisterRequest request, ClaimsPrincipal caller, CancellationToken cancellationToken = default);

    /// <summary>Returns the user if the email/password pair is valid, otherwise null.</summary>
    Task<UserEntity?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Creates the seed Admin if no user with that email exists. Returns true if created.</summary>
    Task<bool> EnsureAdminAsync(string email, string password, string fullName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registration and login against the "Users" Azure Table. Passwords are
/// hashed with ASP.NET Core Identity's PasswordHasher (PBKDF2 with
/// HMAC-SHA512, 128-bit salt, 100,000 iterations) - plain passwords are never stored.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<UserEntity> _hasher;
    private readonly TimeProvider _clock;

    // Used to burn the same CPU time for unknown emails, so response timing
    // doesn't reveal which emails are registered.
    private static readonly UserEntity DummyUser = new();
    private readonly Lazy<string> _dummyHash;

    public UserService(IUserRepository users, IPasswordHasher<UserEntity> hasher, TimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _clock = clock;
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(DummyUser, Guid.NewGuid().ToString()));
    }

    public async Task<RegistrationResult> RegisterAsync(RegisterRequest request, ClaimsPrincipal caller, CancellationToken cancellationToken = default)
    {
        var role = string.IsNullOrWhiteSpace(request.Role) ? Roles.Customer : Roles.Normalize(request.Role);
        if (role is null)
        {
            return new(RegistrationStatus.InvalidRole, Error: $"Role must be one of: {string.Join(", ", Roles.All)}.");
        }

        // Anyone can sign up as a Customer (student). Staff and Admin accounts can
        // only be created by an authenticated Admin, otherwise anyone could
        // register as staff and change menu prices.
        if (role != Roles.Customer && !caller.IsInRole(Roles.Admin))
        {
            return new(RegistrationStatus.Forbidden, Error: $"Only an Admin can create {role} accounts. Log in as an Admin and send the Bearer token.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return new(RegistrationStatus.EmailTaken, Error: $"A user with email '{email}' already exists.");
        }

        var user = CreateEntity(email, request.Password, request.FullName.Trim(), role);
        if (!await _users.TryAddAsync(user, cancellationToken))
        {
            return new(RegistrationStatus.EmailTaken, Error: $"A user with email '{email}' already exists.");
        }

        return new(RegistrationStatus.Created, user);
    }

    public async Task<UserEntity?> ValidateCredentialsAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null)
        {
            _hasher.VerifyHashedPassword(DummyUser, _dummyHash.Value, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task<bool> EnsureAdminAsync(string email, string password, string fullName, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await _users.FindByEmailAsync(normalized, cancellationToken) is not null)
        {
            return false;
        }

        return await _users.TryAddAsync(CreateEntity(normalized, password, fullName, Roles.Admin), cancellationToken);
    }

    private UserEntity CreateEntity(string email, string password, string fullName, string role)
    {
        var user = new UserEntity
        {
            PartitionKey = role,
            RowKey = email,
            FullName = fullName,
            CreatedDate = _clock.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        return user;
    }
}
