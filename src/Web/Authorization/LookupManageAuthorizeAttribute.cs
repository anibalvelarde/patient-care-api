using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// WP-17 (D-10): per-type authorization for the generic <c>LookupsController</c>. The controller is
/// routed as <c>api/lookups/{tableName}</c>, so the required claim depends on the route and a single
/// static <c>[Authorize(Policy=…)]</c> can't express it. This filter maps the <c>{tableName}</c>
/// segment to its <c>Admin.Lookups.&lt;Type&gt;.Manage</c> claim and authorizes against it.
///
/// It runs in the authorization-filter stage — BEFORE model binding/validation — so an unauthorized
/// caller gets 403 regardless of request-body shape (no leaking a malformed-body 400 ahead of the
/// authorization decision). Unknown table names are left for the action to reject as 400 (the valid
/// set is non-sensitive reference data); only KNOWN tables are gated here.
///
/// Actions carrying this filter are listed in
/// <c>AccessControlConformanceTests.ImperativelyAuthorized</c> so the conformance ratchet treats them
/// as genuinely access-controlled (not reliant on the authenticated-only fallback baseline).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LookupManageAuthorizeAttribute : Attribute, IAsyncAuthorizationFilter
{
    private static readonly IReadOnlyDictionary<string, string> ManageClaimByTable =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appointment-statuses"] = Permissions.AdminLookupsAppointmentStatusManage,
            ["payment-types"] = Permissions.AdminLookupsPaymentTypeManage,
            ["role-types"] = Permissions.AdminLookupsRoleTypeManage,
            ["specialty-types"] = Permissions.AdminLookupsSpecialtyTypeManage,
        };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var tableName = context.RouteData.Values["tableName"] as string;

        // Unknown/missing table: nothing to authorize against — let the action return its 400. The
        // valid table set is public reference data, so this leaks nothing sensitive.
        if (tableName is null || !ManageClaimByTable.TryGetValue(tableName, out var claim))
            return;

        var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authService.AuthorizeAsync(context.HttpContext.User, AuthPolicy.Permission(claim));
        if (!result.Succeeded)
            context.Result = new ForbidResult();
    }
}
