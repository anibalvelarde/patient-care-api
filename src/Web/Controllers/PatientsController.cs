using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Web.Authorization;

namespace Neurocorp.Api.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IPatientProfileService _patientProfileService;
    private readonly IHandleSessionEvent _sessionEventHandler;
    private readonly ISessionEventRepository _sessionEventRepository;
    private readonly ILogger<PatientsController> _logger;

    private readonly IPatientMergeService _patientMergeService;

    public PatientsController(
        ILogger<PatientsController> logger,
        IPatientProfileService patientProfileService,
        IHandleSessionEvent sessionEventHandler,
        ISessionEventRepository sessionEventRepository,
        IPatientMergeService patientMergeService)
    {
        _patientProfileService = patientProfileService;
        _sessionEventHandler = sessionEventHandler;
        _sessionEventRepository = sessionEventRepository;
        _patientMergeService = patientMergeService;
        _logger = logger;
    }

    // WP-30 (U2): paged-by-default. ⚠ BREAKING (was bare PatientProfile[]) — deploy with the
    // WP-30C UI as one event.
    [HttpGet]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResult<PatientProfile>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatients(
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var (safePage, safeSize) = PagingParams.Clamp(page, pageSize, defaultPageSize: 30);
        var result = await _patientProfileService.GetPagedAsync(search, isActive, safePage, safeSize);
        return Ok(result);
    }

    // WP-30 (U2): typeahead for pickers — capped at 20 (gate G1), never the full census.
    // Rides Patients.View like the list: no matrix change.
    [HttpGet("lookup")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientLookupItem>))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LookupPatients([FromQuery] string? q = null)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "Query parameter 'q' is required (min length 1).", title: "Bad Request");
        }
        var items = await _patientProfileService.LookupAsync(q, LookupResultCap);
        return Ok(items);
    }

    private const int LookupResultCap = 20;

    [HttpGet("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientProfile))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatient(int id)
    {
        var patient = await _patientProfileService.GetByIdAsync(id);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    [HttpGet("{id:int}/pastdue")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientPastDueInfo))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPastDueSessions(int id)
    {
        var pastDueInfo = await PackagePastDueInfoAsync(id);
        if (pastDueInfo is not null && pastDueInfo.Party!.IsValid)
        {
            return Ok(pastDueInfo);  
        }
        return NotFound();
    }    

    [HttpGet("pastdue")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsDelinquentView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientPastDueInfo>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllPastDuePatients()
    {
        var pastDuePatients = await _sessionEventHandler.GetAllPatientsPastDueAsync();
        return Ok(pastDuePatients);
    }

    [HttpGet("{id:int}/caretakers")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<PatientCaretakerSummary>))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPatientCaretakers(int id)
    {
        var caretakers = await _patientProfileService.GetCaretakersForPatientAsync(id);
        return Ok(caretakers);
    }

    // WP-21 (F1): paged patient summaries ordered by most-recent-session first — backs the
    // Patients → Session History tab's patient list.
    // WP-35 addendum: envelope is SessionHistoryPagedResult (shared shape + full-set Totals);
    // all money fields (per-row and Totals) ride Appointments.ProviderAmount via
    // ProviderAmountResultFilter — callers without the claim get counts only, not a 403.
    [HttpGet("session-history")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionHistoryPagedResult))]
    public async Task<IActionResult> GetPatientSessionHistory(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30)
    {
        var (safePage, safeSize) = PagingParams.Clamp(page, pageSize, defaultPageSize: 30);
        var result = await _patientProfileService.GetSessionHistoryAsync(search, safePage, safeSize);
        return Ok(result);
    }

    // WP-21 (F1 ruling — supersedes WP-17 D-8 for patient-context session reads): gated by the
    // PATIENT domain claim (Patients.View: ACCT/AM/FD/MGR/OWN) so the Session History tab's whole
    // audience can read a patient's sessions; Appointments.View would exclude ACCT. ProviderAmount
    // confidentiality stays field-level — ProviderAmountResultFilter shapes the paged envelope too.
    [HttpGet("{patientId:int}/sessions")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsView)]
    [ProducesResponseType(typeof(PagedResult<SessionEvent>), StatusCodes.Status200OK)]
    // WP-35 (SH-3 support): additive `from`/`to` — date-only inclusive range on sessionDate,
    // bound from `yyyy-MM-dd` query strings (DateOnly, same style as the Statement endpoints).
    public async Task<IActionResult> GetPatientSessions(
        int patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool? isDiscovery = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var (safePage, safeSize) = PagingParams.Clamp(page, pageSize, defaultPageSize: 25);
        var sessions = await _sessionEventRepository.GetByPatientIdAsync(patientId, safePage, safeSize, isDiscovery, status, from, to);
        return Ok(sessions);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsEdit)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(PatientProfile))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePatient([FromBody] PatientProfileRequest patientRequest)
    {
        var createdPatient = await _patientProfileService.CreateAsync(patientRequest);
        return CreatedAtAction(nameof(CreatePatient), new { id = createdPatient.PatientId }, createdPatient);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsEdit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] PatientProfileUpdateRequest patientRequest)
    {
        var patientOnFile = await _patientProfileService.GetByIdAsync(id);
        if (patientOnFile is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"The update request is not valid for patient {id}.", title: "Bad Request");
        }

        // WP-23 (F7): field-level WRITE gate — the platform's first (read-side precedent:
        // ProviderAmountResultFilter). Changing the SENADIS flag needs its own claim (MGR/AM by
        // Questionnaire E); present-but-unchanged passes so clients that echo the full profile
        // keep working for FD. Nothing is applied on the Forbid path.
        if (patientRequest.HasSenadisDiscount.HasValue
            && patientRequest.HasSenadisDiscount.Value != patientOnFile.HasSenadisDiscount
            && !User.HasPermission(Permissions.PatientsSenadisDiscountEdit))
        {
            return Forbid();
        }

        // WP-37 (SEN-1/G4): the expiration date rides the SAME claim as the flag — one claim
        // governs both SENADIS fields, no new claim, no matrix change. Present-AND-different
        // needs Patients.SenadisDiscount.Edit; omitted/null/echoed-unchanged passes. Date-only
        // comparison: the column is a DATE, so an echoed midnight timestamp is not a "change".
        if (patientRequest.SenadisExpirationDate.HasValue
            && patientRequest.SenadisExpirationDate.Value.Date != patientOnFile.SenadisExpirationDate?.Date
            && !User.HasPermission(Permissions.PatientsSenadisDiscountEdit))
        {
            return Forbid();
        }

        // WP-24 (F3/F4): same SENADIS-style gate for the discovery-first waiver flag — changing
        // it needs Patients.RequiresDiscovery.Edit (MGR/AM by Questionnaire C; FD sets it at
        // create only). Both gates run before any update applies.
        if (patientRequest.RequiresDiscovery.HasValue
            && patientRequest.RequiresDiscovery.Value != patientOnFile.RequiresDiscovery
            && !User.HasPermission(Permissions.PatientsRequiresDiscoveryEdit))
        {
            return Forbid();
        }

        try
        {
            if (!await _patientProfileService.UpdateAsync(id, patientRequest))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Patient {id} could not be updated.", title: "Bad Request");
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // Domain-rule violation surfaced as a 400 (kept explicit — the global handler maps
            // InvalidOperationException to 500, not 400).
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message, title: "Bad Request");
        }
    }

    // WP-22 (F2): duplicate-patient merge — SYSADMIN-only by customer ruling. Patients.Merge
    // has an EMPTY grants row in the matrix (hash 6c42b5362fbc); only the System/FullAccess
    // wildcard satisfies it, so no RoleClaim was seeded. Preview is side-effect-free; execute
    // remaps sessions/plans/caretaker-links, fills survivor blanks, writes PatientMergeLog +
    // a [MERGED: ...] Notes marker, and hard-deletes the eliminated Patient + SystemUser.

    [HttpPost("merge/preview")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsMerge)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientMergePreview))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewPatientMerge([FromBody] PatientMergeRequest request)
    {
        var preview = await _patientMergeService.PreviewAsync(request);
        return Ok(preview);
    }

    [HttpPost("merge")]
    [Authorize(Policy = AuthPolicy.PermissionPrefix + Permissions.PatientsMerge)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PatientMergeResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MergePatients([FromBody] PatientMergeRequest request)
    {
        var result = await _patientMergeService.MergeAsync(request);
        return Ok(result);
    }

    private async Task<PatientPastDueInfo> PackagePastDueInfoAsync(int patientId)
    {
        var patient = await _patientProfileService.GetByIdAsync(patientId);
        if (patient is not null)
        {
            // WP-29: scoped fetch — the patient filter runs in SQL, not over the full table.
            var patientPastDueSessions = await _sessionEventHandler.GetPastDueByPatientAsync(patientId);
            // WP-49: was a hand-copied `Amount − Discount`, which dropped the on-site trip
            // charge (live bug) and would have dropped the late fee. TotalCharges() is the
            // shared definition — see SessionEvent.
            var totalPastDueAmount = patientPastDueSessions.Sum(s => s.TotalCharges());
            var totalPaidSoFar = patientPastDueSessions.Sum(s => s.AmountPaid);
            _logger.LogInformation(
                "Patient [{patientName}] has {Count} sessions that are past-due. PastDue:{d} PaidSoFar:{d} ",
                patient!.PatientName, patientPastDueSessions.Count(), totalPastDueAmount, totalPaidSoFar);

            return new PatientPastDueInfo
            {
                Party = patient,
                PastDueSessions = patientPastDueSessions.Count(),
                PastDueTotalAmount = totalPastDueAmount,
                AmountPaidSoFar = totalPaidSoFar,
                Delinquency = patientPastDueSessions,
            };            
        }
        return new PatientPastDueInfo() { Party = new NotFoundProfile() };
    }
}
