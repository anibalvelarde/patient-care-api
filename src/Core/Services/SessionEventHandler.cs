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

    public SessionEventHandler(ILogger<SessionEventHandler> logger, ISessionEventRepository repo, ITherapySessionRepository therapySessionRepo, ITherapistProfileService therapistSvc, IPatientProfileService patientSvc)
    {
        _logger = logger;
        _repository = repo;
        _therapySessionRepository = therapySessionRepo;
        _patientService = patientSvc;
        _therapistService = therapistSvc;
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
        var events = (await _repository.GetAllByTargetDateAsync(targetDate)) ?? new List<SessionEvent>();
        _logger.LogInformation("Fetched events from repository in {ElapsedMilliseconds} ms", stopwatch.ElapsedMilliseconds);

        // Selecting and sorting past-due events
        stopwatch.Restart();
        var sortedEvents = events
            .OrderByDescending(e => e.SessionDate)
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

        // Returning the list to the caller
        _logger.LogInformation("Returning the list to the caller with {Count} past-due events", sortedPastDueEvents.Count);

        return sortedPastDueEvents;
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
        var newTherapySession = await _therapySessionRepository.AddAsync(MapToNewSessionEvent(pProfile!, tProfile!, request));
        _logger.LogInformation($"New TherapySession was created TSid:[{newTherapySession.Id}]");
        return new SessionEvent()
        {
            SessionId = newTherapySession.Id,
            SessionDate = DateOnly.FromDateTime(newTherapySession.SessionDate),
            Patient = pProfile!.PatientName,
            Therapist = tProfile!.TherapistName,
            TherapyTypes = "TBD",
            Amount = newTherapySession.Amount,
            Discount = newTherapySession.DiscountAmount,
            AmountDue = newTherapySession.Amount,
            IsPastDue = false,
            IsPaidOff = false,
            Notes = newTherapySession.Notes
        };
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

    public Task<bool> VerifyRequestAsync(int sessionAggId, SessionEventUpdateRequest request)
    {
        throw new NotImplementedException();
    }

    private static TherapySession MapToNewSessionEvent(PatientProfile pProfile, TherapistProfile tProfile, SessionEventRequest req)
    {
        var calcProviderAmt = tProfile.CalculateFee(req.Amount);
        var calcGrossProfit = CalculateGrossProfit(tProfile, req);
        return new TherapySession()
        {
            PatientId = pProfile.PatientId,
            TherapistId = tProfile.TherapistId,
            SessionDate = req.SessionDate.ToDateTime(TimeOnly.MinValue),
            Duration = req.Duration,
            Amount = req.Amount,
            DiscountAmount = req.Discount,
            ProviderAmount = calcProviderAmt,
            GrossProfit = calcGrossProfit,
            Notes = req.Notes
        };
    }

    private static decimal CalculateGrossProfit(TherapistProfile tProfile, SessionEventRequest request)
    {
        return request.Amount - request.Discount - tProfile.CalculateFee(request.Amount);
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