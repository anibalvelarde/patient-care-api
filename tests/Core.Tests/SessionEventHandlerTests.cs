using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
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
    private readonly Mock<ISpecialtyPriceService> _mockPriceService;
    private readonly Mock<IRepository<Site>> _mockSiteRepository;
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
        _mockPriceService = new Mock<ISpecialtyPriceService>();
        _mockSiteRepository = new Mock<IRepository<Site>>();
        // WP-23 (F10): default = patient HAS a caretaker link so pre-existing tests book freely.
        _mockPatientCaretakerRepository
            .Setup(r => r.GetByPatientIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<PatientCaretaker> { new() { PatientId = 1, CaretakerId = 1 } });
        // WP-40 default: every specialty+duration resolves to a 100.00 sheet row, so
        // pre-existing booking tests derive Amount = 100 (what they used to send as request.Amount).
        _mockPriceService
            .Setup(s => s.ResolvePriceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(100m, AmountSource.DurationPrice));
        _sut = new SessionEventHandler(
            fakeLogger,
            _mockRepository.Object,
            _mockTherapySessionRepository.Object,
            _mockTherapistService.Object,
            _mockPatientService.Object,
            _mockSpecialtyTypeRepository.Object,
            _mockTherapistSpecialtyRepository.Object,
            _mockPatientCaretakerRepository.Object,
            _mockPriceService.Object,
            _mockSiteRepository.Object);
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
    public async Task CreateAsync_WhenNoSpecialtyResolvable_Throws()
    {
        // WP-40 (BK-2): the price sheet is the only Amount source, so a booking with no
        // resolvable specialty has nothing to price against — hard 400.
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

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Contains("specialty is required", ex.Message);
        _mockTherapySessionRepository.Verify(r => r.AddAsync(It.IsAny<TherapySession>()), Times.Never);
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
        // WP-40: bookings need a resolvable specialty (the price sheet keys off it).
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetByIdAsync(5))
            .ReturnsAsync(new SpecialtyType { Id = 5, Abbreviation = "TC", Name = "Conduct Therapy", IsDiscovery = false });
    }

    // WP-40: `amount` seeds the PRICE SHEET (the derived Amount); `discount` is the client's
    // junk value — the server ignores it and derives (exact-20% SENADIS / 0).
    private SessionEventRequest MakeRequest(decimal amount, decimal discount)
    {
        _mockPriceService
            .Setup(s => s.ResolvePriceAsync(5, 60, It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(amount, AmountSource.DurationPrice));
        return new()
        {
            PatientId = 1, TherapistId = 1, SessionDate = DateOnly.Parse("2026-07-20"),
            SessionTime = TimeOnly.Parse("10:00"), Amount = amount, Discount = discount, Duration = 60,
            SpecialtyTypeId = 5
        };
    }

    [Fact]
    public async Task CreateAsync_PercentFee_ComputesProviderAmountOnNet()
    {
        // Questionnaire E ruling (owner 2026-07-12): fee applies AFTER discounts — net, not
        // gross. WP-40: the 20-discount now arrives via the derived SENADIS exact-20%.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 0m));

        Assert.NotNull(_capturedSession);
        Assert.Equal(20m, _capturedSession!.DiscountAmount);  // derived exact-20%
        Assert.Equal(40m, _capturedSession.ProviderAmount);   // 0.50 × (100 − 20)
        Assert.Equal(40m, _capturedSession.GrossProfit);      // 80 − 40
    }

    [Fact]
    public async Task CreateAsync_FlatFee_GrossProfitIsNetMinusFlatFee()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 25m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 0m));

        Assert.Equal(25m, _capturedSession!.ProviderAmount);  // flat fee unchanged by net
        Assert.Equal(55m, _capturedSession.GrossProfit);      // (100 − 20) − 25
    }

    [Theory]
    [InlineData(100, 5, 20)]    // client junk below 20% → derived exactly 20
    [InlineData(100, 20, 20)]   // client echoes the derived value → 20
    [InlineData(100, 30, 20)]   // WP-40 (G2): booking-time staff discretion is GONE — exactly 20
    [InlineData(100, 0, 20)]    // nothing requested → derived 20
    public async Task CreateAsync_SenadisPatient_DerivesExactTwentyPercent_ClientValueIgnored(
        decimal amount, decimal requested, decimal expected)
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        await _sut.CreateAsync(MakeRequest(amount, requested));

        Assert.Equal(expected, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_SenadisDiscount_RoundsToTwoDecimals()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 33.33m, discount: 0m));

        Assert.Equal(6.67m, _capturedSession!.DiscountAmount); // round(0.20 × 33.33, 2)
    }

    [Fact]
    public async Task CreateAsync_UnflaggedPatient_DiscountDerivesToZero_ClientValueIgnored()
    {
        // WP-40 (G2): non-SENADIS bookings get exactly 0 — the client's 5 is junk.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = false },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(0m, _capturedSession!.DiscountAmount);
    }

    // ── WP-37 (SEN-1): expiry-aware SENADIS floor — G2: expiry is compared against the
    // SESSION date (not "today"); expired ⇒ NO floor at all; the flag is never auto-cleared. ──

    private static PatientProfile SenadisPatient(DateTime? expiry) => new()
    {
        PatientId = 1,
        PatientName = "P",
        HasSenadisDiscount = true,
        SenadisExpirationDate = expiry,
    };

    [Fact]
    public async Task CreateAsync_SenadisExpiredBeforeSessionDate_DiscountDerivesToZero()
    {
        // Session date is 2026-07-20 (MakeRequest); expiry the day before ⇒ expired for this
        // booking — derived discount is 0 (same as an unflagged patient; client junk ignored).
        SetupCreateAsyncWithCapture(
            SenadisPatient(new DateTime(2026, 7, 19)),
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(0m, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_SenadisExpiryOnSessionDate_FloorStillApplies()
    {
        // Boundary (G2): session date == expiry ⇒ still active — the floor applies.
        SetupCreateAsyncWithCapture(
            SenadisPatient(new DateTime(2026, 7, 20)),
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(20m, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_SenadisExpiryAfterSessionDate_FloorApplies()
    {
        SetupCreateAsyncWithCapture(
            SenadisPatient(new DateTime(2026, 12, 31)),
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(20m, _capturedSession!.DiscountAmount);
    }

    [Fact]
    public async Task CreateAsync_SenadisNullExpiry_FloorApplies()
    {
        // G1: NULL = no expiry (open-ended) — all backfill-era flags; floor keeps applying.
        SetupCreateAsyncWithCapture(
            SenadisPatient(expiry: null),
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });

        await _sut.CreateAsync(MakeRequest(amount: 100m, discount: 5m));

        Assert.Equal(20m, _capturedSession!.DiscountAmount);
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

    // ── WP-40 (BK-1/BK-2): fixed durations, derived Amount, G4 missing-price block,
    // on-site snapshot, and the BK-3 edit money rules ──

    [Theory]
    [InlineData(30)]
    [InlineData(40)] // 2026-07-27 addendum: real 40-min interview services exist
    [InlineData(120)]
    public async Task CreateAsync_BookableDuration_Creates(int duration)
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        request.Duration = duration;
        _mockPriceService
            .Setup(s => s.ResolvePriceAsync(5, duration, It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(100m, AmountSource.DurationPrice));

        await _sut.CreateAsync(request);

        Assert.Equal(duration, _capturedSession!.Duration);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(50)]
    [InlineData(75)]
    public async Task CreateAsync_NonBookableDuration_Throws(int duration)
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        request.Duration = duration;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Contains("not bookable", ex.Message);
        _mockTherapySessionRepository.Verify(r => r.AddAsync(It.IsAny<TherapySession>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NoPriceAndNoDefault_ThrowsG4BlockMessage()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        _mockPriceService
            .Setup(s => s.ResolvePriceAsync(5, 60, It.IsAny<DateOnly>()))
            .ReturnsAsync(PriceResolution.None);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Equal("No price configured for Conduct Therapy at 60 min — ask a manager to set it in Admin.", ex.Message);
        _mockTherapySessionRepository.Verify(r => r.AddAsync(It.IsAny<TherapySession>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DefaultAmountFallback_SignalsAmountSourceBadge()
    {
        // WP-39 G2 badge path: no duration row ⇒ DefaultAmount, and the response says so.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        _mockPriceService
            .Setup(s => s.ResolvePriceAsync(5, 60, It.IsAny<DateOnly>()))
            .ReturnsAsync(new PriceResolution(75m, AmountSource.DefaultAmount));

        var result = await _sut.CreateAsync(request);

        Assert.Equal(75m, _capturedSession!.Amount);
        Assert.Equal("defaultAmount", result.AmountSource);
    }

    [Fact]
    public async Task CreateAsync_JunkClientMoney_AllIgnored_ResponseCarriesDerived()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.40m });
        var request = MakeRequest(amount: 80m, discount: 0m); // sheet price = 80
        request.Amount = 999m;          // junk — ignored
        request.Discount = 50m;         // junk — ignored (derives to 0, unflagged)
        request.ProviderAmount = 77m;   // junk — always recomputed

        var result = await _sut.CreateAsync(request);

        Assert.Equal(80m, _capturedSession!.Amount);
        Assert.Equal(0m, _capturedSession.DiscountAmount);
        Assert.Equal(32m, _capturedSession.ProviderAmount); // 0.40 × 80
        Assert.Equal(48m, _capturedSession.GrossProfit);
        Assert.Equal(80m, result.Amount);
        Assert.Equal("durationPrice", result.AmountSource);
    }

    [Fact]
    public async Task CreateAsync_OnSiteVisit_SpecialtyNotOffered_Throws()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        request.IsOnSiteVisit = true; // specialty 5 has OfferedOnSite = false

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Contains("not offered", ex.Message);
    }

    private void SetupOnSiteSpecialty()
    {
        _mockSpecialtyTypeRepository
            .Setup(r => r.GetByIdAsync(5))
            .ReturnsAsync(new SpecialtyType { Id = 5, Abbreviation = "TC", Name = "Conduct Therapy", OfferedOnSite = true });
    }

    [Fact]
    public async Task CreateAsync_OnSiteVisit_WithoutSite_Throws()
    {
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 10m });
        SetupOnSiteSpecialty();
        var request = MakeRequest(amount: 100m, discount: 0m);
        request.IsOnSiteVisit = true;
        request.SiteId = null;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));

        Assert.Contains("requires a site", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_OnSiteVisit_SnapshotsCharge_ExcludedFromFee_IncludedInGrossProfit()
    {
        // WP-39 G4 ruling: fee base = net only; the trip charge adds to gross profit + billed total.
        SetupCreateAsyncWithCapture(
            new PatientProfile { PatientId = 1, PatientName = "P" },
            new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });
        SetupOnSiteSpecialty();
        _mockSiteRepository
            .Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new Site { Id = 3, SiteName = "Main", OnSiteTripChargeAmount = 25m });
        var request = MakeRequest(amount: 100m, discount: 0m);
        request.IsOnSiteVisit = true;
        request.SiteId = 3;

        var result = await _sut.CreateAsync(request);

        Assert.Equal(25m, _capturedSession!.OnSiteChargeAmount); // snapshot
        Assert.Equal(50m, _capturedSession.ProviderAmount);      // 0.50 × 100 — charge NOT in the base
        Assert.Equal(75m, _capturedSession.GrossProfit);         // (100 − 50) + 25
        Assert.Equal(25m, result.OnSiteChargeAmount);
        Assert.Equal(125m, result.AmountDue);                    // 100 − 0 + 25
    }

    // ── WP-40 (BK-3 + G1) — UpdateAsync: duration-change validation, floor, recompute ──

    private TherapySession SessionOnFile(
        decimal amount = 100m, decimal discount = 0m, int duration = 60,
        decimal providerAmount = 30m, decimal grossProfit = 70m, decimal? onSiteCharge = null)
    {
        var session = new TherapySession
        {
            Id = 77, PatientId = 1, TherapistId = 1,
            SessionDate = DateOnly.Parse("2026-07-20"),
            Duration = duration, Amount = amount, DiscountAmount = discount,
            ProviderAmount = providerAmount, GrossProfit = grossProfit,
            OnSiteChargeAmount = onSiteCharge,
        };
        _mockTherapySessionRepository.Setup(r => r.GetByIdAsync(77)).ReturnsAsync(session);
        _mockRepository
            .Setup(r => r.UpdateAsync(77, It.IsAny<SessionEventUpdateRequest>(), It.IsAny<SessionMoneyPatch?>()))
            .ReturnsAsync(new SessionEvent());
        return session;
    }

    private static SessionEventUpdateRequest MakeUpdate(decimal amount, decimal discount, int? duration = null) => new()
    {
        SessionTime = TimeOnly.Parse("10:00"), Amount = amount, Discount = discount, Duration = duration,
    };

    [Fact]
    public async Task UpdateAsync_DurationOmitted_KeepsStoredDuration_EvenLegacyOddValues()
    {
        SessionOnFile(duration: 50); // legacy 50-min session
        _mockPatientService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P" });

        var ok = await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 0m, duration: null));

        Assert.True(ok);
        _mockRepository.Verify(r => r.UpdateAsync(77,
            It.Is<SessionEventUpdateRequest>(u => u.Duration == null), It.IsAny<SessionMoneyPatch?>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_DurationChangedToNonBookable_Throws()
    {
        SessionOnFile(duration: 60);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 0m, duration: 75)));

        Assert.Contains("not bookable", ex.Message);
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<int>(), It.IsAny<SessionEventUpdateRequest>(), It.IsAny<SessionMoneyPatch?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_DurationEchoedUnchanged_NotValidated()
    {
        // A legacy 50-min session round-tripped by an old client must not 400.
        SessionOnFile(duration: 50);

        var ok = await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 0m, duration: 50));

        Assert.True(ok);
    }

    [Fact]
    public async Task UpdateAsync_MoneyUnchanged_NoPatch_StoredFeeUntouched()
    {
        // WP-29 discipline: an unrelated edit (notes/status) must not recompute stored money.
        SessionOnFile(amount: 100m, discount: 10m, providerAmount: 33.33m, grossProfit: 56.67m);

        var ok = await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 10m));

        Assert.True(ok);
        _mockRepository.Verify(r => r.UpdateAsync(77, It.IsAny<SessionEventUpdateRequest>(), null), Times.Once);
        _mockTherapistService.Verify(s => s.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_DiscountChanged_RecomputesFeeAndGrossProfit()
    {
        SessionOnFile(amount: 100m, discount: 0m);
        _mockPatientService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P" });
        _mockTherapistService.Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        var ok = await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 30m));

        Assert.True(ok);
        _mockRepository.Verify(r => r.UpdateAsync(77, It.IsAny<SessionEventUpdateRequest>(),
            It.Is<SessionMoneyPatch>(p => p.ProviderAmount == 35m && p.GrossProfit == 35m)), Times.Once); // net 70
    }

    [Fact]
    public async Task UpdateAsync_DiscountChanged_OnSiteSession_ChargeStaysOutOfFeeInsideGrossProfit()
    {
        SessionOnFile(amount: 100m, discount: 0m, onSiteCharge: 25m);
        _mockPatientService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P" });
        _mockTherapistService.Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePctPerSession = 0.50m });

        await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 30m));

        _mockRepository.Verify(r => r.UpdateAsync(77, It.IsAny<SessionEventUpdateRequest>(),
            It.Is<SessionMoneyPatch>(p => p.ProviderAmount == 35m && p.GrossProfit == 60m)), Times.Once); // 35 + 25 charge
    }

    [Fact]
    public async Task UpdateAsync_ActiveSenadis_DiscountBelowFloor_Throws()
    {
        // BK-3: gated generosity goes UP from the floor, never below.
        SessionOnFile(amount: 100m, discount: 20m);
        _mockPatientService.Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true });

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 10m)));

        Assert.Contains("SENADIS floor", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_ActiveSenadis_DiscountRaisedAboveFloor_AllowedAndRecomputed()
    {
        SessionOnFile(amount: 100m, discount: 20m);
        _mockPatientService.Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P", HasSenadisDiscount = true });
        _mockTherapistService.Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "T", FeePerSession = 25m });

        var ok = await _sut.UpdateAsync(77, MakeUpdate(amount: 100m, discount: 40m));

        Assert.True(ok);
        _mockRepository.Verify(r => r.UpdateAsync(77, It.IsAny<SessionEventUpdateRequest>(),
            It.Is<SessionMoneyPatch>(p => p.ProviderAmount == 25m && p.GrossProfit == 35m)), Times.Once); // 60 − 25
    }

    [Fact]
    public async Task UpdateAsync_NonSenadis_DiscountAboveAmount_Throws()
    {
        SessionOnFile(amount: 50m, discount: 0m);
        _mockPatientService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "P" });

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateAsync(77, MakeUpdate(amount: 50m, discount: 60m)));

        Assert.Contains("between 0 and the session amount", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_UnknownSession_ReturnsFalse()
    {
        _mockTherapySessionRepository.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((TherapySession?)null);

        var ok = await _sut.UpdateAsync(999, MakeUpdate(amount: 100m, discount: 0m));

        Assert.False(ok);
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
