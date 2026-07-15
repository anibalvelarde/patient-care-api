using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Core.Tests;

public class SessionEventHandlerTests
{
    private readonly Mock<ISessionEventRepository> _mockRepository;
    private readonly Mock<ITherapySessionRepository> _mockTherapySessionRepository;
    private readonly Mock<IPatientProfileService> _mockPatientService;
    private readonly Mock<ITherapistProfileService> _mockTherapistService;
    private readonly Mock<IRepository<SpecialtyType>> _mockSpecialtyTypeRepository;
    private readonly Mock<ITherapistSpecialtyRepository> _mockTherapistSpecialtyRepository;
    private readonly Mock<IPatientCaretakerRepository> _mockPatientCaretakerRepository;
    private readonly SessionEventHandler _sut;

    public SessionEventHandlerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SessionEventHandler>>();
        _mockRepository = new Mock<ISessionEventRepository>();
        _mockTherapySessionRepository = new Mock<ITherapySessionRepository>();
        _mockPatientService = new Mock<IPatientProfileService>();
        _mockTherapistService = new Mock<ITherapistProfileService>();
        _mockSpecialtyTypeRepository = new Mock<IRepository<SpecialtyType>>();
        _mockTherapistSpecialtyRepository = new Mock<ITherapistSpecialtyRepository>();
        _mockPatientCaretakerRepository = new Mock<IPatientCaretakerRepository>();
        // WP-23 (F10): default = patient HAS a caretaker link so pre-existing tests book freely.
        _mockPatientCaretakerRepository
            .Setup(r => r.GetByPatientIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<PatientCaretaker> { new() { PatientId = 1, CaretakerId = 1 } });
        _sut = new SessionEventHandler(
            fakeLogger,
            _mockRepository.Object,
            _mockTherapySessionRepository.Object,
            _mockTherapistService.Object,
            _mockPatientService.Object,
            _mockSpecialtyTypeRepository.Object,
            _mockTherapistSpecialtyRepository.Object,
            _mockPatientCaretakerRepository.Object);
    }

    [Fact]
    public async Task GetAllPatientsPastDueAsync_GroupsByPatient_AndAggregatesCorrectly()
    {
        // Arrange
        var pastDueSessions = new List<SessionEvent>
        {
            new() { SessionId = 1, PatientId = 1, Amount = 60m, Discount = 0m, AmountDue = 50m, AmountPaid = 10m, IsPastDue = true },
            new() { SessionId = 2, PatientId = 1, Amount = 35m, Discount = 0m, AmountDue = 30m, AmountPaid = 5m, IsPastDue = true },
            new() { SessionId = 3, PatientId = 2, Amount = 100m, Discount = 0m, AmountDue = 100m, AmountPaid = 0m, IsPastDue = true }
        };
        _mockRepository
            .Setup(r => r.GetAllPastDueAsync())
            .ReturnsAsync(pastDueSessions);
        // WP-29: the handler batches profile lookups — one GetByIdsAsync call, never per-id.
        _mockPatientService
            .Setup(s => s.GetByIdsAsync(It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync(new List<PatientProfile>
            {
                new() { PatientId = 1, PatientName = "Patient One" },
                new() { PatientId = 2, PatientName = "Patient Two" },
            });

        // Act
        var result = (await _sut.GetAllPatientsPastDueAsync()).ToList();

        // Assert
        _mockPatientService.Verify(s => s.GetByIdsAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2))), Times.Once);
        _mockPatientService.Verify(s => s.GetByIdAsync(It.IsAny<int>()), Times.Never);
        Assert.Equal(2, result.Count);

        var patient1 = result.First(r => r.Party is PatientProfile p && p.PatientId == 1);
        Assert.Equal(2, patient1.PastDueSessions);
        Assert.Equal(95m, patient1.PastDueTotalAmount);
        Assert.Equal(15m, patient1.AmountPaidSoFar);
        Assert.Equal(2, patient1.Delinquency!.Count());

        var patient2 = result.First(r => r.Party is PatientProfile p && p.PatientId == 2);
        Assert.Equal(1, patient2.PastDueSessions);
        Assert.Equal(100m, patient2.PastDueTotalAmount);
        Assert.Equal(0m, patient2.AmountPaidSoFar);
    }

    [Fact]
    public async Task GetAllPatientsPastDueAsync_SkipsPatient_WhenProfileNotFound()
    {
        // Arrange
        var pastDueSessions = new List<SessionEvent>
        {
            new() { SessionId = 1, PatientId = 99, AmountDue = 50m, IsPastDue = true }
        };
        _mockRepository
            .Setup(r => r.GetAllPastDueAsync())
            .ReturnsAsync(pastDueSessions);
        _mockPatientService
            .Setup(s => s.GetByIdsAsync(It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync(new List<PatientProfile>());

        // Act
        var result = (await _sut.GetAllPatientsPastDueAsync()).ToList();

        // Assert
        Assert.Empty(result);
    }

    // ── WP-29 (U3): party-scoped past-due + the exact boundary filter ─────────────────

    [Fact]
    public async Task GetPastDueByPatientAsync_UsesScopedRepositoryCall_AndFiltersExactly()
    {
        // The repository's SQL cutoff is date-only (a superset): a boundary row can come back
        // with IsPastDue == false and must be dropped by the handler's exact filter.
        var candidates = new List<SessionEvent>
        {
            new() { SessionId = 1, PatientId = 7, SessionDate = new DateOnly(2026, 1, 10), IsPastDue = true },
            new() { SessionId = 2, PatientId = 7, SessionDate = new DateOnly(2026, 6, 8), IsPastDue = false }, // boundary row
            new() { SessionId = 3, PatientId = 7, SessionDate = new DateOnly(2026, 3, 1), IsPastDue = true },
        };
        _mockRepository
            .Setup(r => r.GetAllPastDueAsync(7, null))
            .ReturnsAsync(candidates);

        var result = (await _sut.GetPastDueByPatientAsync(7)).ToList();

        // Exact filter applied, newest first — identical to the pre-WP-29 output shape.
        Assert.Equal(new[] { 3, 1 }, result.Select(s => s.SessionId).ToArray());
        _mockRepository.Verify(r => r.GetAllPastDueAsync(7, null), Times.Once);
        _mockRepository.Verify(r => r.GetAllPastDueAsync(), Times.Never);
    }

    [Fact]
    public async Task GetPastDueByTherapistAsync_UsesScopedRepositoryCall()
    {
        _mockRepository
            .Setup(r => r.GetAllPastDueAsync(null, 4))
            .ReturnsAsync(new List<SessionEvent>
            {
                new() { SessionId = 9, TherapistId = 4, SessionDate = new DateOnly(2026, 2, 2), IsPastDue = true },
            });

        var result = (await _sut.GetPastDueByTherapistAsync(4)).ToList();

        Assert.Equal(9, Assert.Single(result).SessionId);
        _mockRepository.Verify(r => r.GetAllPastDueAsync(null, 4), Times.Once);
    }

    [Fact]
    public async Task GetAllPatientsPastDueAsync_ReturnsEmpty_WhenNoPastDueSessions()
    {
        // Arrange
        _mockRepository
            .Setup(r => r.GetAllPastDueAsync())
            .ReturnsAsync(new List<SessionEvent>());

        // Act
        var result = (await _sut.GetAllPatientsPastDueAsync()).ToList();

        // Assert
        Assert.Empty(result);
    }

    // --- Specialty Resolution Tests (WP-9B) ---

    private void SetupCreateAsyncDependencies()
    {
        _mockPatientService
            .Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "Test Patient" });
        _mockTherapistService
            .Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "Test Therapist", FeePerSession = 25m });
        _mockTherapySessionRepository
            .Setup(r => r.AddAsync(It.IsAny<TherapySession>()))
            .ReturnsAsync((TherapySession ts) => ts);
        _mockTherapySessionRepository
            .Setup(r => r.HasCompletedDiscoveryAsync(It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockTherapistSpecialtyRepository
            .Setup(r => r.HasTherapistSpecialtyAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task CreateAsync_WhenSpecialtyTypeIdProvided_ResolvesSpecialtyAndBackfillsTherapyTypes()
    {
        // Arrange
        SetupCreateAsyncDependencies();
        var specialty = new SpecialtyType { Id = 5, Abbreviation = "TC", Name = "Conduct Therapy", IsDiscovery = false };
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetByIdAsync(5))
            .ReturnsAsync(specialty);

        var request = new SessionEventRequest
        {
            PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-04-04"),
            SessionTime = TimeOnly.Parse("10:00"), Amount = 100m, Duration = 60,
            SpecialtyTypeId = 5, TherapyType = "N/A"
        };

        // Act
        var result = await _sut.CreateAsync(request);

        // Assert
        Assert.Equal(5, result.SpecialtyTypeId);
        Assert.Equal("TC", result.SpecialtyAbbreviation);
        Assert.Equal("Conduct Therapy", result.SpecialtyName);
        Assert.False(result.IsDiscovery);
        Assert.Equal("TC", result.TherapyTypes); // backfilled from specialty abbreviation
    }

    [Fact]
    public async Task CreateAsync_WhenFreeTextTherapyType_ResolvesSpecialtyTypeId()
    {
        // Arrange
        SetupCreateAsyncDependencies();
        var specialties = new List<SpecialtyType>
        {
            new() { Id = 2, Abbreviation = "FS", Name = "Physiotherapy", IsDiscovery = false },
            new() { Id = 6, Abbreviation = "TL", Name = "Language Therapy", IsDiscovery = false },
        };
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(specialties);

        var request = new SessionEventRequest
        {
            PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-04-04"),
            SessionTime = TimeOnly.Parse("10:00"), Amount = 100m, Duration = 60,
            TherapyType = "FS" // free-text, no SpecialtyTypeId
        };

        // Act
        var result = await _sut.CreateAsync(request);

        // Assert
        Assert.Equal(2, result.SpecialtyTypeId);
        Assert.Equal("FS", result.SpecialtyAbbreviation);
        Assert.Equal("Physiotherapy", result.SpecialtyName);
        Assert.False(result.IsDiscovery);
    }

    [Fact]
    public async Task CreateAsync_WhenNoSpecialtyInfo_TherapyTypesUsedAsIs()
    {
        // Arrange
        SetupCreateAsyncDependencies();
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<SpecialtyType>());

        var request = new SessionEventRequest
        {
            PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-04-04"),
            SessionTime = TimeOnly.Parse("10:00"), Amount = 100m, Duration = 60,
            TherapyType = "N/A" // default, no match expected
        };

        // Act
        var result = await _sut.CreateAsync(request);

        // Assert
        Assert.Null(result.SpecialtyTypeId);
        Assert.Null(result.SpecialtyAbbreviation);
        Assert.Null(result.IsDiscovery);
        Assert.Equal("N/A", result.TherapyTypes);
    }

    // --- WP-23: fee-on-net + SENADIS floor + caretaker guard (F7/F10, Questionnaire E) ---

    private TherapySession? _capturedSession;

    private void SetupCreateAsyncWithCapture(PatientProfile patient, TherapistProfile therapist)
    {
        _mockPatientService.Setup(s => s.GetByIdAsync(patient.PatientId)).ReturnsAsync(patient);
        _mockTherapistService.Setup(s => s.GetByIdAsync(therapist.TherapistId)).ReturnsAsync(therapist);
        _mockTherapySessionRepository
            .Setup(r => r.AddAsync(It.IsAny<TherapySession>()))
            .Callback<TherapySession>(ts => _capturedSession = ts)
            .ReturnsAsync((TherapySession ts) => ts);
        _mockTherapySessionRepository
            .Setup(r => r.HasCompletedDiscoveryAsync(It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockTherapistSpecialtyRepository
            .Setup(r => r.HasTherapistSpecialtyAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(true);
        _mockSpecialtyTypeRepository.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<SpecialtyType>());
    }

    private static SessionEventRequest MakeRequest(decimal amount, decimal discount) => new()
    {
        PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-07-20"),
        SessionTime = TimeOnly.Parse("10:00"), Amount = amount, Discount = discount, Duration = 60,
        TherapyType = "N/A"
    };

    [Fact]
    public async Task CreateAsync_PercentFee_ComputesProviderAmountOnNet()
    {
        // Questionnaire E ruling (owner 2026-07-12): fee applies AFTER discounts — net, not gross.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 20m));

        Assert.NotNull(_capturedSession);
        Assert.Equal(40m, _capturedSession!.ProviderAmount);  // 0.50 × (100 − 20), was 50 on gross
        Assert.Equal(40m, _capturedSession.GrossProfit);      // 80 − 40
    }

    [Fact]
    public async Task CreateAsync_FlatFee_GrossProfitIsNetMinusFlatFee()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 25m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 20m));

        Assert.Equal(25m, _capturedSession!.ProviderAmount);  // flat fee unchanged by net
        Assert.Equal(55m, _capturedSession.GrossProfit);      // (100 − 20) − 25
    }

    [Theory]
    [InlineData(100, 5, 20)]    // below the floor → raised to 20% of amount
    [InlineData(100, 20, 20)]   // exactly at the floor → unchanged
    [InlineData(100, 30, 30)]   // above the floor → staff discretion kept
    [InlineData(100, 0, 20)]    // no discount requested → floor applies
    public async Task CreateAsync_SenadisPatient_FloorsDiscountAtTwentyPercent(
        decimal amount, decimal requested, decimal expected)
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        await _sut.CreateAsync(MakeRequest(amount, requested));

        Assert.Equal(expected, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_SenadisFloor_RoundsToTwoDecimals()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 33.33m, discount: 0m));

        Assert.Equal(6.67m, _capturedSession!.DiscountAmount); // round(0.20 × 33.33, 2)
    }

    [Fact]
    public async Task CreateAsync_UnflaggedPatient_DiscountUntouched()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = false },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(5m, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_PatientWithoutCaretaker_ThrowsArgumentException()
    {
        // WP-23 (F10): hard block — GlobalExceptionHandler maps ArgumentException → 400.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        _mockPatientCaretakerRepository
            .Setup(r => r.GetByPatientIdAsync(1))
            .ReturnsAsync(new List<PatientCaretaker>());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(MakeRequest(amount: 100m, discount: 0m)));

        Assert.Contains("caretaker must be linked", ex.Message);
        _mockTherapySessionRepository.Verify(r => r.AddAsync(It.IsAny<TherapySession>()), Times.Never);
    }

    // --- WP-24 (F3/F4): discovery-first waiver — the completed-discovery check only runs
    // when the patient's RequiresDiscovery flag is true (false = waived, e.g. legacy import) ---

    private void SetupWaiverScenario(bool requiresDiscovery, bool hasCompletedDiscovery, SpecialtyType specialty)
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", RequiresDiscovery = requiresDiscovery },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        _mockTherapySessionRepository
            .Setup(r => r.HasCompletedDiscoveryAsync(It.IsAny<int>()))
            .ReturnsAsync(hasCompletedDiscovery);
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetByIdAsync(specialty.Id))
            .ReturnsAsync(specialty);
    }

    private static SessionEventRequest MakeSpecialtyRequest(int specialtyTypeId) => new()
    {
        PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-07-20"),
        SessionTime = TimeOnly.Parse("10:00"), Amount = 100m, Duration = 60,
        SpecialtyTypeId = specialtyTypeId
    };

    [Fact]
    public async Task CreateAsync_WaivedPatient_TreatmentSpecialty_NoCompletedDiscovery_Creates()
    {
        var treatment = new SpecialtyType { Id = 5, Abbreviation = "TC", Name = "Conduct Therapy", IsDiscovery = false };
        SetupWaiverScenario(requiresDiscovery: false, hasCompletedDiscovery: false, treatment);

        var result = await _sut.CreateAsync(MakeSpecialtyRequest(5));

        Assert.NotNull(_capturedSession);
        Assert.Equal(5, result.SpecialtyTypeId);
        // Waived patients skip the check entirely — the repository is never even asked.
        _mockTherapySessionRepository.Verify(r => r.HasCompletedDiscoveryAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnwaivedPatient_TreatmentSpecialty_NoCompletedDiscovery_Throws()
    {
        var treatment = new SpecialtyType { Id = 5, Abbreviation = "TC", Name = "Conduct Therapy", IsDiscovery = false };
        SetupWaiverScenario(requiresDiscovery: true, hasCompletedDiscovery: false, treatment);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.CreateAsync(MakeSpecialtyRequest(5)));

        Assert.Contains("requires a completed discovery", ex.Message);
        _mockTherapySessionRepository.Verify(r => r.AddAsync(It.IsAny<TherapySession>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DiscoverySpecialty_NeverTripsDiscoveryCheck_RegardlessOfFlag()
    {
        var discovery = new SpecialtyType { Id = 8, Abbreviation = "Obs-TO", Name = "Observacion TO", IsDiscovery = true };
        SetupWaiverScenario(requiresDiscovery: true, hasCompletedDiscovery: false, discovery);

        var result = await _sut.CreateAsync(MakeSpecialtyRequest(8));

        Assert.True(result.IsDiscovery);
        Assert.NotNull(_capturedSession);
    }

    [Fact]
    public void PatientProfileRequest_JsonWithoutRequiresDiscovery_DefaultsTrue()
    {
        // WP-24 (F3, Questionnaire C): an omitted JSON field must mean "requires discovery" —
        // the serializer keeps the property initializer when the field is absent.
        var json = """{"firstName": "New", "lastName": "Patient", "gender": "Other"}""";

        var request = System.Text.Json.JsonSerializer.Deserialize<PatientProfileRequest>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(request);
        Assert.True(request!.RequiresDiscovery);
    }

    [Fact]
    public async Task CreateAsync_WhenDiscoverySpecialtyProvided_IsDiscoveryIsTrue()
    {
        // Arrange
        SetupCreateAsyncDependencies();
        var discovery = new SpecialtyType { Id = 8, Abbreviation = "Obs-TO", Name = "Observacion TO", IsDiscovery = true };
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetByIdAsync(8))
            .ReturnsAsync(discovery);

        var request = new SessionEventRequest
        {
            PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-04-04"),
            SessionTime = TimeOnly.Parse("10:00"), Amount = 100m, Duration = 60,
            SpecialtyTypeId = 8
        };

        // Act
        var result = await _sut.CreateAsync(request);

        // Assert
        Assert.Equal(8, result.SpecialtyTypeId);
        Assert.True(result.IsDiscovery);
        Assert.Equal("Obs-TO", result.TherapyTypes);
    }
}
