using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Common;

/// <summary>
/// The signed-in user (from the bearer token) for audit columns. Work done outside an
/// authenticated request — migrations, seeding — is attributed to "system".
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string System = "system";

    public string UserName =>
        accessor.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : System;
}
