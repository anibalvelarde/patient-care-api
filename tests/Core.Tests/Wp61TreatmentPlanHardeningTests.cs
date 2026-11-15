using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

// WP-61 (E2E F-7 / F-8): a plan can no longer be saved in a state that can never be scheduled, an
// Active plan stays editable until its sessions are scheduled (lines updated IN PLACE so their Ids
// survive), and the creator is always the discovery session's therapist. Synthetic data only.
public class Wp61TreatmentPlanHardeningTests
{
    private const int PatientId = 10;
    private const int DiscoveryId = 100;
    private const int DiscoveryTherapistId = 7;
    private const int PricedSpecialty = 1;    // TL: 60-min price row
    private const int DefaultOnlySpecialty = 3; // DefaultAmount only
    private const int UnpricedSpecialty = 4;  // no row, no default
    private const int DiscoverySpecialty = 2;

    private readonly Mock<ITreatmentPlanRepository> _planRepo = new();
    private readonly Mock<ITherapySessionRepository> _sessionRepo = new();
    private readonly Mock<IRepository<SpecialtyType>> _specialtyRepo = new();
    private readonly Mock<ISpecialtyPriceService> _priceService = new();
    private readonly TreatmentPlanService _sut;
    private TreatmentPlan? _added;

    public Wp61TreatmentPlanHardeningTests()
    {
        _specialtyRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SpecialtyType>
        {
            new() { Id = PricedSpecialty, Abbreviation = "TL", Name = "Terapia de Lenguaje" },
            new() { Id = DiscoverySpecialty, Abbreviation = "Eval", Name = "Evaluacion", IsDiscovery = true },
            new() { Id = DefaultOnlySpecialty, Abbreviation = "TO", Name = "Terapia Ocupacional", DefaultAmount = 50m },
            new() { Id = UnpricedSpecialty, Abbreviation = "NP", Name = "Sin Precio" },
        });

        _priceService.Setup(p => p.ResolvePriceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(PriceResolution.None);
        _priceService.Setup(p => p.ResolvePriceAsync(PricedSpecialty, 60, It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(70m, AmountSource.DurationPrice));
        _priceService.Setup(p => p.ResolvePriceAsync(DefaultOnlySpecialty, It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(50m, AmountSource.DefaultAmount));

        _sessionRepo.Setup(r => r.GetByIdWithSpecialtyAsync(DiscoveryId)).ReturnsAsync(new TherapySession
        {
            Id = DiscoveryId,
            PatientId = PatientId,
            TherapistId = DiscoveryTherapistId,
            AppointmentStatusId = SessionStatus.Completed,
            SpecialtyType = new SpecialtyType { Id = DiscoverySpecialty, IsDiscovery = true },
        });

        _planRepo.Setup(r => r.AddAsync(It.IsAny<TreatmentPlan>()))
            .Callback<TreatmentPlan>(p => { p.Id = 55; _added = p; })
            .ReturnsAsync((TreatmentPlan p) => p);
        _planRepo.Setup(r => r.GetByIdWithLinesAsync(55)).ReturnsAsync(() => _added);

        _sut = new TreatmentPlanService(Mock.Of<ILogger<TreatmentPlanService>>(),
            _planRepo.Object, _sessionRepo.Object, _specialtyRepo.Object, _priceService.Object);
    }

    private static TreatmentPlanRequest Request(params TreatmentPlanLineRequest[] lines) => new()
    {
        PatientId = PatientId,
        DiscoverySessionId = DiscoveryId,
        WeeklyFrequency = lines.Length,
        DurationWeeks = 4,
        Lines = lines.ToList(),
    };

    private static TreatmentPlanLineRequest Line(int specialty = PricedSpecialty, int duration = 60, int? id = null)
        => new() { Id = id, SpecialtyTypeId = specialty, Duration = duration };

    private TreatmentPlan Existing(string status, params TreatmentPlanLine[] lines)
    {
        var plan = new TreatmentPlan
        {
            Id = 9,
            PatientId = PatientId,
            DiscoverySessionId = DiscoveryId,
            CreatedByTherapistId = DiscoveryTherapistId,
            PlanStatus = status,
            WeeklyFrequency = lines.Length,
            DurationWeeks = 4,
            Lines = lines.ToList(),
        };
        _planRepo.Setup(r => r.GetByIdWithLinesAsync(9)).ReturnsAsync(plan);
        return plan;
    }

    private static TreatmentPlanLine StoredLine(int id, int specialty = PricedSpecialty, int duration = 60)
        => new() { Id = id, TreatmentPlanId = 9, SpecialtyTypeId = specialty, Duration = duration };

    // ---------------- line validation (F-7) ----------------

    [Fact]
    public async Task Create_UnbookableDuration_Is400_AndNothingSaved()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(Line(duration: 40))));

        Assert.Contains("40 min is not bookable", ex.Message);
        _planRepo.Verify(r => r.AddAsync(It.IsAny<TreatmentPlan>()), Times.Never);
    }

    [Fact]
    public async Task Create_SpecialtyWithNoPriceAndNoDefault_Is400_NamingTheSpecialty()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(Request(Line(UnpricedSpecialty))));

        Assert.Contains("No price configured for Sin Precio at 60 min", ex.Message);
        _planRepo.Verify(r => r.AddAsync(It.IsAny<TreatmentPlan>()), Times.Never);
    }

    [Fact]
    public async Task Create_ReportsEveryBadLine_InOneMessage()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateAsync(Request(Line(duration: 40), Line(UnpricedSpecialty))));

        Assert.Contains("40 min is not bookable", ex.Message);
        Assert.Contains("Sin Precio", ex.Message);
    }

    [Fact]
    public async Task Create_DefaultPriceOnly_IsAccepted()
    {
        var result = await _sut.CreateAsync(Request(Line(DefaultOnlySpecialty, duration: 45)));

        Assert.Equal(55, result.Id);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 0)]
    public async Task Create_FrequencyOrWeeksBelowOne_Is400(int weekly, int weeks)
    {
        var request = Request(Line());
        request.WeeklyFrequency = weekly;
        request.DurationWeeks = weeks;

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    // ---------------- creator (F-8) ----------------

    [Fact]
    public async Task Create_CreatorIsTheDiscoveryTherapist_ClientValueIgnored()
    {
        var request = Request(Line());
        request.CreatedByTherapistId = 1; // the old UI's hardcoded fallback

        await _sut.CreateAsync(request);

        Assert.Equal(DiscoveryTherapistId, _added!.CreatedByTherapistId);
    }

    [Fact]
    public async Task Create_CreatorOmitted_StillTheDiscoveryTherapist()
    {
        await _sut.CreateAsync(Request(Line()));

        Assert.Equal(DiscoveryTherapistId, _added!.CreatedByTherapistId);
    }

    // ---------------- editing (ruling 1: Active editable until scheduled) ----------------

    [Fact]
    public async Task Update_ActivePlanWithoutSessions_UpdatesLinesInPlace_KeepingIds()
    {
        var plan = Existing(TreatmentPlan.PlanStatuses.Active, StoredLine(31));
        var original = plan.Lines.Single();
        _sessionRepo.Setup(r => r.HasSessionsForPlanAsync(9)).ReturnsAsync(false);

        await _sut.UpdateAsync(9, Request(Line(DefaultOnlySpecialty, duration: 45, id: 31)));

        var line = Assert.Single(plan.Lines);
        Assert.Same(original, line);
        Assert.Equal(31, line.Id);
        Assert.Equal(DefaultOnlySpecialty, line.SpecialtyTypeId);
        Assert.Equal(45, line.Duration);
        _planRepo.Verify(r => r.UpdateAsync(plan), Times.Once);
    }

    [Fact]
    public async Task Update_ActivePlanWithScheduledSessions_Is400()
    {
        Existing(TreatmentPlan.PlanStatuses.Active, StoredLine(31));
        _sessionRepo.Setup(r => r.HasSessionsForPlanAsync(9)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(9, Request(Line(id: 31))));

        Assert.Contains("already has scheduled sessions", ex.Message);
        _planRepo.Verify(r => r.UpdateAsync(It.IsAny<TreatmentPlan>()), Times.Never);
    }

    [Fact]
    public async Task Update_ActivePlan_LineCountMustStillMatchWeeklyFrequency()
    {
        Existing(TreatmentPlan.PlanStatuses.Active, StoredLine(31));
        _sessionRepo.Setup(r => r.HasSessionsForPlanAsync(9)).ReturnsAsync(false);
        var request = Request(Line(id: 31), Line());
        request.WeeklyFrequency = 1;

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(9, request));
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task Update_ClosedPlan_Is400(string status)
    {
        Existing(status, StoredLine(31));

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(9, Request(Line(id: 31))));
    }

    [Fact]
    public async Task Update_DraftPlan_AddsNewLines_AndRemovesMissingOnes()
    {
        var plan = Existing(TreatmentPlan.PlanStatuses.Draft, StoredLine(31), StoredLine(32));

        await _sut.UpdateAsync(9, Request(Line(id: 32, duration: 60), Line(DefaultOnlySpecialty, duration: 30)));

        Assert.Equal(2, plan.Lines.Count);
        Assert.Contains(plan.Lines, l => l.Id == 32);
        Assert.DoesNotContain(plan.Lines, l => l.Id == 31);
        Assert.Contains(plan.Lines, l => l.Id == 0 && l.SpecialtyTypeId == DefaultOnlySpecialty && l.Duration == 30);
    }

    [Fact]
    public async Task Update_LineIdFromAnotherPlan_Is400()
    {
        Existing(TreatmentPlan.PlanStatuses.Draft, StoredLine(31));

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateAsync(9, Request(Line(id: 999))));
    }

    [Fact]
    public async Task Update_RunsTheSameLineValidation()
    {
        Existing(TreatmentPlan.PlanStatuses.Draft, StoredLine(31));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateAsync(9, Request(Line(UnpricedSpecialty, id: 31))));

        Assert.Contains("No price configured", ex.Message);
    }

    [Fact]
    public async Task Activate_LineLostItsPrice_Is400()
    {
        Existing(TreatmentPlan.PlanStatuses.Draft, StoredLine(31, UnpricedSpecialty));
        _planRepo.Setup(r => r.GetByPatientIdAsync(PatientId)).ReturnsAsync(new List<TreatmentPlan>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.ActivateAsync(9));

        Assert.Contains("No price configured", ex.Message);
    }

    // ---------------- canEditLines (drives the UI's Edit button) ----------------

    [Theory]
    [InlineData("Draft", false, true)]
    [InlineData("Active", false, true)]
    [InlineData("Active", true, false)]
    [InlineData("Completed", false, false)]
    [InlineData("Cancelled", false, false)]
    public async Task GetById_CanEditLines(string status, bool hasSessions, bool expected)
    {
        Existing(status, StoredLine(31));
        _sessionRepo.Setup(r => r.HasSessionsForPlanAsync(9)).ReturnsAsync(hasSessions);

        var result = await _sut.GetByIdAsync(9);

        Assert.Equal(expected, result!.CanEditLines);
    }
}
