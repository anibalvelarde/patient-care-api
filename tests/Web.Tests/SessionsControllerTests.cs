using FluentAssertions;
using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Web.Controllers;
using Xunit;
using System;
using Microsoft.AspNetCore.Mvc;
using Xunit.Sdk;

namespace Web.Tests.Controllers;

public class SessionsControllerTests
{
    private readonly Mock<IHandleSessionEvent> _mockSessionEventHandler;
    private readonly SessionsController _controller;

    public SessionsControllerTests()
    {
        var fakeLogger = Mock.Of<ILogger<SessionsController>>();
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>(MockBehavior.Strict);
        _controller = new SessionsController(fakeLogger, _mockSessionEventHandler.Object);
    }

    [Fact]
    public void Should_Initialize()
    {
        // arrange
        var fakeLogger = Mock.Of<ILogger<SessionsController>>();
        var fakeEvHandler = Mock.Of<IHandleSessionEvent>();

        // act
        var sut = new SessionsController(fakeLogger, fakeEvHandler);

        // assert
        sut.Should().NotBeNull();
     }

    [Fact]
    public async Task Should_Return_SessionsForGivenDate()
    {
        // Arrange
        var targetDate = new DateTime(2024, 4, 12);
        var tDateAsDateOnly = DateOnly.FromDateTime(targetDate);
        _mockSessionEventHandler
            .Setup(x => x.GetAllByTargetDateAsync(tDateAsDateOnly))
            .ReturnsAsync( [
                new SessionEvent() {SessionId =1, TherapistId = 1, SessionDate = DateOnly.FromDateTime(targetDate) },
                new SessionEvent() {SessionId = 4, TherapistId = 1, SessionDate = DateOnly.FromDateTime(targetDate)}]);        
    
        // Act
        var result = await _controller.GetAllEventsForADate(targetDate.ToShortDateString());
    
        // Assert
        _mockSessionEventHandler.VerifyAll();
        result.Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
                .BeAssignableTo<IEnumerable<SessionEvent>>();
        var sessions = ((OkObjectResult)result).Value as IEnumerable<SessionEvent>;
        sessions.Should()
            .NotBeNull()
            .And.HaveCount(2);
    }
}