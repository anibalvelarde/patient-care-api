using Moq;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using FluentAssertions;
using Castle.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Web.Tests.Controllers;

public class TherapistsControllerTests
{
    private readonly Mock<ITherapistProfileService> _mockService;
    private readonly Mock<IHandleSessionEvent> _mockSessionEventHandler;
    private readonly TherapistsController _controller;

    public TherapistsControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<TherapistsController>>();
        _mockService = new Mock<ITherapistProfileService>();
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>();
        _controller = new TherapistsController(fakeLogger, _mockService.Object, _mockSessionEventHandler.Object);
    }

    [Fact]
    public async Task GetAllTherapists_ReturnsOkResult_WithTherapists()
    {
        // Arrange
        var mockTherapists = new List<TherapistProfile>
        {
            new TherapistProfile { /* properties */ },
            new TherapistProfile { /* properties */ }
        };
        _mockService.Setup(service => service.GetAllAsync()).ReturnsAsync(mockTherapists);

        // Act
        var result = await _controller.GetAllTherapists();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedTherapists = Assert.IsType<List<TherapistProfile>>(okResult.Value);
        Assert.Equal(mockTherapists.Count, returnedTherapists.Count);
    }

    [Fact]
    public async Task GetTherapist_ReturnsNotFound_WhenTherapistDoesNotExist()
    {
        // Arrange
        TherapistProfile? nullTherapist = null;
        _mockService.Setup(service =>
            service.GetByIdAsync(It.IsAny<int>()))
        .ReturnsAsync(value: nullTherapist);

        // Act
        var result = await _controller.GetTherapist(1);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetTherapist_ReturnsOkResult_WithTherapist()
    {
        // Arrange
        var mockTherapist = new TherapistProfile { /* properties */ };
        _mockService.Setup(service => service.GetByIdAsync(1)).ReturnsAsync(mockTherapist);

        // Act
        var result = await _controller.GetTherapist(1);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedTherapist = Assert.IsType<TherapistProfile>(okResult.Value);
        Assert.NotNull(returnedTherapist);
    }

    [Fact]
    public async Task GetTherapist_PastDueEvents_Returns_Ok_Result_WithSessionEvents()
    {
        // Arrange
        var targetTherapistId = 1;
        var expectedSessionIds = new[] { 1, 3 };
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, TherapistId = 1},
                new SessionEvent() {SessionId = 2, TherapistId = 2},
                new SessionEvent() {SessionId = 3, TherapistId = 1}]);

        // Act
        var result = await _controller.GetPastDueSessions(targetTherapistId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<SessionEvent>>()
            .Which.Should().NotBeNull()
            .And.HaveCount(expectedSessionIds.Length)
            .And.OnlyContain(session => expectedSessionIds.Contains(session.SessionId));
    } 

    [Fact]
    public async Task GetTherapist_PastDueEvents_Returns_NO_Result_WithSessionEvents()
    {
        // Arrange
        var targetTherapistId = 3;
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, TherapistId = 1},
                new SessionEvent() {SessionId = 2, TherapistId = 2},
                new SessionEvent() {SessionId = 3, TherapistId = 1}]);

        // Act
        var result = await _controller.GetPastDueSessions(targetTherapistId);

        // Assert
        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeAssignableTo<IEnumerable<SessionEvent>>()
            .Which.Should().NotBeNull()
            .And.HaveCount(0);
    }       
}
