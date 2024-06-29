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
                new SessionEvent() {SessionId = 1, TherapistId = 1, SessionDate = DateOnly.FromDateTime(targetDate) },
                new SessionEvent() {SessionId = 5, TherapistId = 1, SessionDate = DateOnly.FromDateTime(targetDate)}]);        
    
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
    [Fact]
    public async Task Should_Handle_NO_SessionsForGivenDate()
    {
        // Arrange
        var targetDate = new DateTime(2024, 4, 12);
        var tDateAsDateOnly = DateOnly.FromDateTime(targetDate);
        _mockSessionEventHandler
            .Setup(x => x.GetAllByTargetDateAsync(tDateAsDateOnly))
            .ReturnsAsync( []);        
    
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
            .And.HaveCount(0);
    }    

    [Fact]
    public async Task Should_Handle_BadStringDate()
    {
        // Arrange
        var todaysDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var badStringDate = "BadStringDateType";
        _mockSessionEventHandler
            .Setup(x => x.GetAllByTargetDateAsync(todaysDate))
            .ReturnsAsync( [
                new SessionEvent() {SessionId = 1, TherapistId = 1, SessionDate = todaysDate },
                new SessionEvent() {SessionId = 4, TherapistId = 1, SessionDate = todaysDate}
        ]);        
    
        // Act
        var result = await _controller.GetAllEventsForADate(badStringDate);
    
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

    [Fact]
    public async Task Should_Return_PastDueSessions()
    {
        // Arrange
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync([
                new SessionEvent() {SessionId = 1, TherapistId = 1, SessionDate = DateOnly.FromDateTime(DateTime.UtcNow), IsPastDue = true },
                new SessionEvent() {SessionId = 4, TherapistId = 1, SessionDate = DateOnly.FromDateTime(DateTime.UtcNow), IsPastDue = true }
            ]);        

        // Act
        var result = await _controller.GetAllPastDueSessionEvents();

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

    [Fact]
    public async Task Should_Handle_NO_PastDueSessions()
    {
        // Arrange
        _mockSessionEventHandler
            .Setup(x => x.GetAllPastDueAsync())
            .ReturnsAsync([]);        

        // Act
        var result = await _controller.GetAllPastDueSessionEvents();

        // Assert
        _mockSessionEventHandler.VerifyAll();
        result.Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
                .BeAssignableTo<IEnumerable<SessionEvent>>();
        var sessions = ((OkObjectResult)result).Value as IEnumerable<SessionEvent>;
        sessions.Should()
            .NotBeNull()
            .And.HaveCount(0);
    }    

    [Fact]
    public async Task Should_Handle_Creating_NewSession()
    {
 
        // Arrange
        var newSession = Mock.Of<SessionEventRequest>();
        var fakeNewSessionId = DateTime.UtcNow.Millisecond;
        _mockSessionEventHandler        
            .Setup(x => x.CreateAsync(newSession))
            .ReturnsAsync(new SessionEvent(){SessionId = fakeNewSessionId});        

        // Act
        var result = await _controller.CreateSession(newSession);

        // Assert
        _mockSessionEventHandler.VerifyAll();
        result.Should()
            .BeOfType<CreatedAtActionResult>()
            .Which.Value.Should()
                .BeAssignableTo<SessionEvent>();
        var createdEvent = ((CreatedAtActionResult)result).Value as SessionEvent;
        createdEvent.Should().NotBeNull();
        createdEvent!.SessionId.Should().Be(fakeNewSessionId);
    }

    [Fact]
    public async Task Should_Handle_Updating_ExisingSession()
    {
 
        // Arrange
        var existingSession = Mock.Of<SessionEventUpdateRequest>();
        var fakeNewSessionId = DateTime.UtcNow.Millisecond;
        _mockSessionEventHandler
            .Setup(x => x.VerifyRequestAsync(fakeNewSessionId, existingSession))
            .ReturnsAsync(true);
        _mockSessionEventHandler        
            .Setup(x => x.UpdateAsync(fakeNewSessionId, existingSession))
            .ReturnsAsync(true);        

        // Act
        var result = await _controller.UpdateSession(fakeNewSessionId, existingSession);

        // Assert
        _mockSessionEventHandler.VerifyAll();
        result.Should()
            .BeOfType<NoContentResult>();
    }    
}