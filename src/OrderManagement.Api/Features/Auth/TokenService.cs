using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Domain;

namespace OrderManagement.Api.Features.Auth;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public const string UsernameClaim = "name";
    public const string DisplayNameClaim = "display_name";
    public const string RoleClaim = "role";

    public (string Token, DateTimeOffset ExpiresAt) Create(User user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.LifetimeMinutes);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(UsernameClaim, user.Username),
                new Claim(DisplayNameClaim, user.DisplayName),
                new Claim(RoleClaim, user.Role.ToString())
            ]),
            SigningCredentials = new SigningCredentials(jwt.CreateKey(), SecurityAlgorithms.HmacSha256)
        });

        return (token, expiresAt);
    }
}
