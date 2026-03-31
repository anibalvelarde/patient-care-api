using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Statements;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class AccountStatementService : IAccountStatementService
{
    private readonly ICaretakerProfileService _caretakerProfileService;
    private readonly ITherapySessionRepository _sessionRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IPatientCaretakerRepository _patientCaretakerRepo;
    private readonly ILogger<AccountStatementService> _logger;

    public AccountStatementService(
        ILogger<AccountStatementService> logger,
        ICaretakerProfileService caretakerProfileService,
        ITherapySessionRepository sessionRepo,
        IPaymentRepository paymentRepo,
        IPatientCaretakerRepository patientCaretakerRepo)
    {
        _logger = logger;
        _caretakerProfileService = caretakerProfileService;
        _sessionRepo = sessionRepo;
        _paymentRepo = paymentRepo;
        _patientCaretakerRepo = patientCaretakerRepo;
    }

    public async Task<AccountStatement?> GetStatementAsync(int caretakerId, DateOnly? from, DateOnly? to)
    {
        _logger.LogInformation("Generating account statement for caretaker {CaretakerId}", caretakerId);

        // 1. Get caretaker profile
        var caretaker = await _caretakerProfileService.GetByIdAsync(caretakerId);
        if (caretaker == null)
        {
            _logger.LogWarning("Caretaker {CaretakerId} not found", caretakerId);
            return null;
        }

        // 2. Default date range: last 90 days
        var periodStart = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));
        var periodEnd = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // 3. Get patients linked to caretaker
        var patients = caretaker.Patients.Select(p => new StatementPatient
        {
            PatientId = p.PatientId,
            PatientName = p.PatientName,
            IsPrimaryCaretaker = p.IsPrimaryCaretaker,
            RelationshipToPatient = p.RelationshipToPatient
        }).ToList();

        // 4. Get patient IDs
        var patientIds = patients.Select(p => p.PatientId).ToList();

        // 5. Get all sessions in date range
        var sessions = await _sessionRepo.GetByPatientIdsAndDateRangeAsync(patientIds, periodStart, periodEnd);

        // 6. Map sessions to charges
        var charges = sessions.Select(s => new StatementCharge
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate.ToString("yyyy-MM-dd"),
            PatientName = s.Patient?.User != null
                ? $"{s.Patient.User.LastName}, {s.Patient.User.FirstName} {s.Patient.User.MiddleName}".Trim()
                : string.Empty,
            TherapistName = s.Therapist?.User != null
                ? $"{s.Therapist.User.LastName}, {s.Therapist.User.FirstName}".Trim()
                : string.Empty,
            TherapyType = s.TherapyTypes ?? "N/A",
            Amount = s.Amount,
            DiscountAmount = s.DiscountAmount,
            NetCharge = s.Amount - s.DiscountAmount,
            AmountPaid = s.AmountPaid,
            AmountDue = s.AmountDue(),
            IsPastDue = s.GetPastDue(),
            PaymentsApplied = s.SessionPayments?.Select(sp => new ChargePaymentApplied
            {
                PaymentId = sp.PaymentId,
                PaymentDate = sp.Payment.PaymentDate,
                AmountAllocated = sp.AmountAllocated,
                PaymentType = sp.Payment.PaymentType?.Name ?? string.Empty,
                CheckNumber = sp.Payment.CheckNumber
            }).ToList() ?? new List<ChargePaymentApplied>()
        }).ToList();

        // 7. Get payments in date range
        var paymentEntities = await _paymentRepo.GetByCaretakerIdAndDateRangeAsync(
            caretakerId,
            periodStart.ToDateTime(TimeOnly.MinValue),
            periodEnd.ToDateTime(TimeOnly.MaxValue));

        // 8. Map payments
        var payments = paymentEntities.Select(p => new StatementPayment
        {
            PaymentId = p.Id,
            PaymentDate = p.PaymentDate,
            Amount = p.Amount,
            PaymentType = p.PaymentType?.Name ?? string.Empty,
            CheckNumber = p.CheckNumber,
            Allocations = p.SessionPayments?.Select(sp => new StatementPaymentAllocation
            {
                SessionId = sp.TherapySession.Id,
                SessionDate = sp.TherapySession.SessionDate.ToString("yyyy-MM-dd"),
                PatientName = sp.TherapySession.Patient?.User != null
                    ? $"{sp.TherapySession.Patient.User.LastName}, {sp.TherapySession.Patient.User.FirstName} {sp.TherapySession.Patient.User.MiddleName}".Trim()
                    : string.Empty,
                AmountAllocated = sp.AmountAllocated
            }).ToList() ?? new List<StatementPaymentAllocation>()
        }).ToList();

        // 9. Compute summary
        // OutstandingBalance uses actual unpaid amounts from charges (not period-scoped
        // payment totals) because charges in-range may be paid by out-of-range payments.
        var summary = new StatementSummary
        {
            TotalCharges = charges.Sum(c => c.Amount),
            TotalDiscounts = charges.Sum(c => c.DiscountAmount),
            TotalNetCharges = charges.Sum(c => c.NetCharge),
            TotalPayments = charges.Sum(c => c.AmountPaid),
            OutstandingBalance = charges.Sum(c => c.AmountDue),
            PastDueAmount = charges.Where(c => c.IsPastDue).Sum(c => c.AmountDue),
            PastDueSessionCount = charges.Count(c => c.IsPastDue)
        };

        return new AccountStatement
        {
            CaretakerId = caretaker.CaretakerId,
            CaretakerName = caretaker.CaretakerName,
            StatementDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            PeriodStart = periodStart.ToString("yyyy-MM-dd"),
            PeriodEnd = periodEnd.ToString("yyyy-MM-dd"),
            Patients = patients,
            Charges = charges,
            Payments = payments,
            Summary = summary
        };
    }
}
