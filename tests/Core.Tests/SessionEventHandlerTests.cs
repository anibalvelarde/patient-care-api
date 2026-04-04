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
    private readonly SessionEventHandler _sut;

    public SessionEventHandlerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SessionEventHandler>>();
        _mockRepository = new Mock<ISessionEventRepository>();
        _mockTherapySessionRepository = new Mock<ITherapySessionRepository>();
        _mockPatientService = new Mock<IPatientProfileService>();
        _mockTherapistService = new Mock<ITherapistProfileService>();
        _mockSpecialtyTypeRepository = new Mock<IRepository<SpecialtyType>>();
        _sut = new SessionEventHandler(
            fakeLogger,
            _mockRepository.Object,
            _mockTherapySessionRepository.Object,
            _mockTherapistService.Object,
            _mockPatientService.Object,
            _mockSpecialtyTypeRepository.Object);
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
        _mockPatientService
            .Setup(s => s.GetByIdAsync(1))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "Patient One" });
        _mockPatientService
            .Setup(s => s.GetByIdAsync(2))
            .ReturnsAsync(new PatientProfile { PatientId = 2, PatientName = "Patient Two" });

        // Act
        var result = (await _sut.GetAllPatientsPastDueAsync()).ToList();

        // Assert
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
            .Setup(s => s.GetByIdAsync(99))
            .ReturnsAsync((PatientProfile?)null);

        // Act
        var result = (await _sut.GetAllPatientsPastDueAsync()).ToList();

        // Assert
        Assert.Empty(result);
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
