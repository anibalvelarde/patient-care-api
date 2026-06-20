using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class ServicePaymentService : IServicePaymentService
{
    private const string StatusCompleted = "Completed";

    private readonly IServicePaymentRepository _servicePaymentRepo;
    private readonly ISessionServicePaymentRepository _sessionServicePaymentRepo;
    private readonly ITherapySessionRepository _sessionRepo;
    private readonly IRepository<AppointmentStatus> _appointmentStatusRepo;
    private readonly ILogger<ServicePaymentService> _logger;

    public ServicePaymentService(
        ILogger<ServicePaymentService> logger,
        IServicePaymentRepository servicePaymentRepo,
        ISessionServicePaymentRepository sessionServicePaymentRepo,
        ITherapySessionRepository sessionRepo,
        IRepository<AppointmentStatus> appointmentStatusRepo)
    {
        _logger = logger;
        _servicePaymentRepo = servicePaymentRepo;
        _sessionServicePaymentRepo = sessionServicePaymentRepo;
        _sessionRepo = sessionRepo;
        _appointmentStatusRepo = appointmentStatusRepo;
    }

    public async Task<ServicePaymentRecord> CreateAsync(ServicePaymentRequest request)
    {
        _logger.LogInformation("Creating service payment for therapist {TherapistId}, amount {Amount}", request.TherapistId, request.Amount);

        ValidateRequest(request.Amount, request.SessionAllocations);

        // Per-session: cannot apply more than the session still owes the therapist.
        var sessionIds = request.SessionAllocations.Select(a => a.SessionId).ToList();
        var existingAllocations = await _sessionServicePaymentRepo.GetBySessionIdsAsync(sessionIds);
        var appliedBySession = existingAllocations
            .GroupBy(a => a.TherapySessionId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountApplied));

        foreach (var alloc in request.SessionAllocations)
        {
            var session = await _sessionRepo.GetByIdAsync(alloc.SessionId)
                ?? throw new ArgumentException($"Session {alloc.SessionId} not found.");

            var alreadyApplied = appliedBySession.TryGetValue(alloc.SessionId, out var sum) ? sum : 0m;
            var remaining = session.ProviderAmount - alreadyApplied;
            if (alloc.AmountApplied > remaining)
                throw new ArgumentException($"Allocation {alloc.AmountApplied:C} exceeds remaining provider amount {remaining:C} for session {alloc.SessionId}.");
        }

        var servicePayment = new ServicePayment
        {
            TherapistId = request.TherapistId,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            PaymentTypeId = request.PaymentTypeId,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
        };

        var created = await _servicePaymentRepo.AddAsync(servicePayment);

        foreach (var alloc in request.SessionAllocations)
        {
            await _sessionServicePaymentRepo.AddAsync(new SessionServicePayment
            {
                ServicePaymentId = created.Id,
                TherapySessionId = alloc.SessionId,
                AmountApplied = alloc.AmountApplied,
            });
        }

        var result = await _servicePaymentRepo.GetByIdWithDetailsAsync(created.Id);
        return MapToRecord(result!);
    }

    public async Task<IEnumerable<ServicePaymentRecord>> GetByTherapistAsync(int therapistId, DateOnly? from, DateOnly? to)
    {
        var (periodStart, periodEnd) = ResolveRange(from, to);
        _logger.LogInformation("Listing service payments for therapist {TherapistId} {From}..{To}", therapistId, periodStart, periodEnd);

        var payments = await _servicePaymentRepo.GetByTherapistIdAndDateRangeAsync(
            therapistId,
            periodStart.ToDateTime(TimeOnly.MinValue),
            periodEnd.ToDateTime(TimeOnly.MaxValue));

        return payments.Select(MapToRecord);
    }

    public async Task<ServicePaymentRecord?> GetByIdAsync(int servicePaymentId)
    {
        var payment = await _servicePaymentRepo.GetByIdWithDetailsAsync(servicePaymentId);
        return payment == null ? null : MapToRecord(payment);
    }

    public async Task<IEnumerable<UnpaidProviderSessionSummary>> GetUnpaidProviderSessionsAsync(int therapistId, DateOnly? from, DateOnly? to)
    {
        var (periodStart, periodEnd) = ResolveRange(from, to);
        _logger.LogInformation("Listing unpaid provider sessions for therapist {TherapistId} {From}..{To}", therapistId, periodStart, periodEnd);

        var completedId = await ResolveCompletedStatusIdAsync();
        if (completedId == null) return Enumerable.Empty<UnpaidProviderSessionSummary>();

        var sessions = await _sessionRepo.GetByTherapistIdAndDateRangeAsync(
            therapistId, periodStart, periodEnd, new[] { completedId.Value });

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var allocations = await _sessionServicePaymentRepo.GetBySessionIdsAsync(sessionIds);
        var appliedBySession = allocations
            .GroupBy(a => a.TherapySessionId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountApplied));

        var results = new List<UnpaidProviderSessionSummary>();
        foreach (var s in sessions.OrderBy(s => s.SessionDate).ThenBy(s => s.SessionTime))
        {
            var alreadyApplied = appliedBySession.TryGetValue(s.Id, out var sum) ? sum : 0m;
            var remaining = s.ProviderAmount - alreadyApplied;
            if (remaining <= 0) continue;

            results.Add(new UnpaidProviderSessionSummary
            {
                SessionId = s.Id,
                SessionDate = s.SessionDate.ToString("yyyy-MM-dd"),
                SessionTime = s.SessionTime.ToString("HH:mm"),
                PatientId = s.PatientId,
                PatientName = ResolvePatientName(s.Patient),
                TherapyType = ResolveTherapyType(s),
                ProviderAmount = s.ProviderAmount,
                AlreadyApplied = alreadyApplied,
                RemainingProviderAmount = remaining,
            });
        }

        return results;
    }

    public QuincenaWindow GetQuincenaWindow(DateOnly date)
    {
        DateOnly from, to;
        if (date.Day <= 15)
        {
            from = new DateOnly(date.Year, date.Month, 1);
            to = new DateOnly(date.Year, date.Month, 15);
        }
        else
        {
            from = new DateOnly(date.Year, date.Month, 16);
            to = new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        }

        return new QuincenaWindow
        {
            From = from.ToString("yyyy-MM-dd"),
            To = to.ToString("yyyy-MM-dd"),
        };
    }

    private static (DateOnly from, DateOnly to) ResolveRange(DateOnly? from, DateOnly? to)
    {
        var periodStart = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var periodEnd = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (periodEnd < periodStart)
            throw new ArgumentException("Query parameter 'to' must be on or after 'from'.");
        return (periodStart, periodEnd);
    }

    private async Task<int?> ResolveCompletedStatusIdAsync()
    {
        var statuses = await _appointmentStatusRepo.GetAllAsync();
        var completed = statuses.FirstOrDefault(s => string.Equals(s.Name, StatusCompleted, StringComparison.OrdinalIgnoreCase));
        if (completed == null)
            _logger.LogWarning("AppointmentStatus '{Name}' not found in lookup table", StatusCompleted);
        return completed?.Id;
    }

    private static void ValidateRequest(decimal amount, List<SessionServiceAllocationItem> allocations)
    {
        // MVP entry is positive-only. Negative (reversal) entries arrive with WP-14.5.
        if (amount <= 0)
            throw new ArgumentException("Service payment amount must be greater than zero.");

        var totalApplied = allocations.Sum(a => a.AmountApplied);
        if (totalApplied > amount)
            throw new ArgumentException($"Total allocations ({totalApplied:C}) exceed payment amount ({amount:C}).");
    }

    private static ServicePaymentRecord MapToRecord(ServicePayment sp)
    {
        var allocations = sp.SessionServicePayments?.Select(ssp => new ServiceAllocationDetail
        {
            SessionServicePaymentId = ssp.Id,
            SessionId = ssp.TherapySessionId,
            SessionDate = ssp.TherapySession?.SessionDate.ToString("yyyy-MM-dd") ?? string.Empty,
            PatientName = ssp.TherapySession?.Patient != null ? ResolvePatientName(ssp.TherapySession.Patient) : string.Empty,
            AmountApplied = ssp.AmountApplied,
        }).ToList() ?? new List<ServiceAllocationDetail>();

        var totalApplied = allocations.Sum(a => a.AmountApplied);

        return new ServicePaymentRecord
        {
            ServicePaymentId = sp.Id,
            TherapistId = sp.TherapistId,
            TherapistName = ResolveTherapistName(sp.Therapist),
            PaymentDate = sp.PaymentDate,
            Amount = sp.Amount,
            PaymentType = sp.PaymentType != null
                ? new PaymentTypeInfo
                {
                    PaymentTypeId = sp.PaymentType.Id,
                    Abbreviation = sp.PaymentType.Abbreviation,
                    Name = sp.PaymentType.Name,
                }
                : new PaymentTypeInfo(),
            ReferenceNumber = sp.ReferenceNumber,
            Notes = sp.Notes,
            Allocations = allocations,
            TotalApplied = totalApplied,
            UnallocatedAmount = sp.Amount - totalApplied,
        };
    }

    private static string ResolveTherapistName(Therapist? therapist)
    {
        if (therapist?.User == null) return string.Empty;
        return $"{therapist.User.LastName}, {therapist.User.FirstName}".Trim();
    }

    private static string ResolvePatientName(Patient? patient)
    {
        if (patient?.User == null) return string.Empty;
        var middle = string.IsNullOrWhiteSpace(patient.User.MiddleName) ? string.Empty : $" {patient.User.MiddleName}";
        return $"{patient.User.LastName}, {patient.User.FirstName}{middle}".Trim();
    }

    private static string ResolveTherapyType(TherapySession s)
    {
        if (s.SpecialtyType != null && !string.IsNullOrWhiteSpace(s.SpecialtyType.Name))
            return s.SpecialtyType.Name;
        if (!string.IsNullOrWhiteSpace(s.TherapyTypes))
            return s.TherapyTypes;
        return "N/A";
    }
}
