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

    // "Run Payroll" preview: every therapist owed money in the window (count + total + sessions).
    [HttpGet("payroll-preview")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PayrollPreviewTherapist>))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPayrollPreview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var preview = await _servicePaymentService.GetPayrollPreviewAsync(from, to);
        return Ok(preview);
    }

    // "Pending therapist payments" dashboard tile: clinic-wide gross still owed to therapists.
    // Defaults to all-time (outstanding liability); optional from/to narrow it. Same View gate.
    [HttpGet("pending-summary")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PendingPayrollSummary))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendingSummary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var summary = await _servicePaymentService.GetPendingPayrollSummaryAsync(from, to);
        return Ok(summary);
    }

    // Read-only "Pending Pay" report: per-therapist decomposition behind the dashboard tile
    // (date span, distinct patients, gross billed, owed, % owed). Same View gate; all-time default.
    [HttpGet("pending-pay-report")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PendingPayReport))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPendingPayReport([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var report = await _servicePaymentService.GetPendingPayReportAsync(from, to);
        return Ok(report);
    }

    // "Run Payroll" batch: one full-allocation ServicePayment per selected therapist. MGR-only,
    // same as the single-therapist POST (issuing is ServicePayments.Record).
    [HttpPost("batch")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.ServicePaymentsRecord)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BatchPayrollResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunBatchPayroll([FromBody] BatchPayrollRequest request)
    {
        var result = await _servicePaymentService.RunBatchPayrollAsync(request);
        return Ok(result);
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
