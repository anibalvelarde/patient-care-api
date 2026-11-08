using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
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
    private readonly ICaretakerRepository _caretakerRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PaymentRecordService> _logger;

    public PaymentRecordService(
        ILogger<PaymentRecordService> logger,
        IPaymentRepository paymentRepo,
        ISessionPaymentRepository sessionPaymentRepo,
        ITherapySessionRepository sessionRepo,
        IPatientCaretakerRepository patientCaretakerRepo,
        IRepository<PaymentType> paymentTypeRepo,
        ICaretakerRepository caretakerRepo,
        IUnitOfWork unitOfWork)
    {
        _caretakerRepo = caretakerRepo;
        _unitOfWork = unitOfWork;
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

        // WP-58 (F-1): one transaction; every check runs before the first write, so a bad request
        // changes nothing. EfUnitOfWork clears the change tracker if anything throws.
        var paymentId = await _unitOfWork.ExecuteAsync(async () =>
        {
            await ValidateAsync(request.CaretakerId, request.PaymentTypeId, request.Amount,
                request.SessionAllocations, creditBack: new Dictionary<int, decimal>());

            var created = await _paymentRepo.AddAsync(new Payment
            {
                CaretakerId = request.CaretakerId,
                Amount = request.Amount,
                PaymentDate = request.PaymentDate,
                PaymentTypeId = request.PaymentTypeId,
                CheckNumber = request.CheckNumber
            });

            foreach (var alloc in request.SessionAllocations)
            {
                await _sessionPaymentRepo.AddAsync(new SessionPayment
                {
                    PaymentId = created.Id,
                    TherapySessionId = alloc.SessionId,
                    AmountAllocated = alloc.AmountAllocated
                });
            }

            foreach (var sessionId in request.SessionAllocations.Select(a => a.SessionId).Distinct())
            {
                await RecomputeAmountPaidAsync(sessionId);
            }

            return created.Id;
        });

        var result = await _paymentRepo.GetByIdWithDetailsAsync(paymentId);
        return MapToPaymentRecord(result!);
    }

    public async Task UpdateAsync(int paymentId, PaymentRecordUpdateRequest request)
    {
        _logger.LogInformation("Updating payment {PaymentId}", paymentId);

        // WP-58 (F-1): before WP-58 the old allocations were deleted (and saved) FIRST, so any later
        // failure left the payment with no allocations while the sessions kept their AmountPaid.
        // Now: validate everything, then do every write inside one transaction.
        await _unitOfWork.ExecuteAsync(async () =>
        {
            var existing = await _paymentRepo.GetByIdWithDetailsAsync(paymentId)
                ?? throw new ArgumentException($"Payment {paymentId} not found.");

            var oldAllocations = existing.SessionPayments?.ToList() ?? new List<SessionPayment>();
            // This payment's own current allocation to a session is credited back when checking that
            // session's amount due, so re-saving an unchanged payment never fails its own limit.
            var creditBack = oldAllocations
                .GroupBy(sp => sp.TherapySessionId)
                .ToDictionary(g => g.Key, g => g.Sum(sp => sp.AmountAllocated));

            await ValidateAsync(request.CaretakerId, request.PaymentTypeId, request.Amount,
                request.SessionAllocations, creditBack);

            await _sessionPaymentRepo.DeleteByPaymentIdAsync(paymentId);

            existing.Amount = request.Amount;
            existing.PaymentDate = request.PaymentDate;
            existing.CaretakerId = request.CaretakerId;
            existing.PaymentTypeId = request.PaymentTypeId;
            existing.CheckNumber = request.CheckNumber;
            await _paymentRepo.UpdateAsync(existing);

            foreach (var alloc in request.SessionAllocations)
            {
                await _sessionPaymentRepo.AddAsync(new SessionPayment
                {
                    PaymentId = paymentId,
                    TherapySessionId = alloc.SessionId,
                    AmountAllocated = alloc.AmountAllocated
                });
            }

            // Recalculate all affected sessions (old + new).
            var affected = oldAllocations.Select(sp => sp.TherapySessionId)
                .Union(request.SessionAllocations.Select(a => a.SessionId))
                .Distinct();
            foreach (var sessionId in affected)
            {
                await RecomputeAmountPaidAsync(sessionId);
            }

            return true;
        });
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

    /// <summary>
    /// WP-58: every check a payment write needs, run BEFORE any write. All failures are
    /// <see cref="ArgumentException"/> → 400 (GlobalExceptionHandler).
    /// </summary>
    private async Task ValidateAsync(int caretakerId, int paymentTypeId, decimal amount,
        List<SessionAllocationItem> allocations, IReadOnlyDictionary<int, decimal> creditBack)
    {
        if (amount <= 0)
            throw new ArgumentException("Payment amount must be greater than zero.");

        if (allocations.Any(a => a.AmountAllocated <= 0))
            throw new ArgumentException("Each allocation must be greater than zero.");

        var totalAllocated = allocations.Sum(a => a.AmountAllocated);
        if (totalAllocated > amount)
            throw new ArgumentException($"Total allocations ({totalAllocated:C}) exceed payment amount ({amount:C}).");

        // One SessionPayment row per (session, payment). The DB unique key would reject a repeat
        // anyway, but only after earlier rows had been written.
        var duplicate = allocations.GroupBy(a => a.SessionId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"Session {duplicate.Key} appears more than once in the allocations.");

        // F-4: an unknown caretaker used to surface as a 500 (Payment.PaidBy FK violation).
        if (await _caretakerRepo.GetByIdAsync(caretakerId) == null)
            throw new ArgumentException($"Caretaker {caretakerId} not found.");

        if (await _paymentTypeRepo.GetByIdAsync(paymentTypeId) == null)
            throw new ArgumentException($"Payment type {paymentTypeId} not found.");

        if (allocations.Count == 0) return;

        // F-3: a caretaker's payment may only cover sessions of patients linked to that caretaker.
        var linkedPatientIds = (await _patientCaretakerRepo.GetByCaretakerIdAsync(caretakerId))
            .Select(pc => pc.PatientId)
            .ToHashSet();

        foreach (var alloc in allocations)
        {
            var session = await _sessionRepo.GetByIdAsync(alloc.SessionId)
                ?? throw new ArgumentException($"Session {alloc.SessionId} not found.");

            if (!linkedPatientIds.Contains(session.PatientId))
                throw new ArgumentException(
                    $"Session {alloc.SessionId} belongs to a patient who is not linked to caretaker {caretakerId}.");

            var due = session.AmountDue() + creditBack.GetValueOrDefault(alloc.SessionId);
            if (alloc.AmountAllocated > due)
                throw new ArgumentException(
                    $"Allocation {alloc.AmountAllocated:C} exceeds amount due {due:C} for session {alloc.SessionId}.");
        }
    }

    /// <summary>
    /// WP-58: <c>AmountPaid = LegacyAmountPaid + SUM(allocations)</c>. The legacy baseline (V037)
    /// keeps spreadsheet-imported paid amounts from being wiped by the first real payment.
    /// </summary>
    private async Task RecomputeAmountPaidAsync(int sessionId)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId);
        if (session == null) return;

        var allocated = await _sessionPaymentRepo.GetAllocatedTotalForSessionAsync(sessionId);
        session.AmountPaid = session.LegacyAmountPaid + allocated;
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
