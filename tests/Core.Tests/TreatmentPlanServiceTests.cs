using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;

namespace Core.Tests;

public class TreatmentPlanServiceTests
{
    private readonly Mock<ITreatmentPlanRepository> _mockPlanRepo;
    private readonly Mock<ITherapySessionRepository> _mockSessionRepo;
    private readonly Mock<IRepository<SpecialtyType>> _mockSpecialtyRepo;
    private readonly TreatmentPlanService _sut;

    public TreatmentPlanServiceTests()
    {
        var logger = Mock.Of<ILogger<TreatmentPlanService>>();
        _mockPlanRepo = new Mock<ITreatmentPlanRepository>();
        _mockSessionRepo = new Mock<ITherapySessionRepository>();
        _mockSpecialtyRepo = new Mock<IRepository<SpecialtyType>>();
        _sut = new TreatmentPlanService(logger, _mockPlanRepo.Object, _mockSessionRepo.Object, _mockSpecialtyRepo.Object);
    }

    private static TreatmentPlan CreatePlan(
        int id = 1,
        string firstName = "John",
        string lastName = "Doe",
        int weeklyFrequency = 2,
        DateTime? created = null)
    {
        return new TreatmentPlan
        {
            Id = id,
            PatientId = 10,
            PlanStatus = "Active",
            WeeklyFrequency = weeklyFrequency,
            DurationWeeks = 8,
            DiscoverySessionId = 100,
            CreatedByTherapistId = 5,
            CreatedTimestamp = created ?? new DateTime(2026, 4, 7, 14, 30, 0),
            LastUpdatedTimestamp = created ?? new DateTime(2026, 4, 7, 14, 30, 0),
            Patient = new Patient
            {
                Id = 10,
                User = new User { FirstName = firstName, LastName = lastName }
            },
            DiscoverySession = new TherapySession
            {
                SessionDate = new DateOnly(2026, 4, 1),
                SpecialtyType = new SpecialtyType { Abbreviation = "Eval-SP" }
            },
            CreatedByTherapist = new Therapist
            {
                Id = 5,
                User = new User { FirstName = "Dr.", LastName = "Smith" }
            },
            Lines = []
        };
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsDisplayTitle_WithCorrectFormat()
    {
        var plan = CreatePlan(id: 4, firstName: "John", lastName: "Doe", weeklyFrequency: 2,
            created: new DateTime(2026, 4, 7));
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(4)).ReturnsAsync(plan);

        var result = await _sut.GetByIdAsync(4);

        Assert.NotNull(result);
        Assert.Equal("TP | Doe, J. | 2026-04-07 | 2x/week", result!.DisplayTitle);
    }

    [Fact]
    public async Task GetByIdAsync_DisplayTitle_SingleFrequency()
    {
        var plan = CreatePlan(weeklyFrequency: 1);
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);

        var result = await _sut.GetByIdAsync(1);

        Assert.Contains("1x/week", result!.DisplayTitle);
    }

    [Fact]
    public async Task GetByPatientIdAsync_DisplayTitle_IncludesLastNameAndInitial()
    {
        var plan = CreatePlan(firstName: "Maria", lastName: "Garcia");
        _mockPlanRepo.Setup(r => r.GetByPatientIdAsync(10))
            .ReturnsAsync(new List<TreatmentPlan> { plan });

        var results = await _sut.GetByPatientIdAsync(10);

        Assert.Single(results);
        Assert.Contains("Garcia, M.", results[0].DisplayTitle);
    }

    [Fact]
    public async Task GetByIdAsync_DisplayTitle_NullPatient_ShowsQuestionMark()
    {
        var plan = CreatePlan();
        plan.Patient = null;
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);

        var result = await _sut.GetByIdAsync(1);

        Assert.Contains("?, ?", result!.DisplayTitle);
    }

    [Fact]
    public async Task GetByIdAsync_DisplayTitle_EmptyFirstName_ShowsQuestionMark()
    {
        var plan = CreatePlan(firstName: "");
        _mockPlanRepo.Setup(r => r.GetByIdWithLinesAsync(1)).ReturnsAsync(plan);

        var result = await _sut.GetByIdAsync(1);

        Assert.Contains("Doe, ?", result!.DisplayTitle);
    }
}
