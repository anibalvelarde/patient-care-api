using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
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
    private readonly ITherapistProfileService _therapistProfileService;
    private readonly ILogger<ServicePaymentService> _logger;

    public ServicePaymentService(
        ILogger<ServicePaymentService> logger,
        IServicePaymentRepository servicePaymentRepo,
        ISessionServicePaymentRepository sessionServicePaymentRepo,
        ITherapySessionRepository sessionRepo,
        IRepository<AppointmentStatus> appointmentStatusRepo,
        ITherapistProfileService therapistProfileService)
    {
        _logger = logger;
        _servicePaymentRepo = servicePaymentRepo;
        _sessionServicePaymentRepo = sessionServicePaymentRepo;
        _sessionRepo = sessionRepo;
        _appointmentStatusRepo = appointmentStatusRepo;
        _therapistProfileService = therapistProfileService;
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

    public async Task<ServicePaymentRecord> ReverseAsync(int servicePaymentId, ReverseServicePaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A reason is required to reverse a service payment.");

        _logger.LogInformation("Reversing service payment {ServicePaymentId}", servicePaymentId);

        var original = await _servicePaymentRepo.GetByIdWithDetailsAsync(servicePaymentId)
            ?? throw new NotFoundException("ServicePayment", servicePaymentId);

        // Append-only: only an original disbursement can be reversed — never a reversal entry itself.
        if (original.ReversesServicePaymentId != null || original.Amount < 0)
            throw new ArgumentException("This payment is itself a reversal and cannot be reversed.");

        if (await _servicePaymentRepo.IsReversedAsync(servicePaymentId))
            throw new ArgumentException("This payment has already been reversed.");

        var reversal = new ServicePayment
        {
            TherapistId = original.TherapistId,
            Amount = -original.Amount,
            PaymentDate = DateTime.UtcNow,
            PaymentTypeId = original.PaymentTypeId,
            ReferenceNumber = original.ReferenceNumber,
            Notes = $"Reversal of payment #{original.Id}: {request.Reason.Trim()}",
            ReversesServicePaymentId = original.Id,
        };

        var created = await _servicePaymentRepo.AddAsync(reversal);

        foreach (var alloc in original.SessionServicePayments)
        {
            await _sessionServicePaymentRepo.AddAsync(new SessionServicePayment
            {
                ServicePaymentId = created.Id,
                TherapySessionId = alloc.TherapySessionId,
                AmountApplied = -alloc.AmountApplied,
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

        var reversedIds = await _servicePaymentRepo.GetReversedOriginalIdsAsync(payments.Select(p => p.Id));
        return payments.Select(p => MapToRecord(p, reversedIds.Contains(p.Id)));
    }

    public async Task<ServicePaymentRecord?> GetByIdAsync(int servicePaymentId)
    {
        var payment = await _servicePaymentRepo.GetByIdWithDetailsAsync(servicePaymentId);
        if (payment == null) return null;
        return MapToRecord(payment, await _servicePaymentRepo.IsReversedAsync(servicePaymentId));
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

    public async Task<IEnumerable<PayrollPreviewTherapist>> GetPayrollPreviewAsync(DateOnly? from, DateOnly? to)
    {
        var (periodStart, periodEnd) = ResolveRange(from, to);
        _logger.LogInformation("Building payroll preview for {From}..{To}", periodStart, periodEnd);

        var therapists = await _therapistProfileService.GetAllAsync();

        var rows = new List<PayrollPreviewTherapist>();
        foreach (var t in therapists.Where(t => t.IsActive))
        {
            var unpaid = (await GetUnpaidProviderSessionsAsync(t.TherapistId, periodStart, periodEnd)).ToList();
            if (unpaid.Count == 0) continue;

            rows.Add(new PayrollPreviewTherapist
            {
                TherapistId = t.TherapistId,
                TherapistName = t.TherapistName,
                SessionCount = unpaid.Count,
                TotalRemaining = unpaid.Sum(s => s.RemainingProviderAmount),
                Sessions = unpaid,
            });
        }

        return rows.OrderBy(r => r.TherapistName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PendingPayrollSummary> GetPendingPayrollSummaryAsync(DateOnly? from, DateOnly? to)
    {
        // All-time by default: this is an outstanding-liability figure, so (unlike the per-therapist
        // views, which default to a 90-day window) we must not hide older unpaid sessions. The
        // sentinel lower bound stays above MySQL's DATE floor (1000-01-01).
        var periodStart = from ?? new DateOnly(1900, 1, 1);
        var periodEnd = to ?? new DateOnly(9999, 12, 31);
        if (periodEnd < periodStart)
            throw new ArgumentException("Query parameter 'to' must be on or after 'from'.");

        _logger.LogInformation("Building pending payroll summary for {From}..{To}", periodStart, periodEnd);

        var therapists = await _therapistProfileService.GetAllAsync();

        var summary = new PendingPayrollSummary();
        foreach (var t in therapists.Where(t => t.IsActive))
        {
            var unpaid = (await GetUnpaidProviderSessionsAsync(t.TherapistId, periodStart, periodEnd)).ToList();
            if (unpaid.Count == 0) continue;

            summary.TherapistCount++;
            summary.SessionCount += unpaid.Count;
            summary.TotalPending += unpaid.Sum(s => s.RemainingProviderAmount);
        }

        return summary;
    }

    public async Task<PendingPayReport> GetPendingPayReportAsync(DateOnly? from, DateOnly? to)
    {
        // Mirrors GetPendingPayrollSummaryAsync (all-time default; sentinel above MySQL's DATE floor),
        // but builds a per-therapist breakdown straight off the session rows so we can surface gross
        // billed, the date span, and the distinct-patient count alongside the owed amount.
        var periodStart = from ?? new DateOnly(1900, 1, 1);
        var periodEnd = to ?? new DateOnly(9999, 12, 31);
        if (periodEnd < periodStart)
            throw new ArgumentException("Query parameter 'to' must be on or after 'from'.");

        _logger.LogInformation("Building pending-pay report for {From}..{To}", periodStart, periodEnd);

        var report = new PendingPayReport();
        var completedId = await ResolveCompletedStatusIdAsync();
        if (completedId == null) return report;

        var therapists = await _therapistProfileService.GetAllAsync();
        foreach (var t in therapists.Where(t => t.IsActive))
        {
            var sessions = await _sessionRepo.GetByTherapistIdAndDateRangeAsync(
                t.TherapistId, periodStart, periodEnd, new[] { completedId.Value });
            if (sessions.Count == 0) continue;

            var allocations = await _sessionServicePaymentRepo.GetBySessionIdsAsync(sessions.Select(s => s.Id).ToList());
            var appliedBySession = allocations
                .GroupBy(a => a.TherapySessionId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.AmountApplied));

            // Keep only the sessions that still owe the therapist — the same population as the tile.
            var owing = sessions
                .Select(s => new
                {
                    Session = s,
                    Remaining = s.ProviderAmount - (appliedBySession.TryGetValue(s.Id, out var sum) ? sum : 0m),
                })
                .Where(x => x.Remaining > 0)
                .ToList();
            if (owing.Count == 0) continue;

            var grossBilled = owing.Sum(x => x.Session.Amount);
            var amountOwed = owing.Sum(x => x.Remaining);

            report.Rows.Add(new PendingPayReportRow
            {
                TherapistId = t.TherapistId,
                TherapistName = t.TherapistName,
                SessionCount = owing.Count,
                FirstSessionDate = owing.Min(x => x.Session.SessionDate).ToString("yyyy-MM-dd"),
                LastSessionDate = owing.Max(x => x.Session.SessionDate).ToString("yyyy-MM-dd"),
                DistinctPatientCount = owing.Select(x => x.Session.PatientId).Distinct().Count(),
                GrossBilled = grossBilled,
                AmountOwed = amountOwed,
                OwedPctOfGross = grossBilled > 0 ? Math.Round(amountOwed / grossBilled * 100m, 1) : 0m,
            });
        }

        report.Rows = report.Rows.OrderBy(r => r.TherapistName, StringComparer.OrdinalIgnoreCase).ToList();
        report.TherapistCount = report.Rows.Count;
        report.SessionCount = report.Rows.Sum(r => r.SessionCount);
        report.TotalGrossBilled = report.Rows.Sum(r => r.GrossBilled);
        report.TotalOwed = report.Rows.Sum(r => r.AmountOwed);
        report.OwedPctOfGross = report.TotalGrossBilled > 0
            ? Math.Round(report.TotalOwed / report.TotalGrossBilled * 100m, 1)
            : 0m;
        return report;
    }

    public async Task<BatchPayrollResult> RunBatchPayrollAsync(BatchPayrollRequest request)
    {
        if (request.TherapistIds == null || request.TherapistIds.Count == 0)
            throw new ArgumentException("At least one therapist must be selected for the payroll run.");

        var (periodStart, periodEnd) = ResolveRange(request.From, request.To);
        _logger.LogInformation("Running batch payroll for {Count} therapist(s) {From}..{To}",
            request.TherapistIds.Count, periodStart, periodEnd);

        var created = new List<ServicePaymentRecord>();
        foreach (var therapistId in request.TherapistIds.Distinct())
        {
            var unpaid = (await GetUnpaidProviderSessionsAsync(therapistId, periodStart, periodEnd)).ToList();
            if (unpaid.Count == 0) continue; // nothing owed at run time — skip silently

            var record = await CreateAsync(new ServicePaymentRequest
            {
                TherapistId = therapistId,
                PaymentDate = request.PaymentDate,
                Amount = unpaid.Sum(s => s.RemainingProviderAmount),
                PaymentTypeId = request.PaymentTypeId,
                ReferenceNumber = request.ReferenceNumber,
                Notes = request.Notes,
                SessionAllocations = unpaid
                    .Select(s => new SessionServiceAllocationItem { SessionId = s.SessionId, AmountApplied = s.RemainingProviderAmount })
                    .ToList(),
            });
            created.Add(record);
        }

        return new BatchPayrollResult
        {
            Payments = created,
            TherapistCount = created.Count,
            TotalPaid = created.Sum(r => r.Amount),
        };
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

    private static ServicePaymentRecord MapToRecord(ServicePayment sp, bool isReversed = false)
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
            ReversesServicePaymentId = sp.ReversesServicePaymentId,
            IsReversed = isReversed,
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
