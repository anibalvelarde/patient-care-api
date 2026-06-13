using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// WP-17 field-level confidentiality. Strips <see cref="SessionEvent.ProviderAmount"/> (the therapist
/// payout) from responses when the caller lacks <c>Appointments.ProviderAmount</c> — the access-control
/// matrix grants that claim to MGR/AM only, so FrontDesk must never receive the figure. Setting the
/// field to <c>null</c> omits it from the JSON entirely (SessionEvent marks it
/// <c>[JsonIgnore(WhenWritingNull)]</c>), making the confidentiality real rather than UI-cosmetic.
///
/// Registered globally (Startup), so it covers every SessionEvent-returning endpoint today and any
/// added later. All current SessionEvent endpoints are appointments-context, so the governing claim is
/// <c>Appointments.ProviderAmount</c>. (<c>Dashboard.ProviderAmount</c> is a reserved/future claim with
/// no endpoint yet; a future dashboard DTO would get its own shaping keyed on that claim.)
/// </summary>
public class ProviderAmountResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not null } result
            && IsSessionEventPayload(result.Value)
            && !context.HttpContext.User.HasPermission(Permissions.AppointmentsProviderAmount))
        {
            result.Value = Redact(result.Value);
        }

        await next();
    }

    private static bool IsSessionEventPayload(object value) =>
        value is SessionEvent || value is IEnumerable<SessionEvent>;

    private static object Redact(object value)
    {
        switch (value)
        {
            case SessionEvent single:
                single.ProviderAmount = null;
                return single;
            case IEnumerable<SessionEvent> many:
                // Materialize so a lazy sequence isn't re-enumerated (unshaped) at serialization time.
                var list = many.ToList();
                foreach (var sessionEvent in list)
                {
                    sessionEvent.ProviderAmount = null;
                }
                return list;
            default:
                return value;
        }
    }
}
