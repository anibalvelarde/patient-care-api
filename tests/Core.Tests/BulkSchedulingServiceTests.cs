using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;

namespace Core.Tests;

public class BulkSchedulingServiceTests
{
    private readonly Mock<ITreatmentPlanRepository> _mockPlanRepo;
    private readonly Mock<ITherapySessionRepository> _mockSessionRepo;
    private readonly Mock<ITherapistRepository> _mockTherapistRepo;
    private readonly Mock<ITherapistSpecialtyRepository> _mockSpecialtyRepo;
    private readonly BulkSchedulingService _sut;

    public BulkSchedulingServiceTests()
    {
        var logger = Mock.Of<ILogger<BulkSchedulingService>>();
        _mockPlanRepo = new Mock<ITreatmentPlanRepository>();
        _mockSessionRepo = new Mock<ITherapySessionRepository>();
        _mockTherapistRepo = new Mock<ITherapistRepository>();
        _mockSpecialtyRepo = new Mock<ITherapistSpecialtyRepository>();
        _sut = new BulkSchedulingService(
            logger,
            _mockPlanRepo.Object,
            _mockSessionRepo.Object,
            _mockTherapistRepo.Object,
            _mockSpecialtyRepo.Object);
    }

    private TreatmentPlan CreateActivePlan(int id = 1, int patientId = 1, int weeks = 2, int frequency = 1)
    {
        return new TreatmentPlan
        {
            Id = id,
            PatientId = patientId,
            PlanStatus = "Active",
            DurationWeeks = weeks,
            WeeklyFrequency = frequency,
            DiscoverySessionId = 100,
            CreatedByTherapistId = 10,
            Lines =
            [
                new TreatmentPlanLine
                {
                    Id = 1,
                    TreatmentPlanId = id,
                    SpecialtyTypeId = 5,
                    PreferredTherapistId = 10,
                    DayOfWeek = 1, // Monday
                    PreferredTime = new TimeOnly(10, 0),
                    Duration = 60,
                    SortOrder = 0,
                    SpecialtyType = new SpecialtyType { Id = 5, Abbreviation = "FS", Name = "Full Speech" }
                }
            ]
        };
    }

    private void SetupTherapist(int id, string first, string last, decimal feePct = 0, decimal feeFlat = 0)
    {
        var therapist = new Therapist
        {
            Id = id,
            TherapistId = id,
            FeePctPerSession = feePct,
            FeePerSession = feeFlat,
            User = new User { FirstName = first, LastName = last }
        };
        _mockTherapistRepo
            .Setup(r => r.GetByIdsWithUserAsync(It.Is<IEnumerable<int>>(ids => ids.Contains(id))))
            .ReturnsAsync(new List<Therapist> { therapist });
    }

    private void SetupCommonMocks(TreatmentPlan plan)
    {
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(plan.Id)).ReturnsAsync(plan);
        _mockSessionRepo.Setup(r => r.HasSessionsForPlanAsync(plan.Id)).ReturnsAsync(false);
        _mockSessionRepo.Setup(r => r.GetTherapistIdsForPatientAsync(plan.PatientId))
            .ReturnsAsync(new List<int>());
        _mockSessionRepo.Setup(r => r.GetByTherapistAndDateRangeAsync(It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<TherapySession>());
        _mockSessionRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<TherapySession>>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ScheduleAsync_HappyPath_CreatesCorrectNumberOfSessions()
    {
        // 2 weeks, 1 session/week = 2 sessions
        var plan = CreateActivePlan(weeks: 2, frequency: 1);
        SetupCommonMocks(plan);
        SetupTherapist(10, "Dr.", "Smith", feePct: 0.40m);
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5)).ReturnsAsync(new List<int> { 10 });

        var request = new BulkScheduleRequest
        {
            StartDate = new DateOnly(2026, 4, 13), // Monday
            SiteId = 1
        };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Equal(2, result.SessionsCreated);
        Assert.Empty(result.Conflicts);
        Assert.Equal(2, result.Sessions.Count);
        Assert.Equal(new DateOnly(2026, 4, 13), result.Sessions[0].SessionDate);
        Assert.Equal(new DateOnly(2026, 4, 20), result.Sessions[1].SessionDate);
    }

    [Fact]
    public async Task ScheduleAsync_PricingModel_ProRatesAndAppliesDiscount()
    {
        var plan = CreateActivePlan(weeks: 1, frequency: 1);
        plan.Lines.First().Duration = 45; // 45 min
        SetupCommonMocks(plan);
        SetupTherapist(10, "Dr.", "Smith", feePct: 0.40m);
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5)).ReturnsAsync(new List<int> { 10 });

        var request = new BulkScheduleRequest
        {
            StartDate = new DateOnly(2026, 4, 13),
            SiteId = 1,
            LineOverrides = [new LineOverride { TreatmentPlanLineId = 1, DiscountAmount = 10m }]
        };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Single(result.Sessions);
        var session = result.Sessions[0];
        Assert.Equal(48.75m, session.Amount); // $65 * 45/60
        Assert.Equal(10m, session.DiscountAmount);
        Assert.Equal(15.50m, session.ProviderAmount); // (48.75 - 10) * 0.40 = 15.50
        Assert.Equal(23.25m, session.GrossProfit); // 38.75 - 15.50
    }

    [Fact]
    public async Task ScheduleAsync_FlatFeeTherapist_UsesFixedProviderAmount()
    {
        var plan = CreateActivePlan(weeks: 1, frequency: 1);
        SetupCommonMocks(plan);
        SetupTherapist(10, "Dr.", "Smith", feeFlat: 25m);
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5)).ReturnsAsync(new List<int> { 10 });

        var request = new BulkScheduleRequest { StartDate = new DateOnly(2026, 4, 13), SiteId = 1 };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Single(result.Sessions);
        Assert.Equal(25m, result.Sessions[0].ProviderAmount);
        Assert.Equal(40m, result.Sessions[0].GrossProfit); // 65 - 0 - 25
    }

    [Fact]
    public async Task ScheduleAsync_ConflictDetection_ReportsConflictWithAlternatives()
    {
        var plan = CreateActivePlan(weeks: 1, frequency: 1);
        SetupCommonMocks(plan);
        SetupTherapist(10, "Dr.", "Smith", feePct: 0.40m);
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5)).ReturnsAsync(new List<int> { 10 });

        // Therapist 10 is already booked on the target date/time
        _mockSessionRepo.Setup(r => r.GetByTherapistAndDateRangeAsync(10, It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<TherapySession>
            {
                new()
                {
                    TherapistId = 10,
                    SessionDate = new DateOnly(2026, 4, 13),
                    SessionTime = new TimeOnly(10, 0),
                    Duration = 60,
                    AppointmentStatusId = 2 // Confirmed
                }
            });

        var request = new BulkScheduleRequest { StartDate = new DateOnly(2026, 4, 13), SiteId = 1 };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Equal(0, result.SessionsCreated);
        Assert.Single(result.Conflicts);
        Assert.Contains("already booked", result.Conflicts[0].Reason);
    }

    [Fact]
    public async Task ScheduleAsync_AutoAssign_PrefersPatientHistoryTherapist()
    {
        var plan = CreateActivePlan(weeks: 1, frequency: 1);
        plan.Lines.First().PreferredTherapistId = null; // No preferred
        SetupCommonMocks(plan);

        // Patient has history with therapist 20
        _mockSessionRepo.Setup(r => r.GetTherapistIdsForPatientAsync(1))
            .ReturnsAsync(new List<int> { 20 });

        // Both therapists 10 and 20 are qualified
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5))
            .ReturnsAsync(new List<int> { 10, 20 });

        var therapists = new List<Therapist>
        {
            new() { Id = 10, TherapistId = 10, FeePctPerSession = 0.40m, User = new User { FirstName = "Dr.", LastName = "New" } },
            new() { Id = 20, TherapistId = 20, FeePctPerSession = 0.35m, User = new User { FirstName = "Dr.", LastName = "Familiar" } }
        };
        _mockTherapistRepo.Setup(r => r.GetByIdsWithUserAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(therapists);

        var request = new BulkScheduleRequest { StartDate = new DateOnly(2026, 4, 13), SiteId = 1 };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Single(result.Sessions);
        Assert.Equal(20, result.Sessions[0].TherapistId); // History therapist preferred
    }

    [Fact]
    public async Task ScheduleAsync_RejectsNonActivePlan()
    {
        var plan = CreateActivePlan();
        plan.PlanStatus = "Draft";
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);

        var request = new BulkScheduleRequest { StartDate = new DateOnly(2026, 4, 13), SiteId = 1 };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.ScheduleAsync(1, request));
        Assert.Contains("must be Active", ex.Message);
    }

    [Fact]
    public async Task ScheduleAsync_RejectsIfAlreadyHasSessions()
    {
        var plan = CreateActivePlan();
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);
        _mockSessionRepo.Setup(r => r.HasSessionsForPlanAsync(1)).ReturnsAsync(true);

        var request = new BulkScheduleRequest { StartDate = new DateOnly(2026, 4, 13), SiteId = 1 };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.ScheduleAsync(1, request));
        Assert.Contains("already has scheduled sessions", ex.Message);
    }

    [Fact]
    public async Task ScheduleAsync_LineOverride_OverridesTherapistAndDiscount()
    {
        var plan = CreateActivePlan(weeks: 1, frequency: 1);
        SetupCommonMocks(plan);

        var therapists = new List<Therapist>
        {
            new() { Id = 10, TherapistId = 10, FeePctPerSession = 0.40m, User = new User { FirstName = "Dr.", LastName = "Smith" } },
            new() { Id = 20, TherapistId = 20, FeePerSession = 30, User = new User { FirstName = "Dr.", LastName = "Override" } }
        };
        _mockTherapistRepo.Setup(r => r.GetByIdsWithUserAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(therapists);
        _mockSpecialtyRepo.Setup(r => r.GetTherapistIdsBySpecialtyAsync(5)).ReturnsAsync(new List<int> { 10, 20 });

        var request = new BulkScheduleRequest
        {
            StartDate = new DateOnly(2026, 4, 13),
            SiteId = 1,
            LineOverrides = [new LineOverride { TreatmentPlanLineId = 1, TherapistId = 20, DiscountAmount = 5m }]
        };

        var result = await _sut.ScheduleAsync(1, request);

        Assert.Single(result.Sessions);
        Assert.Equal(20, result.Sessions[0].TherapistId);
        Assert.Equal(5m, result.Sessions[0].DiscountAmount);
        Assert.Equal(30m, result.Sessions[0].ProviderAmount); // Flat fee
    }

    [Fact]
    public async Task GetProgressAsync_ComputesCorrectSummary()
    {
        var plan = CreateActivePlan(weeks: 4, frequency: 1);
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);

        var sessions = new List<TherapySession>
        {
            new() { Id = 1, SessionDate = new DateOnly(2026, 4, 13), SessionTime = new TimeOnly(10, 0),
                    AppointmentStatusId = 4, TreatmentPlanLineId = 1,
                    Therapist = new Therapist { User = new User { FirstName = "Dr.", LastName = "S" } },
                    SpecialtyType = new SpecialtyType { Abbreviation = "FS" },
                    AppointmentStatus = new AppointmentStatus { Name = "Completed" } },
            new() { Id = 2, SessionDate = new DateOnly(2026, 4, 20), SessionTime = new TimeOnly(10, 0),
                    AppointmentStatusId = 1, TreatmentPlanLineId = 1,
                    Therapist = new Therapist { User = new User { FirstName = "Dr.", LastName = "S" } },
                    SpecialtyType = new SpecialtyType { Abbreviation = "FS" },
                    AppointmentStatus = new AppointmentStatus { Name = "Proposed" } },
            new() { Id = 3, SessionDate = new DateOnly(2026, 4, 27), SessionTime = new TimeOnly(10, 0),
                    AppointmentStatusId = 3, TreatmentPlanLineId = 1,
                    Therapist = new Therapist { User = new User { FirstName = "Dr.", LastName = "S" } },
                    SpecialtyType = new SpecialtyType { Abbreviation = "FS" },
                    AppointmentStatus = new AppointmentStatus { Name = "Cancelled" } },
        };
        _mockSessionRepo.Setup(r => r.GetByTreatmentPlanIdAsync(1)).ReturnsAsync(sessions);

        var progress = await _sut.GetProgressAsync(1);

        Assert.Equal(4, progress.TotalPlanned); // 4 weeks * 1 freq
        Assert.Equal(3, progress.SessionsCreated);
        Assert.Equal(1, progress.SessionsCompleted);
        Assert.Equal(1, progress.SessionsCancelled);
        Assert.Equal(1, progress.SessionsRemaining); // Proposed
    }
}
