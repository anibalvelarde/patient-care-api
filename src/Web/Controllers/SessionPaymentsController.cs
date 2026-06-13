using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

// Session-payment query, split out of SessionsController (Chunk 4). Keeps the api/sessions route
// prefix so the URL is unchanged.
[ApiController]
[Route("api/sessions")]
public class SessionPaymentsController : ControllerBase
{
    private readonly IPaymentRecordService _paymentService;

    public SessionPaymentsController(IPaymentRecordService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{id}/payments")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<SessionPaymentDetail>))]
    public async Task<IActionResult> GetSessionPayments(int id)
    {
        var payments = await _paymentService.GetPaymentsForSessionAsync(id);
        return Ok(payments);
    }
}
