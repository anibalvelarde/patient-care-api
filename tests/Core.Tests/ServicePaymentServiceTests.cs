using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

public class ServicePaymentServiceTests
{
    private const int TherapistId = 42;
    private static readonly AppointmentStatus CompletedStatus = new() { Id = 4, Name = "Completed" };

    private static (
        ServicePaymentService Service,
        Mock<IServicePaymentRepository> ServicePaymentRepo,
        Mock<ISessionServicePaymentRepository> SspRepo,
        Mock<ITherapySessionRepository> SessionRepo
    ) BuildService(
        IReadOnlyList<TherapySession>? completedSessions = null,
        IReadOnlyList<SessionServicePayment>? existingAllocations = null,
        IReadOnlyList<TherapistProfile>? therapists = null)
    {
        var logger = Mock.Of<ILogger<ServicePaymentService>>();

        var spRepo = new Mock<IServicePaymentRepository>();
        var sspRepo = new Mock<ISessionServicePaymentRepository>();
        var sessionRepo = new Mock<ITherapySessionRepository>();
        var statusRepo = new Mock<IRepository<AppointmentStatus>>();
        var therapistSvc = new Mock<ITherapistProfileService>();

        statusRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AppointmentStatus> { CompletedStatus });
        therapistSvc.Setup(s => s.GetAllAsync()).ReturnsAsync(therapists ?? new List<TherapistProfile>());

        var allocations = existingAllocations ?? new List<SessionServicePayment>();
        sspRepo
            .Setup(r => r.GetBySessionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((IEnumerable<int> ids) =>
            {
                var set = ids.ToHashSet();
                return allocations.Where(a => set.Contains(a.TherapySessionId)).ToList();
            });
        sspRepo.Setup(r => r.AddAsync(It.IsAny<SessionServicePayment>()))
            .ReturnsAsync((SessionServicePayment s) => s);

        var sessions = completedSessions ?? new List<TherapySession>();
        // Filter by therapist so multi-therapist (preview / batch) scenarios are isolated.
        sessionRepo
            .Setup(r => r.GetByTherapistIdAndDateRangeAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((int therapistId, DateOnly _, DateOnly _, IEnumerable<int> _) =>
                sessions.Where(s => s.TherapistId == therapistId).ToList());
        sessionRepo
            .Setup(r => r.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => sessions.FirstOrDefault(s => s.Id == id));

        // Default create wiring so RunBatchPayroll works without per-test setup: assign ids and echo
        // the created payment back from GetByIdWithDetailsAsync. (Individual tests may override.)
        var nextId = 1;
        var createdById = new Dictionary<int, ServicePayment>();
        spRepo.Setup(r => r.AddAsync(It.IsAny<ServicePayment>()))
            .ReturnsAsync((ServicePayment sp) => { sp.Id = nextId++; createdById[sp.Id] = sp; return sp; });
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => createdById.TryGetValue(id, out var sp) ? sp : null);

        // Reversal helpers default to "nothing reversed"; individual tests override.
        spRepo.Setup(r => r.IsReversedAsync(It.IsAny<int>())).ReturnsAsync(false);
        spRepo.Setup(r => r.GetReversedOriginalIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(Array.Empty<int>());

        var service = new ServicePaymentService(logger, spRepo.Object, sspRepo.Object, sessionRepo.Object, statusRepo.Object, therapistSvc.Object);
        return (service, spRepo, sspRepo, sessionRepo);
    }

    private static TherapySession Session(int id, decimal providerAmount, DateOnly? date = null, int therapistId = TherapistId, decimal amount = 0m, int patientId = 1) => new()
    {
        Id = id,
        PatientId = patientId,
        TherapistId = therapistId,
        SessionDate = date ?? new DateOnly(2026, 4, 5),
        SessionTime = new TimeOnly(9, 0),
        AppointmentStatusId = CompletedStatus.Id,
        Amount = amount,
        ProviderAmount = providerAmount,
        Patient = new Patient { Id = patientId, User = new User { Id = 100 + patientId, FirstName = "Dan", LastName = "Green" } },
    };

    private static TherapistProfile Therapist(int id, string name, bool active = true) =>
        new() { TherapistId = id, TherapistName = name, IsActive = active };

    [Fact]
    public async Task CreateAsync_PersistsPaymentAndAllocations()
    {
        var (service, spRepo, sspRepo, _) = BuildService(
            completedSessions: new List<TherapySession> { Session(1, 60m), Session(2, 60m) });

        spRepo.Setup(r => r.AddAsync(It.IsAny<ServicePayment>()))
            .ReturnsAsync((ServicePayment sp) => { sp.Id = 500; return sp; });

        spRepo.Setup(r => r.GetByIdWithDetailsAsync(500)).ReturnsAsync(new ServicePayment
        {
            Id = 500,
            TherapistId = TherapistId,
            Amount = 120m,
            PaymentDate = new DateTime(2026, 4, 20),
            PaymentType = new PaymentType { Id = 1, Name = "Bank Transfer", Abbreviation = "ACH" },
            Therapist = new Therapist { Id = TherapistId, User = new User { Id = 9, FirstName = "Jane", LastName = "Smith" } },
            SessionServicePayments = new List<SessionServicePayment>
            {
                new() { Id = 1, ServicePaymentId = 500, TherapySessionId = 1, AmountApplied = 60m, TherapySession = Session(1, 60m) },
                new() { Id = 2, ServicePaymentId = 500, TherapySessionId = 2, AmountApplied = 60m, TherapySession = Session(2, 60m) },
            },
        });

        var request = new ServicePaymentRequest
        {
            TherapistId = TherapistId,
            Amount = 120m,
            PaymentDate = new DateTime(2026, 4, 20),
            PaymentTypeId = 1,
            SessionAllocations = new List<SessionServiceAllocationItem>
            {
                new() { SessionId = 1, AmountApplied = 60m },
                new() { SessionId = 2, AmountApplied = 60m },
            },
        };

        var result = await service.CreateAsync(request);

        result.ServicePaymentId.Should().Be(500);
        result.TherapistName.Should().Be("Smith, Jane");
        result.Allocations.Should().HaveCount(2);
        result.TotalApplied.Should().Be(120m);
        result.UnallocatedAmount.Should().Be(0m);
        spRepo.Verify(r => r.AddAsync(It.IsAny<ServicePayment>()), Times.Once);
        sspRepo.Verify(r => r.AddAsync(It.IsAny<SessionServicePayment>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenAmountNotPositive()
    {
        var (service, _, _, _) = BuildService();
        var request = new ServicePaymentRequest { TherapistId = TherapistId, Amount = 0m, PaymentTypeId = 1 };

        var act = async () => await service.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenAllocationsExceedAmount()
    {
        var (service, _, _, _) = BuildService(completedSessions: new List<TherapySession> { Session(1, 200m) });
        var request = new ServicePaymentRequest
        {
            TherapistId = TherapistId,
            Amount = 50m,
            PaymentTypeId = 1,
            SessionAllocations = new List<SessionServiceAllocationItem> { new() { SessionId = 1, AmountApplied = 100m } },
        };

        var act = async () => await service.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenAllocationExceedsRemainingProviderAmount()
    {
        // Session provider amount is 60; 40 already applied → only 20 remains, but we try 50.
        var existing = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = 99, TherapySessionId = 1, AmountApplied = 40m },
        };
        var (service, _, _, _) = BuildService(
            completedSessions: new List<TherapySession> { Session(1, 60m) },
            existingAllocations: existing);

        var request = new ServicePaymentRequest
        {
            TherapistId = TherapistId,
            Amount = 50m,
            PaymentTypeId = 1,
            SessionAllocations = new List<SessionServiceAllocationItem> { new() { SessionId = 1, AmountApplied = 50m } },
        };

        var act = async () => await service.CreateAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetUnpaidProviderSessionsAsync_ExcludesFullyPaidSessions()
    {
        // Session 1 fully paid (60 of 60); session 2 partly paid (20 of 60 → 40 remains).
        var sessions = new List<TherapySession> { Session(1, 60m), Session(2, 60m) };
        var existing = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = 99, TherapySessionId = 1, AmountApplied = 60m },
            new() { Id = 2, ServicePaymentId = 99, TherapySessionId = 2, AmountApplied = 20m },
        };
        var (service, _, _, _) = BuildService(completedSessions: sessions, existingAllocations: existing);

        var result = (await service.GetUnpaidProviderSessionsAsync(TherapistId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))).ToList();

        result.Should().HaveCount(1);
        result[0].SessionId.Should().Be(2);
        result[0].AlreadyApplied.Should().Be(20m);
        result[0].RemainingProviderAmount.Should().Be(40m);
    }

    [Theory]
    [InlineData(2026, 4, 10, "2026-04-01", "2026-04-15")]  // day <= 15 → first half
    [InlineData(2026, 4, 15, "2026-04-01", "2026-04-15")]  // boundary day 15
    [InlineData(2026, 4, 16, "2026-04-16", "2026-04-30")]  // boundary day 16 → second half
    [InlineData(2026, 2, 20, "2026-02-16", "2026-02-28")]  // EOM for non-leap February
    public void GetQuincenaWindow_ReturnsExpectedWindow(int y, int m, int d, string expectedFrom, string expectedTo)
    {
        var (service, _, _, _) = BuildService();

        var window = service.GetQuincenaWindow(new DateOnly(y, m, d));

        window.From.Should().Be(expectedFrom);
        window.To.Should().Be(expectedTo);
    }

    [Fact]
    public async Task GetPayrollPreviewAsync_RollsUpPerTherapist_SkippingFullyPaidAndInactive()
    {
        // 42 Smith: two sessions, one fully paid -> 60 remaining on the other.
        // 43 Jones: one session, unpaid -> 100.
        // 44 Lee: active but no sessions -> excluded. 45 Ng: has a session but inactive -> excluded.
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, therapistId: 42), Session(2, 60m, therapistId: 42),
            Session(3, 100m, therapistId: 43),
            Session(4, 80m, therapistId: 45),
        };
        var existing = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = 99, TherapySessionId = 1, AmountApplied = 60m },
        };
        var therapists = new List<TherapistProfile>
        {
            Therapist(42, "Smith, Jane"), Therapist(43, "Jones, Bob"),
            Therapist(44, "Lee, Sue"), Therapist(45, "Ng, Al", active: false),
        };
        var (service, _, _, _) = BuildService(sessions, existing, therapists);

        var preview = (await service.GetPayrollPreviewAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))).ToList();

        preview.Should().HaveCount(2);
        preview[0].TherapistName.Should().Be("Jones, Bob");   // alphabetical
        preview[0].SessionCount.Should().Be(1);
        preview[0].TotalRemaining.Should().Be(100m);
        preview[1].TherapistName.Should().Be("Smith, Jane");
        preview[1].SessionCount.Should().Be(1);
        preview[1].TotalRemaining.Should().Be(60m);
    }

    [Fact]
    public async Task GetPendingPayrollSummaryAsync_AggregatesAcrossTherapists_SkippingFullyPaidAndInactive()
    {
        // 42 Smith: two sessions, one fully paid -> 60 remaining on the other (1 session).
        // 43 Jones: one session, unpaid -> 100 (1 session).
        // 44 Lee: active but no sessions -> contributes nothing. 45 Ng: has a session but inactive -> excluded.
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, therapistId: 42), Session(2, 60m, therapistId: 42),
            Session(3, 100m, therapistId: 43),
            Session(4, 80m, therapistId: 45),
        };
        var existing = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = 99, TherapySessionId = 1, AmountApplied = 60m },
        };
        var therapists = new List<TherapistProfile>
        {
            Therapist(42, "Smith, Jane"), Therapist(43, "Jones, Bob"),
            Therapist(44, "Lee, Sue"), Therapist(45, "Ng, Al", active: false),
        };
        var (service, _, _, _) = BuildService(sessions, existing, therapists);

        // No range -> all-time outstanding.
        var summary = await service.GetPendingPayrollSummaryAsync(null, null);

        summary.TherapistCount.Should().Be(2);
        summary.SessionCount.Should().Be(2);
        summary.TotalPending.Should().Be(160m);
    }

    [Fact]
    public async Task GetPendingPayrollSummaryAsync_ReturnsZeroes_WhenNothingOwed()
    {
        var (service, _, _, _) = BuildService(
            therapists: new List<TherapistProfile> { Therapist(42, "Smith, Jane") });

        var summary = await service.GetPendingPayrollSummaryAsync(null, null);

        summary.TherapistCount.Should().Be(0);
        summary.SessionCount.Should().Be(0);
        summary.TotalPending.Should().Be(0m);
    }

    [Fact]
    public async Task GetPendingPayrollSummaryAsync_Throws_WhenToBeforeFrom()
    {
        var (service, _, _, _) = BuildService();

        var act = async () => await service.GetPendingPayrollSummaryAsync(new DateOnly(2026, 4, 30), new DateOnly(2026, 4, 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPendingPayReportAsync_BuildsPerTherapistRows_WithDatesPatientsGrossAndPct()
    {
        // Therapist 42: s1 fully paid -> excluded; s2 partly paid (40 owed, patient 2, gross 120, 04-10);
        // s3 unpaid (80 owed, patient 1, gross 200, 04-02). Owing set = {s2, s3}.
        // Therapist 45 inactive -> skipped entirely.
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, date: new DateOnly(2026, 4, 5),  therapistId: 42, amount: 100m, patientId: 1),
            Session(2, 60m, date: new DateOnly(2026, 4, 10), therapistId: 42, amount: 120m, patientId: 2),
            Session(3, 80m, date: new DateOnly(2026, 4, 2),  therapistId: 42, amount: 200m, patientId: 1),
            Session(4, 80m, date: new DateOnly(2026, 4, 4),  therapistId: 45, amount: 150m, patientId: 3),
        };
        var existing = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = 99, TherapySessionId = 1, AmountApplied = 60m }, // s1 fully paid
            new() { Id = 2, ServicePaymentId = 99, TherapySessionId = 2, AmountApplied = 20m }, // s2 -> 40 remains
        };
        var therapists = new List<TherapistProfile>
        {
            Therapist(42, "Smith, Jane"), Therapist(45, "Ng, Al", active: false),
        };
        var (service, _, _, _) = BuildService(sessions, existing, therapists);

        var report = await service.GetPendingPayReportAsync(null, null);

        report.Rows.Should().HaveCount(1);
        var row = report.Rows[0];
        row.TherapistName.Should().Be("Smith, Jane");
        row.SessionCount.Should().Be(2);
        row.FirstSessionDate.Should().Be("2026-04-02");
        row.LastSessionDate.Should().Be("2026-04-10");
        row.DistinctPatientCount.Should().Be(2);
        row.GrossBilled.Should().Be(320m);              // 120 + 200
        row.AmountOwed.Should().Be(120m);               // 40 + 80
        row.OwedPctOfGross.Should().Be(37.5m);          // 120 / 320 * 100

        // Grand totals
        report.TherapistCount.Should().Be(1);
        report.SessionCount.Should().Be(2);
        report.TotalGrossBilled.Should().Be(320m);
        report.TotalOwed.Should().Be(120m);
        report.OwedPctOfGross.Should().Be(37.5m);
    }

    [Fact]
    public async Task GetPendingPayReportAsync_Throws_WhenToBeforeFrom()
    {
        var (service, _, _, _) = BuildService();

        var act = async () => await service.GetPendingPayReportAsync(new DateOnly(2026, 4, 30), new DateOnly(2026, 4, 1));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunBatchPayrollAsync_CreatesOnePaymentPerOwedTherapist()
    {
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, therapistId: 42),
            Session(2, 100m, therapistId: 43),
        };
        var (service, spRepo, _, _) = BuildService(completedSessions: sessions);

        var result = await service.RunBatchPayrollAsync(new BatchPayrollRequest
        {
            From = new DateOnly(2026, 4, 1),
            To = new DateOnly(2026, 4, 30),
            PaymentDate = new DateTime(2026, 4, 16),
            PaymentTypeId = 1,
            TherapistIds = new List<int> { 42, 43, 99 },  // 99 has nothing owed -> skipped
        });

        result.TherapistCount.Should().Be(2);
        result.TotalPaid.Should().Be(160m);
        result.Payments.Should().HaveCount(2);
        spRepo.Verify(r => r.AddAsync(It.IsAny<ServicePayment>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunBatchPayrollAsync_Throws_WhenNoTherapistsSelected()
    {
        var (service, _, _, _) = BuildService();

        var act = async () => await service.RunBatchPayrollAsync(new BatchPayrollRequest
        {
            PaymentTypeId = 1,
            TherapistIds = new List<int>(),
        });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- WP-20: paid-in-range breakdown -------------------------------------

    private static SessionServicePayment Allocation(int id, int servicePaymentId, int sessionId, decimal amountApplied,
        string? referenceNumber = null, DateTime? paymentDate = null) => new()
    {
        Id = id,
        ServicePaymentId = servicePaymentId,
        TherapySessionId = sessionId,
        AmountApplied = amountApplied,
        ServicePayment = new ServicePayment
        {
            Id = servicePaymentId,
            TherapistId = TherapistId,
            ReferenceNumber = referenceNumber,
            PaymentDate = paymentDate ?? new DateTime(2026, 5, 31),
        },
    };

    [Fact]
    public async Task GetProviderSessionsBreakdownAsync_PartitionsPayableAndPaid_AndUpholdsInvariant()
    {
        // s1 fully paid (20 of 20) -> paid detail only; s2 partially paid (20 of 60 -> 40 remains)
        // -> payable list AND paid detail; s3 unpaid (30) -> payable only.
        var sessions = new List<TherapySession>
        {
            Session(1, 20m, date: new DateOnly(2026, 4, 5)),
            Session(2, 60m, date: new DateOnly(2026, 4, 6)),
            Session(3, 30m, date: new DateOnly(2026, 4, 7)),
        };
        var existing = new List<SessionServicePayment>
        {
            Allocation(1, 88, sessionId: 1, amountApplied: 20m, referenceNumber: "PAYROLL-2026-05"),
            Allocation(2, 88, sessionId: 2, amountApplied: 20m, referenceNumber: "PAYROLL-2026-05"),
        };
        var (service, _, _, _) = BuildService(completedSessions: sessions, existingAllocations: existing);

        var result = await service.GetProviderSessionsBreakdownAsync(TherapistId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        // Payable list: s2 (partial) + s3 (unpaid), ordered by date.
        result.Sessions.Select(s => s.SessionId).Should().Equal(2, 3);
        result.Sessions.Single(s => s.SessionId == 2).RemainingProviderAmount.Should().Be(40m);
        result.Sessions.Single(s => s.SessionId == 3).RemainingProviderAmount.Should().Be(30m);

        // Paid detail: s1 (full, remaining clamped to 0) + s2 (partial, remaining 40).
        result.PaidInRange.Sessions.Select(s => s.SessionId).Should().Equal(1, 2);
        var full = result.PaidInRange.Sessions.Single(s => s.SessionId == 1);
        full.AmountApplied.Should().Be(20m);
        full.RemainingProviderAmount.Should().Be(0m);
        var partial = result.PaidInRange.Sessions.Single(s => s.SessionId == 2);
        partial.AmountApplied.Should().Be(20m);
        partial.RemainingProviderAmount.Should().Be(40m);

        // Fully-paid rollup counts only remaining <= 0; TotalApplied spans ALL sessions.
        result.PaidInRange.FullyPaidSessionCount.Should().Be(1);
        result.PaidInRange.FullyPaidTotal.Should().Be(20m);
        result.PaidInRange.TotalApplied.Should().Be(40m);

        // Reconciliation invariant: ΣProvider = ΣRemaining (payable) + TotalApplied.
        var totalProvider = sessions.Sum(s => s.ProviderAmount);
        var totalRemaining = result.Sessions.Sum(s => s.RemainingProviderAmount);
        (totalRemaining + result.PaidInRange.TotalApplied).Should().Be(totalProvider);
    }

    [Fact]
    public async Task GetProviderSessionsBreakdownAsync_ReturnsEmptyEnvelope_WhenNoSessionsInRange()
    {
        var (service, _, _, _) = BuildService();

        var result = await service.GetProviderSessionsBreakdownAsync(TherapistId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        result.Sessions.Should().BeEmpty();
        result.PaidInRange.Sessions.Should().BeEmpty();
        result.PaidInRange.FullyPaidSessionCount.Should().Be(0);
        result.PaidInRange.FullyPaidTotal.Should().Be(0m);
        result.PaidInRange.TotalApplied.Should().Be(0m);
    }

    [Fact]
    public async Task GetProviderSessionsBreakdownAsync_PopulatesPaymentReferences_GroupingMultipleAllocationsPerSession()
    {
        // One session covered by two service payments (30 + 30 of 60 -> fully paid).
        var sessions = new List<TherapySession> { Session(1, 60m) };
        var existing = new List<SessionServicePayment>
        {
            Allocation(1, 88, sessionId: 1, amountApplied: 30m, referenceNumber: "PAYROLL-2026-05", paymentDate: new DateTime(2026, 5, 31)),
            Allocation(2, 91, sessionId: 1, amountApplied: 30m, referenceNumber: null, paymentDate: new DateTime(2026, 6, 15)),
        };
        var (service, _, _, _) = BuildService(completedSessions: sessions, existingAllocations: existing);

        var result = await service.GetProviderSessionsBreakdownAsync(TherapistId, new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30));

        result.PaidInRange.Sessions.Should().HaveCount(1);
        var detail = result.PaidInRange.Sessions[0];
        detail.AmountApplied.Should().Be(60m);
        detail.PaymentReferences.Should().HaveCount(2);

        var ref1 = detail.PaymentReferences.Single(r => r.ServicePaymentId == 88);
        ref1.ReferenceNumber.Should().Be("PAYROLL-2026-05");
        ref1.PaymentDate.Should().Be("2026-05-31");
        ref1.AmountApplied.Should().Be(30m);

        var ref2 = detail.PaymentReferences.Single(r => r.ServicePaymentId == 91);
        ref2.ReferenceNumber.Should().Be(string.Empty); // null reference -> empty string
        ref2.PaymentDate.Should().Be("2026-06-15");
        ref2.AmountApplied.Should().Be(30m);
    }

    [Fact]
    public async Task GetUnpaidProviderSessionsAsync_DelegatesToBreakdown_ReturningExactlyThePayableList()
    {
        // Guards batch payroll: the legacy method must return exactly the breakdown's payable list.
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, date: new DateOnly(2026, 4, 5)),
            Session(2, 60m, date: new DateOnly(2026, 4, 6)),
            Session(3, 30m, date: new DateOnly(2026, 4, 7)),
        };
        var existing = new List<SessionServicePayment>
        {
            Allocation(1, 99, sessionId: 1, amountApplied: 60m), // fully paid -> excluded
            Allocation(2, 99, sessionId: 2, amountApplied: 20m), // partial -> included
        };
        var (service, _, _, _) = BuildService(completedSessions: sessions, existingAllocations: existing);
        var from = new DateOnly(2026, 4, 1);
        var to = new DateOnly(2026, 4, 30);

        var unpaid = (await service.GetUnpaidProviderSessionsAsync(TherapistId, from, to)).ToList();
        var breakdown = await service.GetProviderSessionsBreakdownAsync(TherapistId, from, to);

        unpaid.Should().BeEquivalentTo(breakdown.Sessions, o => o.WithStrictOrdering());
        unpaid.Select(s => s.SessionId).Should().Equal(2, 3);
        unpaid[0].AlreadyApplied.Should().Be(20m);
        unpaid[0].RemainingProviderAmount.Should().Be(40m);
    }

    [Fact]
    public async Task GetPayrollPreviewAsync_PopulatesPaidContext_AndStillSkipsOnlyPaidTherapists()
    {
        // 42 Smith: one fully paid (60) + one partially paid (20 of 60 -> 40 owed)
        //   -> row with PaidSessionCount 1 (fully paid only) and PaidTotal 80 (all applied money).
        // 43 Jones: single fully-paid session -> still skipped (paid context rides existing rows only).
        var sessions = new List<TherapySession>
        {
            Session(1, 60m, therapistId: 42), Session(2, 60m, therapistId: 42),
            Session(3, 100m, therapistId: 43),
        };
        var existing = new List<SessionServicePayment>
        {
            Allocation(1, 99, sessionId: 1, amountApplied: 60m),
            Allocation(2, 99, sessionId: 2, amountApplied: 20m),
            Allocation(3, 99, sessionId: 3, amountApplied: 100m),
        };
        var therapists = new List<TherapistProfile>
        {
            Therapist(42, "Smith, Jane"), Therapist(43, "Jones, Bob"),
        };
        var (service, _, _, _) = BuildService(sessions, existing, therapists);

        var preview = (await service.GetPayrollPreviewAsync(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30))).ToList();

        preview.Should().HaveCount(1); // Jones (only-paid) skipped
        var row = preview[0];
        row.TherapistName.Should().Be("Smith, Jane");
        row.SessionCount.Should().Be(1);
        row.TotalRemaining.Should().Be(40m);
        row.PaidSessionCount.Should().Be(1);   // fully paid sessions only
        row.PaidTotal.Should().Be(80m);        // Σ applied across ALL in-range sessions (60 + 20)
    }

    // ---- WP-14.5: reversal -------------------------------------------------

    private static ServicePayment OriginalPayment(int id = 500, decimal amount = 120m) => new()
    {
        Id = id,
        TherapistId = TherapistId,
        Amount = amount,
        PaymentDate = new DateTime(2026, 4, 20),
        PaymentTypeId = 1,
        ReferenceNumber = "CHK-1",
        ReversesServicePaymentId = null,
        PaymentType = new PaymentType { Id = 1, Name = "Check", Abbreviation = "CHK" },
        Therapist = new Therapist { Id = TherapistId, User = new User { Id = 9, FirstName = "Jane", LastName = "Smith" } },
        SessionServicePayments = new List<SessionServicePayment>
        {
            new() { Id = 1, ServicePaymentId = id, TherapySessionId = 1, AmountApplied = 60m, TherapySession = Session(1, 60m) },
            new() { Id = 2, ServicePaymentId = id, TherapySessionId = 2, AmountApplied = 60m, TherapySession = Session(2, 60m) },
        },
    };

    [Fact]
    public async Task ReverseAsync_WritesOffsettingEntry_WithNegativeAmountAndAllocations()
    {
        var (service, spRepo, sspRepo, _) = BuildService();
        var original = OriginalPayment();

        ServicePayment? captured = null;
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(500)).ReturnsAsync(original);
        spRepo.Setup(r => r.AddAsync(It.IsAny<ServicePayment>()))
            .ReturnsAsync((ServicePayment sp) => { sp.Id = 999; captured = sp; return sp; });
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(999)).ReturnsAsync(() => captured);

        var capturedAllocations = new List<SessionServicePayment>();
        sspRepo.Setup(r => r.AddAsync(It.IsAny<SessionServicePayment>()))
            .ReturnsAsync((SessionServicePayment ssp) => { capturedAllocations.Add(ssp); return ssp; });

        var result = await service.ReverseAsync(500, new ReverseServicePaymentRequest { Reason = "duplicate payment" });

        captured.Should().NotBeNull();
        captured!.Amount.Should().Be(-120m);
        captured.ReversesServicePaymentId.Should().Be(500);
        captured.TherapistId.Should().Be(TherapistId);
        captured.PaymentTypeId.Should().Be(1);
        captured.Notes.Should().Be("Reversal of payment #500: duplicate payment");

        capturedAllocations.Should().HaveCount(2);
        capturedAllocations.Should().OnlyContain(a => a.ServicePaymentId == 999);
        capturedAllocations.Select(a => a.AmountApplied).Should().BeEquivalentTo(new[] { -60m, -60m });

        result.Amount.Should().Be(-120m);
        result.ReversesServicePaymentId.Should().Be(500);
    }

    [Fact]
    public async Task ReverseAsync_StampsTheProvidedPaymentDate_NotUtcNow()
    {
        // Regression (WP-14.5): the reversal must record the client-supplied local business date so it
        // lines up with the other date-only payments — not DateTime.UtcNow, which can roll to the next
        // calendar day for users west of UTC.
        var (service, spRepo, _, _) = BuildService();
        var original = OriginalPayment();

        ServicePayment? captured = null;
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(500)).ReturnsAsync(original);
        spRepo.Setup(r => r.AddAsync(It.IsAny<ServicePayment>()))
            .ReturnsAsync((ServicePayment sp) => { sp.Id = 999; captured = sp; return sp; });
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(999)).ReturnsAsync(() => captured);

        var localToday = new DateTime(2026, 6, 29);
        await service.ReverseAsync(500, new ReverseServicePaymentRequest { Reason = "wrong therapist", PaymentDate = localToday });

        captured!.PaymentDate.Should().Be(localToday);
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenOriginalNotFound()
    {
        var (service, spRepo, _, _) = BuildService();
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(404)).ReturnsAsync((ServicePayment?)null);

        var act = async () => await service.ReverseAsync(404, new ReverseServicePaymentRequest { Reason = "x" });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenReasonMissing()
    {
        var (service, _, _, _) = BuildService();

        var act = async () => await service.ReverseAsync(500, new ReverseServicePaymentRequest { Reason = "  " });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenAlreadyReversed()
    {
        var (service, spRepo, _, _) = BuildService();
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(500)).ReturnsAsync(OriginalPayment());
        spRepo.Setup(r => r.IsReversedAsync(500)).ReturnsAsync(true);

        var act = async () => await service.ReverseAsync(500, new ReverseServicePaymentRequest { Reason = "x" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReverseAsync_Throws_WhenReversingAReversal()
    {
        var (service, spRepo, _, _) = BuildService();
        var reversal = OriginalPayment(id: 600, amount: -120m);
        reversal.ReversesServicePaymentId = 500; // this row is itself a reversal
        spRepo.Setup(r => r.GetByIdWithDetailsAsync(600)).ReturnsAsync(reversal);

        var act = async () => await service.ReverseAsync(600, new ReverseServicePaymentRequest { Reason = "x" });

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetByTherapistAsync_FlagsReversedOriginalsAndReversalRows()
    {
        var (service, spRepo, _, _) = BuildService();
        var original = OriginalPayment(id: 500);
        var reversal = OriginalPayment(id: 600, amount: -120m);
        reversal.ReversesServicePaymentId = 500;

        spRepo.Setup(r => r.GetByTherapistIdAndDateRangeAsync(TherapistId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<ServicePayment> { original, reversal });
        spRepo.Setup(r => r.GetReversedOriginalIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new[] { 500 });

        var records = (await service.GetByTherapistAsync(TherapistId, null, null)).ToList();

        var originalRecord = records.Single(r => r.ServicePaymentId == 500);
        originalRecord.IsReversed.Should().BeTrue();
        originalRecord.ReversesServicePaymentId.Should().BeNull();

        var reversalRecord = records.Single(r => r.ServicePaymentId == 600);
        reversalRecord.IsReversed.Should().BeFalse();
        reversalRecord.ReversesServicePaymentId.Should().Be(500);
    }
}
