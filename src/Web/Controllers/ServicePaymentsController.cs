using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/service-payments")]
public class ServicePaymentsController : ControllerBase
{
    private readonly IServicePaymentService _servicePaymentService;
    private readonly ILogger<ServicePaymentsController> _logger;

    public ServicePaymentsController(ILogger<ServicePaymentsController> logger, IServicePaymentService servicePaymentService)
    {
        _logger = logger;
        _servicePaymentService = servicePaymentService;
    }

    // WP-14: viewing therapist payroll exposes ProviderAmount-derived figures, so it follows the
    // same confidentiality rule as Appointments.ProviderAmount — MGR/AM only, FD excluded.
    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<ServicePaymentRecord>))]
    public async Task<IActionResult> GetServicePayments([FromQuery] int therapistId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var payments = await _servicePaymentService.GetByTherapistAsync(therapistId, from, to);
        return Ok(payments);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ServicePaymentRecord))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServicePayment(int id)
    {
        var payment = await _servicePaymentService.GetByIdAsync(id);
        if (payment == null) return NotFound();
        return Ok(payment);
    }

    // Feeds the "Pay Therapist" wizard: completed sessions in range that still owe the therapist.
    [HttpGet("unpaid-sessions")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<UnpaidProviderSessionSummary>))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUnpaidSessions([FromQuery] int therapistId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var sessions = await _servicePaymentService.GetUnpaidProviderSessionsAsync(therapistId, from, to);
        return Ok(sessions);
    }

    // Pure date helper for the date-range picker default. Gated with View for consistency
    // (the whole feature is MGR/AM-only); the window is a UX hint, not a constraint.
    [HttpGet("quincena")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(QuincenaWindow))]
    public IActionResult GetQuincena([FromQuery] DateOnly? date)
    {
        var window = _servicePaymentService.GetQuincenaWindow(date ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return Ok(window);
    }

    // WP-14: issuing a disbursement is MGR-only (owner-level). The append-only reversal/adjust
    // capability arrives with WP-14.5 (separate claim).
    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsRecord)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ServicePaymentRecord))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateServicePayment([FromBody] ServicePaymentRequest request)
    {
        var created = await _servicePaymentService.CreateAsync(request);
        return CreatedAtAction(nameof(GetServicePayment), new { id = created.ServicePaymentId }, created);
    }
}
