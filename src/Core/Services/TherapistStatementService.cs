using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Statements;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class TherapistStatementService : ITherapistStatementService
{
    private const string StatusCompleted = "Completed";
    private const string StatusCancelled = "Cancelled";
    private const string StatusNoShow = "NoShow";

    private readonly ITherapistProfileService _therapistProfileService;
    private readonly ITherapySessionRepository _sessionRepo;
    private readonly IRepository<AppointmentStatus> _appointmentStatusRepo;
    private readonly ISessionServicePaymentRepository _sessionServicePaymentRepo;
    private readonly IServicePaymentRepository _servicePaymentRepo;
    private readonly ILogger<TherapistStatementService> _logger;

    public TherapistStatementService(
        ILogger<TherapistStatementService> logger,
        ITherapistProfileService therapistProfileService,
        ITherapySessionRepository sessionRepo,
        IRepository<AppointmentStatus> appointmentStatusRepo,
        ISessionServicePaymentRepository sessionServicePaymentRepo,
        IServicePaymentRepository servicePaymentRepo)
    {
        _logger = logger;
        _therapistProfileService = therapistProfileService;
        _sessionRepo = sessionRepo;
        _appointmentStatusRepo = appointmentStatusRepo;
        _sessionServicePaymentRepo = sessionServicePaymentRepo;
        _servicePaymentRepo = servicePaymentRepo;
    }

    public async Task<TherapistStatement?> GetStatementAsync(int therapistId, DateOnly? from, DateOnly? to)
    {
        _logger.LogInformation("Generating therapist statement for therapist {TherapistId}", therapistId);

        // 1. Verify therapist exists
        var therapist = await _therapistProfileService.GetByIdAsync(therapistId);
        if (therapist == null)
        {
            _logger.LogWarning("Therapist {TherapistId} not found", therapistId);
            return null;
        }

        // 2. Default date range: last 90 days
        var periodStart = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var periodEnd = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (periodEnd < periodStart)
        {
            throw new ArgumentException("Query parameter 'to' must be on or after 'from'.");
        }

        // 3. Resolve appointment-status IDs by name (configurable lookup, never hardcoded)
        var allStatuses = await _appointmentStatusRepo.GetAllAsync();
        var statusByName = allStatuses
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var includedStatusIds = new List<int>();
        int? completedId = null;
        foreach (var name in new[] { StatusCompleted, StatusCancelled, StatusNoShow })
        {
            if (statusByName.TryGetValue(name, out var status))
            {
                includedStatusIds.Add(status.Id);
                if (string.Equals(name, StatusCompleted, StringComparison.OrdinalIgnoreCase))
                {
                    completedId = status.Id;
                }
            }
            else
            {
                _logger.LogWarning("AppointmentStatus '{Name}' not found in lookup table", name);
            }
        }

        // 4. Pull sessions for therapist within range, restricted to the three statuses
        var sessions = await _sessionRepo.GetByTherapistIdAndDateRangeAsync(
            therapistId, periodStart, periodEnd, includedStatusIds);

        // 5. Group by patient → emit blocks, sorted by patient name
        var patientBlocks = sessions
            .GroupBy(s => s.PatientId)
            .Select(g =>
            {
                var first = g.First();
                var patientName = ResolvePatientName(first.Patient);

                var sessionRows = g
                    .OrderBy(s => s.SessionDate)
                    .ThenBy(s => s.SessionTime)
                    .Select(s => MapSession(s, completedId))
                    .ToList();

                var subtotalFee = sessionRows.Sum(r => r.Amount);
                var subtotalDiscount = sessionRows.Sum(r => r.DiscountAmount);
                var subtotalProvider = sessionRows.Where(r => r.IsBillable).Sum(r => r.ProviderAmount);
                var completedCount = sessionRows.Count(r => r.IsBillable);
                var nonBillableCount = sessionRows.Count - completedCount;

                return new TherapistStatementPatient
                {
                    PatientId = g.Key,
                    PatientName = patientName,
                    Sessions = sessionRows,
                    SubtotalFee = subtotalFee,
                    SubtotalDiscount = subtotalDiscount,
                    SubtotalProviderAmount = subtotalProvider,
                    CompletedCount = completedCount,
                    NonBillableCount = nonBillableCount,
                };
            })
            .OrderBy(b => b.PatientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 6. Service payments (WP-14): join the payroll allocations recorded against any session
        //    in this statement's range. The statement is authoritative (not pro-forma) once any
        //    payment touches the period; until then it remains an estimate.
        var inRangeSessionIds = sessions.Select(s => s.Id).ToList();
        var inRangeSessionIdSet = new HashSet<int>(inRangeSessionIds);
        var allocations = await _sessionServicePaymentRepo.GetBySessionIdsAsync(inRangeSessionIds);
        var totalServicePaymentsApplied = allocations.Sum(a => a.AmountApplied);

        var servicePaymentIds = allocations.Select(a => a.ServicePaymentId).Distinct().ToList();
        var servicePayments = await _servicePaymentRepo.GetByIdsWithDetailsAsync(servicePaymentIds);
        var servicePaymentItems = servicePayments
            .OrderByDescending(p => p.PaymentDate)
            .Select(p => MapServicePaymentItem(p, inRangeSessionIdSet))
            .ToList();

        var isProForma = allocations.Count == 0;

        // 7. Compute summary
        var totalCompleted = patientBlocks.Sum(p => p.CompletedCount);
        var totalNonBillable = patientBlocks.Sum(p => p.NonBillableCount);
        var totalFee = patientBlocks.Sum(p => p.SubtotalFee);
        var totalDiscount = patientBlocks.Sum(p => p.SubtotalDiscount);
        var totalProvider = patientBlocks.Sum(p => p.SubtotalProviderAmount);
        var estimatedAmountDue = totalProvider - totalServicePaymentsApplied;

        var summary = new TherapistStatementSummary
        {
            TotalCompletedSessions = totalCompleted,
            TotalNonBillableSessions = totalNonBillable,
            TotalFee = totalFee,
            TotalDiscount = totalDiscount,
            TotalProviderAmount = totalProvider,
            TotalServicePaymentsApplied = totalServicePaymentsApplied,
            EstimatedAmountDue = estimatedAmountDue,
        };

        return new TherapistStatement
        {
            TherapistId = therapist.TherapistId,
            TherapistName = therapist.TherapistName,
            StatementDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            PeriodStart = periodStart.ToString("yyyy-MM-dd"),
            PeriodEnd = periodEnd.ToString("yyyy-MM-dd"),
            IsProForma = isProForma,
            Patients = patientBlocks,
            ServicePayments = servicePaymentItems,
            Summary = summary,
        };
    }

    private static TherapistStatementSession MapSession(TherapySession s, int? completedStatusId)
    {
        var statusName = s.AppointmentStatus?.Name ?? string.Empty;
        var isBillable = completedStatusId.HasValue && s.AppointmentStatusId == completedStatusId.Value;

        return new TherapistStatementSession
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate.ToString("yyyy-MM-dd"),
            SessionTime = s.SessionTime.ToString("HH:mm"),
            TherapyType = ResolveTherapyType(s),
            Amount = s.Amount,
            DiscountAmount = s.DiscountAmount,
            ProviderAmount = isBillable ? s.ProviderAmount : 0m,
            StatusName = statusName,
            IsBillable = isBillable,
        };
    }

    /// <summary>
    /// Therapy Type fallback chain (per WP-13B):
    ///   1. SpecialtyType.Name (FK-resolved)
    ///   2. TherapySession.TherapyTypes (legacy text column)
    ///   3. "N/A"
    /// </summary>
    private static string ResolveTherapyType(TherapySession s)
    {
        if (s.SpecialtyType != null && !string.IsNullOrWhiteSpace(s.SpecialtyType.Name))
        {
            return s.SpecialtyType.Name;
        }
        if (!string.IsNullOrWhiteSpace(s.TherapyTypes))
        {
            return s.TherapyTypes;
        }
        return "N/A";
    }

    private static string ResolvePatientName(Patient? patient)
    {
        if (patient?.User == null) return string.Empty;
        var middle = string.IsNullOrWhiteSpace(patient.User.MiddleName) ? string.Empty : $" {patient.User.MiddleName}";
        return $"{patient.User.LastName}, {patient.User.FirstName}{middle}".Trim();
    }

    /// <summary>
    /// Maps a ServicePayment to a statement line. ALL of the payment's allocations are listed
    /// (per WP-14 open-Q 9) even when some fall outside this statement's period; a note is added
    /// when that happens so the reader understands only a subset of allocations is in-range.
    /// </summary>
    private static ServicePaymentItem MapServicePaymentItem(ServicePayment p, HashSet<int> inRangeSessionIds)
    {
        var allocations = p.SessionServicePayments?
            .Select(ssp => new ServicePaymentAllocation
            {
                SessionId = ssp.TherapySessionId,
                AmountApplied = ssp.AmountApplied,
            })
            .ToList() ?? new List<ServicePaymentAllocation>();

        var coversOutOfRange = allocations.Any(a => !inRangeSessionIds.Contains(a.SessionId));
        var notes = p.Notes ?? string.Empty;
        if (coversOutOfRange)
        {
            notes = string.IsNullOrWhiteSpace(notes)
                ? "Covers sessions outside this period."
                : $"{notes} (Covers sessions outside this period.)";
        }

        return new ServicePaymentItem
        {
            ServicePaymentId = p.Id,
            PaymentDate = p.PaymentDate.ToString("yyyy-MM-dd"),
            Amount = p.Amount,
            PaymentTypeName = p.PaymentType?.Name ?? string.Empty,
            ReferenceNumber = p.ReferenceNumber ?? string.Empty,
            Notes = notes,
            Allocations = allocations,
        };
    }
}
