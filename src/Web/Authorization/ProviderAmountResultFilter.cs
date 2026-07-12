using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;

namespace Neurocorp.Api.Web.Authorization;

/// <summary>
/// WP-17 field-level confidentiality. Strips <see cref="SessionEvent.ProviderAmount"/> (the therapist
/// payout) from responses when the caller lacks <c>Appointments.ProviderAmount</c> — the access-control
/// matrix grants that claim to AM/MGR/OWN only, so FrontDesk (and Accountant) must never receive the
/// figure. Setting the field to <c>null</c> omits it from the JSON entirely (SessionEvent marks it
/// <c>[JsonIgnore(WhenWritingNull)]</c>), making the confidentiality real rather than UI-cosmetic.
///
/// Registered globally (Startup), so it covers every SessionEvent-returning endpoint today and any
/// added later — including WP-21's <see cref="PagedResult{T}"/>-of-SessionEvent envelope. Note that
/// endpoint-level gating and this field-level shaping are keyed on different claims: WP-21's
/// patient-context session reads are reachable via <c>Patients.View</c>, which is exactly why ACCT/FD
/// get redacted rows there rather than a 403. (<c>Dashboard.ProviderAmount</c> is a reserved/future
/// claim with no endpoint yet; a future dashboard DTO would get its own shaping keyed on that claim.)
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
        value is SessionEvent || value is IEnumerable<SessionEvent> || value is PagedResult<SessionEvent>;

    private static object Redact(object value)
    {
        switch (value)
        {
            case SessionEvent single:
                single.ProviderAmount = null;
                return single;
            case PagedResult<SessionEvent> paged:
                // WP-21: unwrap the paged envelope — a closed type-match (not reflection over
                // "anything with Items") so the shaped surface stays enumerable and testable.
                var shapedItems = paged.Items.ToList();
                foreach (var sessionEvent in shapedItems)
                {
                    sessionEvent.ProviderAmount = null;
                }
                paged.Items = shapedItems;
                return paged;
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
