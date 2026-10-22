using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Api.Common.Errors;
using OrderManagement.Domain;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Features.Auth;

public sealed class LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed record UserResponse(Guid Id, string Username, string DisplayName, UserRole Role);

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, UserResponse User);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    OrderDbContext db,
    IPasswordHasher<User> hasher,
    TokenService tokens,
    ILogger<AuthController> logger) : ControllerBase
{
    // Verified against when the username does not exist, so both failure paths cost the same time
    // and response timing does not reveal which usernames exist.
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(new User(), "not-a-real-password");

    /// <summary>Exchange username and password for a bearer token.</summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<LoginResponse> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);

        var verification = hasher.VerifyHashedPassword(user ?? new User(), user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || !user.IsActive || verification == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("Failed login for {Username}", username);
            throw new UnauthorizedException(ErrorCodes.InvalidCredentials, "Username atau password salah.");
        }

        var (token, expiresAt) = tokens.Create(user);
        logger.LogInformation("User {Username} ({Role}) signed in", user.Username, user.Role);
        return new LoginResponse(token, "Bearer", expiresAt, ToResponse(user));
    }

    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<UserResponse> Me(CancellationToken ct)
    {
        var username = User.Identity?.Name;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct)
            ?? throw new UnauthorizedException(ErrorCodes.Unauthorized, "The account no longer exists or is inactive.");
        return ToResponse(user);
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.Username, user.DisplayName, user.Role);
}
