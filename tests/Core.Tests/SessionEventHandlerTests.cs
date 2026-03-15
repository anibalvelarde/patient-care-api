using Moq;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Core.Tests;

public class SessionEventHandlerTests
{
    private readonly Mock<ISessionEventRepository> _mockRepository;
    private readonly Mock<ITherapySessionRepository> _mockTherapySessionRepository;
    private readonly Mock<IPatientProfileService> _mockPatientService;
    private readonly Mock<ITherapistProfileService> _mockTherapistService;
    private readonly SessionEventHandler _sut;

    public SessionEventHandlerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SessionEventHandler>>();
        _mockRepository = new Mock<ISessionEventRepository>();
        _mockTherapySessionRepository = new Mock<ITherapySessionRepository>();
        _mockPatientService = new Mock<IPatientProfileService>();
        _mockTherapistService = new Mock<ITherapistProfileService>();
        _sut = new SessionEventHandler(
            fakeLogger,
            _mockRepository.Object,
            _mockTherapySessionRepository.Object,
            _mockTherapistService.Object,
            _mockPatientService.Object);
    }

    [Fact]
    public async Task GetAllPatientsPastDueAsync_GroupsByPatient_AndAggregatesCorrectly()
    {
        // Arrange
        var pastDueSessions = new List<SessionEvent>
        {
            new() { SessionId = 1, PatientId = 1, AmountDue = 50m, AmountPaid = 10m, IsPastDue = true },
            new() { SessionId = 2, PatientId = 1, AmountDue = 30m, AmountPaid = 5m, IsPastDue = true },
            new() { SessionId = 3, PatientId = 2, AmountDue = 100m, AmountPaid = 0m, IsPastDue = true }
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
        Assert.Equal(80m, patient1.PastDueTotalAmount);
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
}
