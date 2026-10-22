using System.Text.RegularExpressions;
using OrderManagement.Infrastructure;

namespace OrderManagement.Api.Common;

/// <summary>
/// Resolves the acting user for audit columns from the X-User-Id header. This prototype has no
/// authentication; in production the value would come from the authenticated principal (JWT claim).
/// </summary>
public sealed partial class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string HeaderName = "X-User-Id";
    public const string Anonymous = "anonymous";

    public string UserName
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers[HeaderName].ToString();
            return !string.IsNullOrEmpty(value) && SafeUserName().IsMatch(value) ? value : Anonymous;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._@\-]{1,100}$")]
    private static partial Regex SafeUserName();
}
