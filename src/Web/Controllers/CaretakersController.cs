using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.BusinessObjects.Statements;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CaretakersController : ControllerBase
{
    private readonly ICaretakerProfileService _caretakerProfileService;
    private readonly IPaymentRecordService _paymentService;
    private readonly IAccountStatementService _statementService;
    private readonly ILogger<CaretakersController> _logger;

    public CaretakersController(
        ILogger<CaretakersController> logger,
        ICaretakerProfileService caretakerProfileService,
        IPaymentRecordService paymentService,
        IAccountStatementService statementService)
    {
        _logger = logger;
        _caretakerProfileService = caretakerProfileService;
        _paymentService = paymentService;
        _statementService = statementService;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<CaretakerProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllCaretakers()
    {
        var Caretakers = await _caretakerProfileService.GetAllAsync();
        return Ok(Caretakers);
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CaretakerProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCaretaker(int id)
    {
        var Caretaker = await _caretakerProfileService.GetByIdAsync(id);
        if (Caretaker == null) return NotFound();
        return Ok(Caretaker);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersEdit)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(CaretakerProfile))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCaretaker([FromBody] CaretakerProfileRequest createRequest)
    {
        var createdCaretaker = await _caretakerProfileService.CreateAsync(createRequest);
        return CreatedAtAction(nameof(GetCaretaker), new { id = createdCaretaker.CaretakerId }, createdCaretaker);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCaretaker(int id, [FromBody] CaretakerProfileUpdateRequest updateRequest)
    {
        if (!await _caretakerProfileService.VerifyRequestAsync(id, updateRequest))
        {
            throw new ArgumentException($"The update request is not valid for caretaker {id}.");
        }
        if (!await _caretakerProfileService.UpdateAsync(id, updateRequest))
        {
            throw new ArgumentException($"Caretaker {id} could not be updated.");
        }
        return NoContent();
    }

    [HttpGet("{id}/patients")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<CaretakerPatientSummary>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCaretakerPatients(int id)
    {
        var patients = await _caretakerProfileService.GetPatientsForCaretakerAsync(id);
        return Ok(patients);
    }

    [HttpPost("{id}/patients")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersLinkPatient)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LinkPatient(int id, [FromBody] PatientLinkRequest request)
    {
        var result = await _caretakerProfileService.LinkPatientAsync(id, request.PatientId, request.IsPrimary, request.Relationship);
        if (result)
        {
            return Created($"/api/caretakers/{id}/patients", null);
        }
        return Problem(statusCode: StatusCodes.Status400BadRequest,
            detail: "Link already exists or invalid data.", title: "Bad Request");
    }

    [HttpDelete("{id}/patients/{patientId}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.CaretakersLinkPatient)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnlinkPatient(int id, int patientId)
    {
        var result = await _caretakerProfileService.UnlinkPatientAsync(id, patientId);
        if (result)
        {
            return NoContent();
        }
        return NotFound();
    }

    // WP-17 (D-8): cross-resource read gated by the DATA's own domain claim (Payments.View),
    // not the parent resource (Caretakers.View). Same granted set (AM/FD/MGR) either way; this
    // tracks intent — you're reading payment data.
    [HttpGet("{id}/payments")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PaymentRecord>))]
    public async Task<IActionResult> GetCaretakerPayments(int id)
    {
        var payments = await _paymentService.GetByCaretakerAsync(id);
        return Ok(payments);
    }

    // WP-17 (D-8): cross-resource read gated by the data's own domain claim (Payments.View) —
    // unpaid-session balances are payment data. AM/FD/MGR.
    [HttpGet("{id}/unpaid-sessions")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PaymentsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<UnpaidSessionSummary>))]
    public async Task<IActionResult> GetUnpaidSessions(int id)
    {
        var sessions = await _paymentService.GetUnpaidSessionsForCaretakerAsync(id);
        return Ok(sessions);
    }

    [HttpGet("{id}/statement")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.StatementsCaretakerView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AccountStatement))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetStatement(int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var statement = await _statementService.GetStatementAsync(id, from, to);
        if (statement == null) return NotFound();
        return Ok(statement);
    }
}
