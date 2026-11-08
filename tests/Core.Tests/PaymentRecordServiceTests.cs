using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-58 — money integrity of payment create/update, driven by the 2026-11-08 E2E findings
/// (patient-care-super/planning/audits/e2e-prerollout-2026-11-08.md): F-1 non-atomic update,
/// F-3 cross-family allocation, F-4 unknown caretaker → 500, and the legacy paid baseline.
/// </summary>
public class PaymentRecordServiceTests
{
    private const int CaretakerId = 1135;
    private const int LinkedPatientId = 922;
    private const int OtherPatientId = 140;
    private const int CashTypeId = 4;

    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISessionPaymentRepository> _sessionPayments = new();
    private readonly Mock<ITherapySessionRepository> _sessions = new();
    private readonly Mock<IPatientCaretakerRepository> _links = new();
    private readonly Mock<IRepository<PaymentType>> _paymentTypes = new();
    private readonly Mock<ICaretakerRepository> _caretakers = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly PaymentRecordService _sut;

    // Ledger state the allocation-total mock answers from (sessionId → allocated).
    private readonly Dictionary<int, decimal> _allocated = new();

    public PaymentRecordServiceTests()
    {
        // Pass-through unit of work (pattern: PatientMergeServiceTests). Atomicity itself is
        // EfUnitOfWork's job; here we prove nothing is WRITTEN when validation fails.
        _uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<int>>>())).Returns((Func<Task<int>> op) => op());
        _uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<bool>>>())).Returns((Func<Task<bool>> op) => op());

        _caretakers.Setup(r => r.GetByIdAsync(CaretakerId)).ReturnsAsync(new Caretaker { Id = CaretakerId });
        _paymentTypes.Setup(r => r.GetByIdAsync(CashTypeId)).ReturnsAsync(new PaymentType { Id = CashTypeId });
        _links.Setup(r => r.GetByCaretakerIdAsync(CaretakerId))
            .ReturnsAsync(new[] { new PatientCaretaker { PatientId = LinkedPatientId, CaretakerId = CaretakerId } });
        _payments.Setup(r => r.AddAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => { p.Id = 2244; return p; });
        _payments.Setup(r => r.GetByIdWithDetailsAsync(2244))
            .ReturnsAsync(new Payment { Id = 2244, SessionPayments = new List<SessionPayment>() });
        _sessionPayments.Setup(r => r.GetAllocatedTotalForSessionAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _allocated.GetValueOrDefault(id));
        _sessionPayments.Setup(r => r.AddAsync(It.IsAny<SessionPayment>()))
            .ReturnsAsync((SessionPayment sp) =>
            {
                _allocated[sp.TherapySessionId] = _allocated.GetValueOrDefault(sp.TherapySessionId) + sp.AmountAllocated;
                return sp;
            });

        _sut = new PaymentRecordService(
            Mock.Of<ILogger<PaymentRecordService>>(), _payments.Object, _sessionPayments.Object,
            _sessions.Object, _links.Object, _paymentTypes.Object, _caretakers.Object, _uow.Object);
    }

    private TherapySession Session(int id, int patientId = LinkedPatientId, decimal amount = 50m,
        decimal discount = 10m, decimal amountPaid = 0m, decimal legacyPaid = 0m)
    {
        var s = new TherapySession
        {
            Id = id, PatientId = patientId, Amount = amount, DiscountAmount = discount,
            AmountPaid = amountPaid, LegacyAmountPaid = legacyPaid,
        };
        _sessions.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(s);
        return s;
    }

    private static PaymentRecordRequest Create(decimal amount, params (int sessionId, decimal alloc)[] allocations) => new()
    {
        Amount = amount, PaymentDate = new DateTime(2026, 11, 8), CaretakerId = CaretakerId, PaymentTypeId = CashTypeId,
        SessionAllocations = allocations.Select(a => new SessionAllocationItem { SessionId = a.sessionId, AmountAllocated = a.alloc }).ToList(),
    };

    private static PaymentRecordUpdateRequest Update(decimal amount, int caretakerId, params (int sessionId, decimal alloc)[] allocations) => new()
    {
        Amount = amount, PaymentDate = new DateTime(2026, 11, 8), CaretakerId = caretakerId, PaymentTypeId = CashTypeId,
        SessionAllocations = allocations.Select(a => new SessionAllocationItem { SessionId = a.sessionId, AmountAllocated = a.alloc }).ToList(),
    };

    private void AssertNothingWritten()
    {
        _payments.Verify(r => r.AddAsync(It.IsAny<Payment>()), Times.Never);
        _payments.Verify(r => r.UpdateAsync(It.IsAny<Payment>()), Times.Never);
        _sessionPayments.Verify(r => r.AddAsync(It.IsAny<SessionPayment>()), Times.Never);
        _sessionPayments.Verify(r => r.DeleteByPaymentIdAsync(It.IsAny<int>()), Times.Never);
        _sessions.Verify(r => r.UpdateAsync(It.IsAny<TherapySession>()), Times.Never);
    }

    [Fact]
    public async Task Create_UnknownCaretaker_Is400_AndWritesNothing()
    {
        // F-4: used to reach the DB and fail on the Payment.PaidBy FK → 500.
        Session(12352);
        var request = Create(25m, (12352, 25m));
        request.CaretakerId = 0;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Contains("Caretaker 0 not found", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Create_SessionOfAnUnlinkedPatient_Is400_AndWritesNothing()
    {
        // F-3 repro: caretaker 1135 (Duarte) paying session 12342 of patient 140 (Rivas) → was 201.
        Session(12342, patientId: OtherPatientId, amount: 75m, discount: 0m);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Create(5m, (12342, 5m))));

        Assert.Contains("not linked to caretaker 1135", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Create_DuplicateSessionInOneRequest_Is400()
    {
        Session(12350);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(Create(30m, (12350, 10m), (12350, 20m))));

        Assert.Contains("more than once", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Create_OnALegacySession_KeepsTheImportedPaidBaseline()
    {
        // VERGARA #11042: $175 billed, $50 paid per the legacy spreadsheet (no Payment row).
        // Before WP-58 the recompute was SUM(allocations) only, so this $20 payment would have
        // dropped AmountPaid from 50 to 20.
        var legacy = Session(11042, amount: 175m, discount: 0m, amountPaid: 50m, legacyPaid: 50m);

        await _sut.CreateAsync(Create(20m, (11042, 20m)));

        Assert.Equal(70m, legacy.AmountPaid);   // 50 legacy + 20 allocated
        Assert.False(legacy.IsPaidOff);         // 175 − 70 still due
        _sessions.Verify(r => r.UpdateAsync(legacy), Times.Once);
    }

    [Fact]
    public async Task Create_FullyPaysASession_SetsIsPaidOff()
    {
        var s = Session(12349, amount: 50m, discount: 10m);

        await _sut.CreateAsync(Create(40m, (12349, 40m)));

        Assert.Equal(40m, s.AmountPaid);
        Assert.True(s.IsPaidOff);
    }

    [Fact]
    public async Task Update_WithAnUnknownSession_FailsBeforeDeletingTheOldAllocations()
    {
        // F-1 repro B (payment 2244): the "Session 999999 not found" 400 used to come AFTER the
        // old allocations had been deleted and saved, leaving session 12353 marked paid with no
        // allocation behind it.
        Session(12349, amountPaid: 40m);
        _payments.Setup(r => r.GetByIdWithDetailsAsync(2244)).ReturnsAsync(new Payment
        {
            Id = 2244, CaretakerId = CaretakerId, Amount = 60m,
            SessionPayments = new List<SessionPayment>
            {
                new() { PaymentId = 2244, TherapySessionId = 12349, AmountAllocated = 40m },
                new() { PaymentId = 2244, TherapySessionId = 12353, AmountAllocated = 20m },
            },
        });

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(2244, Update(60m, CaretakerId, (12349, 40m), (999999, 20m))));

        Assert.Contains("Session 999999 not found", ex.Message);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Update_WithMissingCaretaker_Is400_BeforeAnyWrite()
    {
        // F-1 repro A + F-4 (payment 2245): PUT without caretakerId → FK 500 after the delete.
        Session(12352, amountPaid: 25m);
        _payments.Setup(r => r.GetByIdWithDetailsAsync(2245)).ReturnsAsync(new Payment
        {
            Id = 2245, CaretakerId = CaretakerId, Amount = 25m,
            SessionPayments = new List<SessionPayment> { new() { PaymentId = 2245, TherapySessionId = 12352, AmountAllocated = 25m } },
        });

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(2245, Update(25m, 0, (12352, 25m))));

        AssertNothingWritten();
    }

    [Fact]
    public async Task Update_ResavingAnUnchangedPayment_CreditsBackItsOwnAllocation()
    {
        // Session fully paid by THIS payment: AmountDue() is 0, but re-saving the same 40 must pass.
        var s = Session(12349, amount: 50m, discount: 10m, amountPaid: 40m);
        _allocated[12349] = 40m;
        _payments.Setup(r => r.GetByIdWithDetailsAsync(2244)).ReturnsAsync(new Payment
        {
            Id = 2244, CaretakerId = CaretakerId, Amount = 40m,
            SessionPayments = new List<SessionPayment> { new() { PaymentId = 2244, TherapySessionId = 12349, AmountAllocated = 40m } },
        });
        _sessionPayments.Setup(r => r.DeleteByPaymentIdAsync(2244)).Callback(() => _allocated.Remove(12349)).Returns(Task.CompletedTask);

        await _sut.UpdateAsync(2244, Update(40m, CaretakerId, (12349, 40m)));

        Assert.Equal(40m, s.AmountPaid);
        Assert.True(s.IsPaidOff);
    }

    [Fact]
    public async Task Update_MovingAnAllocation_RecomputesTheSessionItLeft()
    {
        var left = Session(12351, amountPaid: 20m);
        var gained = Session(12353);
        _allocated[12351] = 20m;
        _payments.Setup(r => r.GetByIdWithDetailsAsync(2244)).ReturnsAsync(new Payment
        {
            Id = 2244, CaretakerId = CaretakerId, Amount = 20m,
            SessionPayments = new List<SessionPayment> { new() { PaymentId = 2244, TherapySessionId = 12351, AmountAllocated = 20m } },
        });
        _sessionPayments.Setup(r => r.DeleteByPaymentIdAsync(2244)).Callback(() => _allocated.Remove(12351)).Returns(Task.CompletedTask);

        await _sut.UpdateAsync(2244, Update(20m, CaretakerId, (12353, 20m)));

        Assert.Equal(0m, left.AmountPaid);
        Assert.Equal(20m, gained.AmountPaid);
    }
}
