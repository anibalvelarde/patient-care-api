using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Entities;
using System.Diagnostics;

namespace Neurocorp.Api.Core.Services;

public class SessionEventHandler : IHandleSessionEvent
{
    private readonly ILogger<SessionEventHandler> _logger;
    private readonly ISessionEventRepository _repository;
    private readonly ITherapySessionRepository _therapySessionRepository;
    private readonly IPatientProfileService _patientService;
    private readonly ITherapistProfileService _therapistService;
    private readonly IRepository<SpecialtyType> _specialtyTypeRepository;
    private readonly ITherapistSpecialtyRepository _therapistSpecialtyRepository;
    private readonly IPatientCaretakerRepository _patientCaretakerRepository;

    public SessionEventHandler(ILogger<SessionEventHandler> logger, ISessionEventRepository repo, ITherapySessionRepository therapySessionRepo, ITherapistProfileService therapistSvc, IPatientProfileService patientSvc, IRepository<SpecialtyType> specialtyTypeRepo, ITherapistSpecialtyRepository therapistSpecialtyRepo, IPatientCaretakerRepository patientCaretakerRepo)
    {
        _logger = logger;
        _repository = repo;
        _therapySessionRepository = therapySessionRepo;
        _patientService = patientSvc;
        _therapistService = therapistSvc;
        _specialtyTypeRepository = specialtyTypeRepo;
        _therapistSpecialtyRepository = therapistSpecialtyRepo;
        _patientCaretakerRepository = patientCaretakerRepo;
    }

    public async Task<IEnumerable<SessionEvent>> GetAllAsync()
    {
        return (await _repository.GetAllAsync()) ?? [];
    }


    public async Task<IEnumerable<SessionEvent>> GetAllByTargetDateAsync(DateOnly targetDate)
    {
        _logger.LogInformation("Started to fetch Sessions for this date {targetDate}", targetDate);
        var stopwatch = Stopwatch.StartNew();

        // Starting to fetch
        stopwatch.Restart();
        var events = (await _repository.GetAllByTargetDateAsync(targetDate)) ?? [];
        _logger.LogInformation("Fetched events from repository in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Selecting and sorting events
        stopwatch.Restart();
        var sortedEvents = events
            .OrderBy(e => e.Patient)
            .ToList();
        _logger.LogInformation("Selected and sorted events in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Returning the list to the caller
        _logger.LogInformation("Returning the list to the caller with {Count} events", sortedEvents.Count);
        return sortedEvents;
    }


    public async Task<IEnumerable<SessionEvent>> GetAllPastDueAsync()
    {
        _logger.LogInformation("Started to fetch Sessions for past-due events");
        var stopwatch = Stopwatch.StartNew();

        // Starting to fetch
        stopwatch.Restart();
        var pastDueEvents = (await _repository.GetAllPastDueAsync()) ?? new List<SessionEvent>();
        _logger.LogInformation("Fetched past-due events from repository in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Selecting and sorting past-due events
        stopwatch.Restart();
        var sortedPastDueEvents = pastDueEvents
            .Where(t => t.IsPastDue)
            .OrderByDescending(e => e.SessionDate)
            .ToList();
        _logger.LogInformation("Selected and sorted past-due events in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Returning past-due event list to the caller
        _logger.LogInformation("Returning the list to the caller with {Count} past-due events", sortedPastDueEvents.Count);
        return sortedPastDueEvents;
    }

    public async Task<IEnumerable<PatientPastDueInfo>> GetAllPatientsPastDueAsync()
    {
        _logger.LogInformation("Started to fetch all patients with past-due sessions");
        var stopwatch = Stopwatch.StartNew();

        var pastDueSessions = await GetAllPastDueAsync();
        var groupedByPatient = pastDueSessions.GroupBy(s => s.PatientId);

        var results = new List<PatientPastDueInfo>();
        foreach (var group in groupedByPatient)
        {
            var patient = await _patientService.GetByIdAsync(group.Key);
            if (patient is null) continue;

            var sessions = group.ToList();
            results.Add(new PatientPastDueInfo
            {
                Party = patient,
                PastDueSessions = sessions.Count,
                PastDueTotalAmount = sessions.Sum(s => s.Amount - s.Discount),
                AmountPaidSoFar = sessions.Sum(s => s.AmountPaid),
                Delinquency = sessions,
            });
        }

        _logger.LogInformation("Found {Count} patients with past-due sessions in {ElapsedMilliseconds} ms",
            results.Count, stopwatch.ElapsedMilliseconds);
        return results;
    }

    public async Task<SessionEvent?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<SessionEvent> CreateAsync(SessionEvent entity)
    {
        return await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task UpdateAsync(SessionEvent entity)
    {
        await Task.FromException<SessionEvent>(new NotImplementedException());
    }

    public async Task<SessionEvent> CreateAsync(SessionEventRequest request)
    {
        _logger.LogInformation("Started creating a new therapy session from request.");
        var (pProfile, tProfile) = await FetchTargetPartiesAsync(request);

        // WP-23 (F10): hard block — no caretaker on file, no booking (owner ruling: synthetic
        // placeholders satisfy the rule; they are PatientCaretaker links like any other).
        // ArgumentException → 400 via GlobalExceptionHandler.
        var caretakerLinks = await _patientCaretakerRepository.GetByPatientIdAsync(request.PatientId);
        if (!caretakerLinks.Any())
        {
            throw new ArgumentException("A caretaker must be linked to this patient before booking.");
        }

        // WP-23 (F7): statutory SENADIS discount FLOOR — 20% of Amount, staff may grant more,
        // never less. (A pending "no stacking" customer ruling would tighten this to exactly-20%.)
        if (pProfile!.HasSenadisDiscount)
        {
            request.Discount = Math.Max(request.Discount, Math.Round(0.20m * request.Amount, 2));
        }

        var specialty = await ResolveSpecialtyAsync(request);

        // Discovery-first enforcement: treatment specialties require a completed discovery session.
        // WP-24 (F3/F4): only when the patient's RequiresDiscovery flag is set — false = waived
        // (e.g. legacy-imported patients), which skips the check entirely.
        if (specialty != null && !specialty.IsDiscovery && pProfile!.RequiresDiscovery)
        {
            var hasDiscovery = await _therapySessionRepository.HasCompletedDiscoveryAsync(request.PatientId);
            if (!hasDiscovery)
                throw new ArgumentException(
                    "Patient requires a completed discovery session before treatment sessions can be scheduled.");
        }

        // Specialty validation: therapist must hold the requested specialty
        if (specialty != null && request.SpecialtyTypeId.HasValue)
        {
            var hasSpecialty = await _therapistSpecialtyRepository
                .HasTherapistSpecialtyAsync(request.TherapistId, specialty.Id);
            if (!hasSpecialty)
                throw new ArgumentException(
                    $"Therapist {tProfile!.TherapistName} does not have the {specialty.Name} qualification.");
        }

        var newTherapySession = await _therapySessionRepository.AddAsync(MapToNewSessionEvent(pProfile!, tProfile!, request, specialty));
        _logger.LogInformation($"New TherapySession was created TSid:[{newTherapySession.Id}]");
        var confirmedStatuses = new HashSet<int> { 2, 4, 6, 7 };
        return new SessionEvent()
        {
            SessionId = newTherapySession.Id,
            SessionDate = newTherapySession.SessionDate,
            SessionTime = newTherapySession.SessionTime,
            Patient = pProfile!.PatientName,
            Therapist = tProfile!.TherapistName,
            TherapyTypes = newTherapySession.TherapyTypes,
            Amount = newTherapySession.Amount,
            Discount = newTherapySession.DiscountAmount,
            AmountDue = newTherapySession.Amount,
            IsPastDue = false,
            IsPaidOff = false,
            Notes = newTherapySession.Notes,
            AppointmentStatusId = newTherapySession.AppointmentStatusId,
            StatusName = newTherapySession.AppointmentStatus?.Name ?? "Proposed",
            IsConfirmed = confirmedStatuses.Contains(newTherapySession.AppointmentStatusId),
            SiteId = newTherapySession.SiteId,
            SiteName = newTherapySession.Site?.SiteName,
            SpecialtyTypeId = newTherapySession.SpecialtyTypeId,
            SpecialtyAbbreviation = specialty?.Abbreviation,
            SpecialtyName = specialty?.Name,
            IsDiscovery = specialty?.IsDiscovery,
        };
    }

    // WP-24 (audit F1): lets the controller ask "is this a discovery-specialty request?" BEFORE
    // CreateAsync, so the Patients.StartDiscovery gate can 403 with nothing created. Uses the
    // same resolution CreateAsync will run — Core stays free of HttpContext; the permission
    // check itself lives in SessionsController.
    public async Task<bool> IsDiscoveryRequestAsync(SessionEventRequest request)
    {
        var specialty = await ResolveSpecialtyAsync(request);
        return specialty?.IsDiscovery == true;
    }

    public async Task<bool> UpdateAsync(int sessionEventId, SessionEventUpdateRequest request)
    {
        ArgumentNullException.ThrowIfNull(nameof(request));

        // ensure both patient and therapist exist...
        var sessionOnFile = await _repository.GetByIdAsync(sessionEventId);
        if (sessionOnFile is not null)
        {
            await _repository.UpdateAsync(sessionEventId, request);
            return true;
        }
        return false;
    }

    public Task DeleteAsync(int id)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> VerifyRequestAsync(int sessionAggId, SessionEventUpdateRequest request)
    {
        var profile = await this.GetByIdAsync(sessionAggId);
        if (profile != null)
        {
            return true;
        }
        return false;
    }

    private static TherapySession MapToNewSessionEvent(PatientProfile pProfile, TherapistProfile tProfile, SessionEventRequest req, SpecialtyType? specialty)
    {
        // WP-23 (Questionnaire E ruling): the therapist fee applies to the NET amount — after
        // discounts — matching BulkSchedulingService. Flat fees are unaffected by the base.
        var netAmount = req.Amount - req.Discount;
        var calcProviderAmt = tProfile.CalculateFee(netAmount);
        var calcGrossProfit = netAmount - calcProviderAmt;
        // When SpecialtyTypeId is provided, populate TherapyTypes from specialty for backward compat
        var therapyTypes = specialty != null ? specialty.Abbreviation : req.TherapyType;
        return new TherapySession()
        {
            PatientId = pProfile.PatientId,
            TherapistId = tProfile.TherapistId,
            SessionDate = req.SessionDate,
            SessionTime = req.SessionTime,
            TherapyTypes = therapyTypes,
            Duration = req.Duration,
            Amount = req.Amount,
            DiscountAmount = req.Discount,
            ProviderAmount = calcProviderAmt,
            GrossProfit = calcGrossProfit,
            Notes = req.Notes,
            AppointmentStatusId = req.AppointmentStatusId,
            SiteId = req.SiteId,
            SpecialtyTypeId = specialty?.Id ?? req.SpecialtyTypeId,
        };
    }

    private async Task<SpecialtyType?> ResolveSpecialtyAsync(SessionEventRequest request)
    {
        // If SpecialtyTypeId is provided, use it directly
        if (request.SpecialtyTypeId.HasValue)
        {
            var specialty = await _specialtyTypeRepository.GetByIdAsync(request.SpecialtyTypeId.Value);
            if (specialty == null)
            {
                _logger.LogWarning("SpecialtyTypeId {Id} not found, ignoring", request.SpecialtyTypeId.Value);
            }
            return specialty;
        }

        // If only free-text TherapyType is provided, try to resolve by abbreviation
        if (!string.IsNullOrEmpty(request.TherapyType) && request.TherapyType != "N/A")
        {
            var allSpecialties = await _specialtyTypeRepository.GetAllAsync();
            var matched = allSpecialties.FirstOrDefault(s =>
                s.Abbreviation.Equals(request.TherapyType, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                _logger.LogInformation("Resolved free-text TherapyType '{Type}' to SpecialtyTypeId {Id}", request.TherapyType, matched.Id);
            }
            return matched;
        }

        return null;
    }

    public async Task<IEnumerable<DiscoverySessionSummary>> GetCompletedDiscoverySessionsAsync(int patientId)
    {
        var sessions = await _therapySessionRepository.GetCompletedDiscoverySessionsAsync(patientId);
        return sessions.Select(s => new DiscoverySessionSummary
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate,
            SpecialtyAbbreviation = s.SpecialtyType?.Abbreviation ?? "N/A",
            TherapistName = s.Therapist?.User?.GetFullName() ?? "Unknown",
        });
    }

    private async Task<(PatientProfile?, TherapistProfile?)> FetchTargetPartiesAsync(SessionEventRequest request)
    {
        return await FetchTargetPartiesAsync(request.PatientId, request.TherapistId);
    }

    private async Task<(PatientProfile?, TherapistProfile?)> FetchTargetPartiesAsync(int patientId, int therapistId)
    {
        var verifyPatient = await _patientService.GetByIdAsync(patientId);
        var verifyTherapist = await _therapistService.GetByIdAsync(therapistId);

        if (verifyPatient is null)
        {
            throw new ArgumentNullException(nameof(patientId), "Patient not found.");
        }
        
        if (verifyTherapist is null)
        {
            throw new ArgumentNullException(nameof(therapistId), "Therapist not found.");
        }

        return (verifyPatient, verifyTherapist);
    }
}