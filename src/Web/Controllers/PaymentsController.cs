using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentRecordService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(ILogger<PaymentsController> logger, IPaymentRecordService paymentService)
    {
        _logger = logger;
        _paymentService = paymentService;
    }

    // WP-17 access-control (decision D-1): intentionally authenticated-only — payment-type
    // reference data for dropdowns that every role needs; not matrix-gated. Stays in
    // conformance-baseline.txt. See planning/active/wp-17b2-endpoint-claim-map.md.
    [HttpGet("types")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PaymentTypeInfo>))]
    public async Task<IActionResult> GetPaymentTypes()
    {
        var types = await _paymentService.GetPaymentTypesAsync();
        return Ok(types);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PaymentRecord>))]
    public async Task<IActionResult> GetPayments([FromQuery] int? caretakerId)
    {
        if (caretakerId.HasValue)
        {
            var payments = await _paymentService.GetByCaretakerAsync(caretakerId.Value);
            return Ok(payments);
        }
        var all = await _paymentService.GetAllAsync();
        return Ok(all);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PaymentRecord))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayment(int id)
    {
        var payment = await _paymentService.GetByIdAsync(id);
        if (payment == null) return NotFound();
        return Ok(payment);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(PaymentRecord))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePayment([FromBody] PaymentRecordRequest request)
    {
        var created = await _paymentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetPayment), new { id = created.PaymentId }, created);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePayment(int id, [FromBody] PaymentRecordUpdateRequest request)
    {
        await _paymentService.UpdateAsync(id, request);
        return NoContent();
    }
}
