using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using FluentAssertions;
using System.Linq;
using Castle.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class PatientsControllerTests
{
    private readonly Mock<IPatientProfileService> _mockPatientProfileService;
    private readonly Mock<IHandleSessionEvent> _mockSessionEventHandler;
    private readonly PatientsController _controller;

    public PatientsControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<PatientsController>>();
        _mockPatientProfileService = new Mock<IPatientProfileService>();
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>();
        _controller = new PatientsController(fakeLogger, _mockPatientProfileService.Object, _mockSessionEventHandler.Object);
    }

    [Fact]
    public async Task GetAllPatients_ReturnsOkResult_WithPatients()
    {
        // Arrange
        var mockPatients = new List<PatientProfile>
        {
            new() { /* properties */ },
            new() { /* properties */ }
        };
        _mockPatientProfileService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockPatients);

        // Act
        var result = await _controller.GetAllPatients();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPatients = Assert.IsType<List<PatientProfile>>(okResult.Value);
        Assert.Equal(mockPatients.Count, returnedPatients.Count);
    }

    [Fact]
    public async Task GetPatient_ReturnsNotFound_WhenPatientDoesNotExist()
    {
        // Arrange
        PatientProfile? nullPatient = null;
        _mockPatientProfileService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPatient_ReturnsOkResult_WithPatient()
    {
        // Arrange
        var mockPatient = new PatientProfile { /* properties */ };
        _mockPatientProfileService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockPatient);

        // Act
        var result = await _controller.GetPatient(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedPatient = Assert.IsType<PatientProfile>(okResult.Value);
        Assert.NotNull(returnedPatient);
    }

    [Fact]
    public async Task GetPatient_PastDueEvents_Returns_Ok_Result_WithSessionEvents()
    {
        // Arrange
        var targetPatientId = 1;
        var expectedSessionIds = new[] { 1, 3 };
        _mockPatientProfileService
            .Setup(x => x.GetByIdAsync(targetPatientId))
            .ReturnsAsync(Mock.Of<PatientProfile>(pp => pp.PatientId == targetPatientId));
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, PatientId = 1},
                new SessionEvent() {SessionId = 2, PatientId = 2},
                new SessionEvent() {SessionId = 3, PatientId = 1}]);

        // Act
        var result = await _controller.GetPastDueSessions(targetPatientId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<PatientPastDueInfo>()
            .Which.Should().NotBeNull();
        var patientInfo = ((OkObjectResult)result).Value as PatientPastDueInfo;
        patientInfo.Should().NotBeNull();
        patientInfo!.PartyType.Should().NotBeNull();
        patientInfo.Party.Should().NotBeNull();
        patientInfo.PastDueSessions.Should().BeGreaterOrEqualTo(0);
        patientInfo.PastDueTotalAmount.Should().BeGreaterOrEqualTo(0);
        patientInfo.AmountPaidSoFar.Should().BeGreaterOrEqualTo(0);
        patientInfo.Delinquency.Should().NotBeNull()
            .And.HaveCount(expectedSessionIds.Length)
            .And.OnlyContain(session => expectedSessionIds.Contains(session.SessionId));
    }    

    [Fact]
    public async Task GetPatient_PastDueEvents_Returns_NO_Result_WithSessionEvents()
    {
        // Arrange
        var targetPatientId = 3;
        _mockPatientProfileService
            .Setup(x => x.GetByIdAsync(targetPatientId))
            .ReturnsAsync(Mock.Of<PatientProfile>(pp => pp.PatientId == targetPatientId));
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, PatientId = 1},
                new SessionEvent() {SessionId = 2, PatientId = 2},
                new SessionEvent() {SessionId = 3, PatientId = 1}]);

        // Act
        var result = await _controller.GetPastDueSessions(targetPatientId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<PatientPastDueInfo>()
            .Which.Should().NotBeNull();
        var patientInfo = ((OkObjectResult)result).Value as PatientPastDueInfo;
        patientInfo!.PartyType.Should().NotBeNull();
        patientInfo.Party.Should().NotBeNull();
        patientInfo.PastDueSessions.Should().BeGreaterOrEqualTo(0);
        patientInfo.PastDueTotalAmount.Should().BeGreaterOrEqualTo(0);
        patientInfo.AmountPaidSoFar.Should().BeGreaterOrEqualTo(0);
        patientInfo.Delinquency.Should().NotBeNull()
            .And.HaveCount(0);
    }    
}
