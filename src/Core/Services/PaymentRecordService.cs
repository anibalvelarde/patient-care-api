using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

public class PaymentRecordService : IPaymentRecordService
{
    private readonly IPaymentRepository _paymentRepo;
    private readonly ISessionPaymentRepository _sessionPaymentRepo;
    private readonly ITherapySessionRepository _sessionRepo;
    private readonly IPatientCaretakerRepository _patientCaretakerRepo;
    private readonly IRepository<PaymentType> _paymentTypeRepo;
    private readonly ILogger<PaymentRecordService> _logger;

    public PaymentRecordService(
        ILogger<PaymentRecordService> logger,
        IPaymentRepository paymentRepo,
        ISessionPaymentRepository sessionPaymentRepo,
        ITherapySessionRepository sessionRepo,
        IPatientCaretakerRepository patientCaretakerRepo,
        IRepository<PaymentType> paymentTypeRepo)
    {
        _logger = logger;
        _paymentRepo = paymentRepo;
        _sessionPaymentRepo = sessionPaymentRepo;
        _sessionRepo = sessionRepo;
        _patientCaretakerRepo = patientCaretakerRepo;
        _paymentTypeRepo = paymentTypeRepo;
    }

    public async Task<IEnumerable<PaymentTypeInfo>> GetPaymentTypesAsync()
    {
        _logger.LogInformation("Getting all payment types");
        var types = await _paymentTypeRepo.GetAllAsync();
        return types.Select(pt => new PaymentTypeInfo
        {
            PaymentTypeId = pt.Id,
            Abbreviation = pt.Abbreviation,
            Name = pt.Name,
        });
    }

    public async Task<IEnumerable<PaymentRecord>> GetAllAsync()
    {
        _logger.LogInformation("Getting all payments");
        var payments = await _paymentRepo.GetAllWithDetailsAsync();
        return payments.Select(MapToPaymentRecord);
    }

    public async Task<IEnumerable<PaymentRecord>> GetByCaretakerAsync(int caretakerId)
    {
        _logger.LogInformation("Getting payments for caretaker {CaretakerId}", caretakerId);
        var payments = await _paymentRepo.GetByCaretakerIdAsync(caretakerId);
        return payments.Select(MapToPaymentRecord);
    }

    public async Task<PaymentRecord?> GetByIdAsync(int paymentId)
    {
        _logger.LogInformation("Getting payment {PaymentId}", paymentId);
        var payment = await _paymentRepo.GetByIdWithDetailsAsync(paymentId);
        return payment == null ? null : MapToPaymentRecord(payment);
    }

    public async Task<PaymentRecord> CreateAsync(PaymentRecordRequest request)
    {
        _logger.LogInformation("Creating payment for caretaker {CaretakerId}, amount {Amount}", request.CaretakerId, request.Amount);

        ValidateRequest(request.Amount, request.SessionAllocations);

        // Validate per-session allocation limits
        foreach (var alloc in request.SessionAllocations)
        {
            var session = await _sessionRepo.GetByIdAsync(alloc.SessionId);
            if (session == null)
                throw new ArgumentException($"Session {alloc.SessionId} not found.");
            if (alloc.AmountAllocated > session.AmountDue())
                throw new ArgumentException($"Allocation {alloc.AmountAllocated:C} exceeds amount due {session.AmountDue():C} for session {alloc.SessionId}.");
        }

        var payment = new Payment
        {
            CaretakerId = request.CaretakerId,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            PaymentTypeId = request.PaymentTypeId,
            CheckNumber = request.CheckNumber
        };

        var created = await _paymentRepo.AddAsync(payment);

        foreach (var alloc in request.SessionAllocations)
        {
            await _sessionPaymentRepo.AddAsync(new SessionPayment
            {
                PaymentId = created.Id,
                TherapySessionId = alloc.SessionId,
                AmountAllocated = alloc.AmountAllocated
            });

            await UpdateSessionAmountPaid(alloc.SessionId);
        }

        var result = await _paymentRepo.GetByIdWithDetailsAsync(created.Id);
        return MapToPaymentRecord(result!);
    }

    public async Task UpdateAsync(int paymentId, PaymentRecordUpdateRequest request)
    {
        _logger.LogInformation("Updating payment {PaymentId}", paymentId);

        ValidateRequest(request.Amount, request.SessionAllocations);

        var existing = await _paymentRepo.GetByIdWithDetailsAsync(paymentId);
        if (existing == null)
            throw new ArgumentException($"Payment {paymentId} not found.");

        // Collect affected session IDs before deleting old allocations
        var oldSessionIds = existing.SessionPayments?.Select(sp => sp.TherapySessionId).ToList() ?? new List<int>();

        // Delete old allocations
        await _sessionPaymentRepo.DeleteByPaymentIdAsync(paymentId);

        // Update payment
        existing.Amount = request.Amount;
        existing.PaymentDate = request.PaymentDate;
        existing.CaretakerId = request.CaretakerId;
        existing.PaymentTypeId = request.PaymentTypeId;
        existing.CheckNumber = request.CheckNumber;
        await _paymentRepo.UpdateAsync(existing);

        // Create new allocations
        foreach (var alloc in request.SessionAllocations)
        {
            var session = await _sessionRepo.GetByIdAsync(alloc.SessionId);
            if (session == null)
                throw new ArgumentException($"Session {alloc.SessionId} not found.");

            await _sessionPaymentRepo.AddAsync(new SessionPayment
            {
                PaymentId = paymentId,
                TherapySessionId = alloc.SessionId,
                AmountAllocated = alloc.AmountAllocated
            });
        }

        // Recalculate all affected sessions (old + new)
        var newSessionIds = request.SessionAllocations.Select(a => a.SessionId);
        var allAffectedSessionIds = oldSessionIds.Union(newSessionIds).Distinct();
        foreach (var sessionId in allAffectedSessionIds)
        {
            await UpdateSessionAmountPaid(sessionId);
        }
    }

    public async Task<IEnumerable<UnpaidSessionSummary>> GetUnpaidSessionsForCaretakerAsync(int caretakerId)
    {
        _logger.LogInformation("Getting unpaid sessions for caretaker {CaretakerId}", caretakerId);

        var links = await _patientCaretakerRepo.GetByCaretakerIdAsync(caretakerId);
        var patientIds = links.Select(pc => pc.PatientId).Distinct().ToList();

        var unpaidSessions = await _sessionRepo.GetUnpaidByPatientIdsAsync(patientIds);

        return unpaidSessions.Select(s => new UnpaidSessionSummary
        {
            SessionId = s.Id,
            SessionDate = s.SessionDate.ToString("yyyy-MM-dd"),
            SessionTime = s.SessionTime.ToString("HH:mm:ss"),
            PatientId = s.PatientId,
            PatientName = s.Patient?.User != null
                ? $"{s.Patient.User.LastName}, {s.Patient.User.FirstName} {s.Patient.User.MiddleName}".Trim()
                : string.Empty,
            TherapistName = s.Therapist?.User != null
                ? $"{s.Therapist.User.LastName}, {s.Therapist.User.FirstName}".Trim()
                : string.Empty,
            Amount = s.Amount,
            DiscountAmount = s.DiscountAmount,
            AmountPaid = s.AmountPaid,
            AmountDue = s.AmountDue(),
            IsPastDue = s.GetPastDue()
        });
    }

    public async Task<IEnumerable<SessionPaymentDetail>> GetPaymentsForSessionAsync(int sessionId)
    {
        _logger.LogInformation("Getting payments for session {SessionId}", sessionId);
        var sessionPayments = await _sessionPaymentRepo.GetBySessionIdWithDetailsAsync(sessionId);

        return sessionPayments.Select(sp => new SessionPaymentDetail
        {
            SessionPaymentId = sp.Id,
            PaymentId = sp.PaymentId,
            AmountAllocated = sp.AmountAllocated,
            PaymentDate = sp.Payment.PaymentDate,
            PaymentTotalAmount = sp.Payment.Amount,
            CaretakerId = sp.Payment.CaretakerId,
            CaretakerName = sp.Payment.Caretaker?.User != null
                ? $"{sp.Payment.Caretaker.User.LastName}, {sp.Payment.Caretaker.User.FirstName} {sp.Payment.Caretaker.User.MiddleName}".Trim()
                : string.Empty,
            PaymentType = sp.Payment.PaymentType != null
                ? new PaymentTypeInfo
                {
                    PaymentTypeId = sp.Payment.PaymentType.Id,
                    Abbreviation = sp.Payment.PaymentType.Abbreviation,
                    Name = sp.Payment.PaymentType.Name,
                }
                : new PaymentTypeInfo(),
            CheckNumber = sp.Payment.CheckNumber
        });
    }

    private static void ValidateRequest(decimal amount, List<SessionAllocationItem> allocations)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be greater than zero.");

        var totalAllocated = allocations.Sum(a => a.AmountAllocated);
        if (totalAllocated > amount)
            throw new ArgumentException($"Total allocations ({totalAllocated:C}) exceed payment amount ({amount:C}).");
    }

    private async Task UpdateSessionAmountPaid(int sessionId)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId);
        if (session == null) return;

        var allocations = await _sessionPaymentRepo.GetByPaymentIdAsync(session.Id);
        // We need all allocations for this SESSION, not for a specific payment
        // So we need to query differently — get all SessionPayments for this session
        var allSessionAllocations = (await _sessionPaymentRepo.GetAllAsync())
            .Where(sp => sp.TherapySessionId == sessionId);

        session.AmountPaid = allSessionAllocations.Sum(sp => sp.AmountAllocated);
        session.IsPaidOff = session.AmountDue() <= 0;
        await _sessionRepo.UpdateAsync(session);
    }

    private static PaymentRecord MapToPaymentRecord(Payment payment)
    {
        var caretakerName = payment.Caretaker?.User != null
            ? $"{payment.Caretaker.User.LastName}, {payment.Caretaker.User.FirstName} {payment.Caretaker.User.MiddleName}".Trim()
            : string.Empty;

        var allocations = payment.SessionPayments?.Select(sp => new AllocationDetail
        {
            SessionPaymentId = sp.Id,
            SessionId = sp.TherapySessionId,
            SessionDate = sp.TherapySession?.SessionDate.ToString("yyyy-MM-dd") ?? string.Empty,
            PatientName = sp.TherapySession?.Patient?.User != null
                ? $"{sp.TherapySession.Patient.User.LastName}, {sp.TherapySession.Patient.User.FirstName} {sp.TherapySession.Patient.User.MiddleName}".Trim()
                : string.Empty,
            TherapistName = sp.TherapySession?.Therapist?.User != null
                ? $"{sp.TherapySession.Therapist.User.LastName}, {sp.TherapySession.Therapist.User.FirstName}".Trim()
                : string.Empty,
            AmountAllocated = sp.AmountAllocated
        }).ToList() ?? new List<AllocationDetail>();

        var totalAllocated = allocations.Sum(a => a.AmountAllocated);

        return new PaymentRecord
        {
            PaymentId = payment.Id,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            CaretakerId = payment.CaretakerId,
            CaretakerName = caretakerName,
            PaymentType = payment.PaymentType != null
                ? new PaymentTypeInfo
                {
                    PaymentTypeId = payment.PaymentType.Id,
                    Abbreviation = payment.PaymentType.Abbreviation,
                    Name = payment.PaymentType.Name,
                }
                : new PaymentTypeInfo(),
            CheckNumber = payment.CheckNumber,
            Allocations = allocations,
            TotalAllocated = totalAllocated,
            UnallocatedAmount = payment.Amount - totalAllocated
        };
    }
}
