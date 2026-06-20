using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.ServicePayments;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
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

        var service = new ServicePaymentService(logger, spRepo.Object, sspRepo.Object, sessionRepo.Object, statusRepo.Object, therapistSvc.Object);
        return (service, spRepo, sspRepo, sessionRepo);
    }

    private static TherapySession Session(int id, decimal providerAmount, DateOnly? date = null, int therapistId = TherapistId) => new()
    {
        Id = id,
        PatientId = 1,
        TherapistId = therapistId,
        SessionDate = date ?? new DateOnly(2026, 4, 5),
        SessionTime = new TimeOnly(9, 0),
        AppointmentStatusId = CompletedStatus.Id,
        ProviderAmount = providerAmount,
        Patient = new Patient { Id = 1, User = new User { Id = 100, FirstName = "Dan", LastName = "Green" } },
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
}
