using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.Authorization;

namespace Neurocorp.Api.Web.Middleware;

/// <summary>
/// Enforces the locked design requirement that "the first login must force a password change
/// before any other actions are allowed" (remediation-2026-system-admin-bootstrap.md).
///
/// When the authenticated principal carries the must-change-password marker
/// (<see cref="SystemClaims.MustChangePasswordClaimType"/>), every request is rejected with a
/// 403 <c>application/problem+json</c> carrying <c>code = "password_change_required"</c>, except a
/// short allowlist needed to actually resolve the situation (change-password, identity lookup) plus
/// the anonymous auth/health endpoints. The marker is set at token issuance and disappears as soon
/// as the password is changed (tokens are re-issued without it), so no per-request database lookup
/// is required.
/// </summary>
public class PasswordChangeRequiredMiddleware
{
    /// <summary>Stable code the SPA can switch on to route the user to the change-password screen.</summary>
    public const string ProblemCode = "password_change_required";

    // Paths a must-change-password session is still allowed to reach. Matched as path segments
    // (case-insensitive) so trailing content / query strings do not bypass the check.
    private static readonly string[] AllowedPaths =
    {
        "/api/auth/change-password",
        "/api/auth/me",
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/health"
    };

    private readonly RequestDelegate _next;

    public PasswordChangeRequiredMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        var mustChange = user?.Identity?.IsAuthenticated == true
            && user.FindFirst(SystemClaims.MustChangePasswordClaimType)?.Value == SystemClaims.MustChangePasswordValue;

        if (mustChange && !IsAllowed(context.Request.Path))
        {
            await WriteProblem(context.Response);
            return;
        }

        await _next(context);
    }

    private static bool IsAllowed(PathString path)
    {
        foreach (var allowed in AllowedPaths)
        {
            if (path.StartsWithSegments(allowed, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task WriteProblem(HttpResponse response)
    {
        if (response.HasStarted)
        {
            return;
        }

        response.StatusCode = StatusCodes.Status403Forbidden;
        response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Password change required",
            Detail = "You must change your password before performing any other action.",
            Type = "https://httpstatuses.io/403",
            Extensions = { ["code"] = ProblemCode }
        };

        await response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
