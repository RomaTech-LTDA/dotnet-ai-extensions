using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Romatech.Extensions.Ai.Mcp.Security;

/// <summary>
/// Default rate-limit partitioning: one bucket per authenticated user, otherwise per remote IP.
/// </summary>
public static class McpClientKey
{
    /// <summary>
    /// Returns <c>user:&lt;id&gt;</c> for authenticated callers, otherwise <c>ip:&lt;address&gt;</c>.
    /// </summary>
    public static string Default(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.Identity.Name;
            if (!string.IsNullOrEmpty(id))
                return "user:" + id;
        }

        return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
