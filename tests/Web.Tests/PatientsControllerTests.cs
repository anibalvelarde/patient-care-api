using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using FluentAssertions;
using System.Linq;
using Castle.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class PatientsControllerTests
{
    private readonly Mock<IPatientProfileService> _mockPatientProfileService;
    private readonly Mock<IHandleSessionEvent> _mockSessionEventHandler;
    private readonly Mock<ISessionEventRepository> _mockSessionEventRepository;
    private readonly Mock<IPatientMergeService> _mockPatientMergeService;
    private readonly PatientsController _controller;

    public PatientsControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<PatientsController>>();
        _mockPatientProfileService = new Mock<IPatientProfileService>();
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>();
        _mockSessionEventRepository = new Mock<ISessionEventRepository>();
        _mockPatientMergeService = new Mock<IPatientMergeService>();
        _controller = new PatientsController(fakeLogger, _mockPatientProfileService.Object, _mockSessionEventHandler.Object, _mockSessionEventRepository.Object, _mockPatientMergeService.Object);
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

    // ── WP-21 (F1): paged session-history endpoints ──────────────────────────────────

    [Fact]
    public async Task GetPatientSessionHistory_ReturnsOk_WithPagedResult()
    {
        var envelope = new PagedResult<PatientSessionHistorySummary>
        {
            Items = new List<PatientSessionHistorySummary> { new() { PatientId = 7 } },
            Page = 1,
            PageSize = 30,
            TotalCount = 1,
        };
        _mockPatientProfileService
            .Setup(s => s.GetSessionHistoryAsync("doe", 1, 30))
            .ReturnsAsync(envelope);

        var result = await _controller.GetPatientSessionHistory("doe", 1, 30);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PagedResult<PatientSessionHistorySummary>>(okResult.Value);
        returned.Should().BeSameAs(envelope);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync("doe", 1, 30), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessionHistory_ClampsPagingParams()
    {
        _mockPatientProfileService
            .Setup(s => s.GetSessionHistoryAsync(null, It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new PagedResult<PatientSessionHistorySummary>());

        // page 0 → 1; pageSize 9999 → the 100 ceiling; pageSize 0 → the 30 default.
        await _controller.GetPatientSessionHistory(null, page: 0, pageSize: 9999);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync(null, 1, 100), Times.Once);

        await _controller.GetPatientSessionHistory(null, page: 3, pageSize: 0);
        _mockPatientProfileService.Verify(s => s.GetSessionHistoryAsync(null, 3, 30), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessions_ReturnsOk_WithPagedResult()
    {
        var envelope = new PagedResult<SessionEvent>
        {
            Items = new List<SessionEvent> { new() { SessionId = 42 } },
            Page = 2,
            PageSize = 25,
            TotalCount = 132,
        };
        _mockSessionEventRepository
            .Setup(r => r.GetByPatientIdAsync(7, 2, 25, null, null))
            .ReturnsAsync(envelope);

        var result = await _controller.GetPatientSessions(7, page: 2, pageSize: 25);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<PagedResult<SessionEvent>>(okResult.Value);
        returned.Should().BeSameAs(envelope);
        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 2, 25, null, null), Times.Once);
    }

    [Fact]
    public async Task GetPatientSessions_ClampsPagingParams_AndForwardsFilters()
    {
        _mockSessionEventRepository
            .Setup(r => r.GetByPatientIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool?>(), It.IsAny<string?>()))
            .ReturnsAsync(new PagedResult<SessionEvent>());

        await _controller.GetPatientSessions(7, page: -5, pageSize: 400, isDiscovery: true, status: "Completed");

        _mockSessionEventRepository.Verify(r => r.GetByPatientIdAsync(7, 1, 100, true, "Completed"), Times.Once);
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
    public async Task GetAllPastDuePatients_ReturnsOk_WithPastDuePatients()
    {
        // Arrange
        var pastDuePatients = new List<PatientPastDueInfo>
        {
            new() { Party = Mock.Of<PatientProfile>(p => p.PatientId == 1), PastDueSessions = 2, PastDueTotalAmount = 100m, AmountPaidSoFar = 20m },
            new() { Party = Mock.Of<PatientProfile>(p => p.PatientId == 2), PastDueSessions = 1, PastDueTotalAmount = 50m, AmountPaidSoFar = 0m }
        };
        _mockSessionEventHandler
            .Setup(x => x.GetAllPatientsPastDueAsync())
            .ReturnsAsync(pastDuePatients);

        // Act
        var result = await _controller.GetAllPastDuePatients();

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<PatientPastDueInfo>>()
            .Which.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllPastDuePatients_ReturnsOk_WithEmptyList()
    {
        // Arrange
        _mockSessionEventHandler
            .Setup(x => x.GetAllPatientsPastDueAsync())
            .ReturnsAsync(new List<PatientPastDueInfo>());

        // Act
        var result = await _controller.GetAllPastDuePatients();

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<PatientPastDueInfo>>()
            .Which.Should().BeEmpty();
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

    // ── WP-22 (F2): duplicate-patient merge ───────────────────────────────────────────

    [Fact]
    public async Task PreviewPatientMerge_ReturnsOk_WithServicePreview()
    {
        var request = new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 };
        var preview = new PatientMergePreview { Blockers = new List<string> { "blocked" } };
        _mockPatientMergeService.Setup(s => s.PreviewAsync(request)).ReturnsAsync(preview);

        var result = await _controller.PreviewPatientMerge(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().BeSameAs(preview);
        _mockPatientMergeService.Verify(s => s.PreviewAsync(request), Times.Once);
        _mockPatientMergeService.Verify(s => s.MergeAsync(It.IsAny<PatientMergeRequest>()), Times.Never);
    }

    [Fact]
    public async Task MergePatients_ReturnsOk_WithServiceResult()
    {
        var request = new PatientMergeRequest { SurvivorPatientId = 1, EliminatedPatientId = 2 };
        var mergeResult = new PatientMergeResult { SurvivorPatientId = 1, EliminatedPatientId = 2, MergeLogId = 42 };
        _mockPatientMergeService.Setup(s => s.MergeAsync(request)).ReturnsAsync(mergeResult);

        var result = await _controller.MergePatients(request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().BeSameAs(mergeResult);
        _mockPatientMergeService.Verify(s => s.MergeAsync(request), Times.Once);
    }
}
