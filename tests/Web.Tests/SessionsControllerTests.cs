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
        // After the Chunk 4 split, SessionsController only handles core session events
        // (booking/schedule/payments moved to their own controllers), so it depends on
        // IHandleSessionEvent alone.
        _mockSessionEventHandler = new Mock<IHandleSessionEvent>(MockBehavior.Strict);
        _controller = new SessionsController(_mockSessionEventHandler.Object);
    }

    [Fact]
    public void Should_Initialize()
    {
        // arrange
        var fakeEvHandler = Mock.Of<IHandleSessionEvent>();

        // act
        var sut = new SessionsController(fakeEvHandler);

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
        // WP-24 (audit F1): a non-discovery request skips the Patients.StartDiscovery gate.
        _mockSessionEventHandler
            .Setup(x => x.IsDiscoveryRequestAsync(newSession))
            .ReturnsAsync(false);
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
        // WP-40 (BK-3): the controller reads the session on file to detect a discount change.
        _mockSessionEventHandler
            .Setup(x => x.GetByIdAsync(fakeNewSessionId))
            .ReturnsAsync(new SessionEvent { SessionId = fakeNewSessionId, Discount = 0m });
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

    // ── WP-40 (BK-3): the Sessions.Discount.Edit gate on PUT — a CHANGED discount needs the
    // claim (AM/MGR + wildcard); unchanged/echoed discounts pass so FD keeps editing
    // non-money fields. Same imperative pattern as the WP-23/WP-37 patient-field gates. ──

    private void UseCaller(params (string Type, string Value)[] claims)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            claims.Select(c => new System.Security.Claims.Claim(c.Type, c.Value)),
            authenticationType: "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(identity)
            }
        };
    }

    private SessionEventUpdateRequest SetupSessionOnFile(int id, decimal storedDiscount)
    {
        var request = new SessionEventUpdateRequest { Amount = 100m, Discount = storedDiscount };
        _mockSessionEventHandler.Setup(x => x.VerifyRequestAsync(id, It.IsAny<SessionEventUpdateRequest>())).ReturnsAsync(true);
        _mockSessionEventHandler.Setup(x => x.GetByIdAsync(id))
            .ReturnsAsync(new SessionEvent { SessionId = id, Amount = 100m, Discount = storedDiscount });
        return request;
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_WithoutClaim_ReturnsForbid_AndDoesNotUpdate()
    {
        var request = SetupSessionOnFile(7, storedDiscount: 0m);
        request.Discount = 15m; // change
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.AppointmentsBook)); // FD-like

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<ForbidResult>();
        _mockSessionEventHandler.Verify(x => x.UpdateAsync(It.IsAny<int>(), It.IsAny<SessionEventUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_WithClaim_Delegates()
    {
        var request = SetupSessionOnFile(7, storedDiscount: 0m);
        request.Discount = 15m;
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.SessionsDiscountEdit));
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
        _mockSessionEventHandler.Verify(x => x.UpdateAsync(7, request), Times.Once);
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_WithWildcard_Delegates()
    {
        var request = SetupSessionOnFile(7, storedDiscount: 10m);
        request.Discount = 0m; // lowering is also a change
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.SystemClaimType,
                   Neurocorp.Api.Core.Authorization.SystemClaims.FullAccessValue)); // SYSADMIN
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UpdateSession_DiscountEchoedUnchanged_WithoutClaim_Passes()
    {
        var request = SetupSessionOnFile(7, storedDiscount: 20m);
        request.Discount = 20m; // echo, not a change
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.AppointmentsBook));
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
    }

    // ── WP-49 (ruling 4): the AM loophole. On a FEE-BEARING session a changed discount needs
    // Sessions.Fee.Manage in ADDITION to Sessions.Discount.Edit. Without this an AM could zero
    // a manager's fee by setting discount = amount, moving the same money as a waiver but with
    // no reason, no marker and no waiver record — leaving BR4 as decoration. ──

    private SessionEventUpdateRequest SetupFeeBearingSessionOnFile(int id, decimal storedDiscount)
    {
        var request = new SessionEventUpdateRequest { Amount = 100m, Discount = storedDiscount };
        _mockSessionEventHandler.Setup(x => x.VerifyRequestAsync(id, It.IsAny<SessionEventUpdateRequest>())).ReturnsAsync(true);
        _mockSessionEventHandler.Setup(x => x.GetByIdAsync(id))
            .ReturnsAsync(new SessionEvent
            {
                SessionId = id,
                Amount = 100m,
                Discount = storedDiscount,
                LateFeeAmount = 30m,
                LateFeeAppliedOn = new DateOnly(2026, 7, 10),
                CarriesFee = true,
            });
        return request;
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_OnFeeBearingSession_WithOnlyDiscountClaim_Forbids()
    {
        // The AM case: holds Sessions.Discount.Edit, does NOT hold Sessions.Fee.Manage.
        var request = SetupFeeBearingSessionOnFile(7, storedDiscount: 0m);
        request.Discount = 100m; // discount = amount — the loophole move
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.SessionsDiscountEdit));

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<ForbidResult>();
        _mockSessionEventHandler.Verify(x => x.UpdateAsync(It.IsAny<int>(), It.IsAny<SessionEventUpdateRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_OnFeeBearingSession_WithBothClaims_Delegates()
    {
        var request = SetupFeeBearingSessionOnFile(7, storedDiscount: 0m);
        request.Discount = 25m;
        UseCaller(
            (Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
             Neurocorp.Api.Web.Authorization.Permissions.SessionsDiscountEdit),
            (Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
             Neurocorp.Api.Web.Authorization.Permissions.SessionsFeeManage));
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
        _mockSessionEventHandler.Verify(x => x.UpdateAsync(7, request), Times.Once);
    }

    [Fact]
    public async Task UpdateSession_DiscountUnchanged_OnFeeBearingSession_IsNotBlocked()
    {
        // The guard is change-based like every other field gate in the house — carrying a fee
        // must not freeze the session's other fields.
        var request = SetupFeeBearingSessionOnFile(7, storedDiscount: 20m);
        request.Discount = 20m; // echo
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.AppointmentsBook));
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task UpdateSession_DiscountChanged_OnFeeFreeSession_StillOnlyNeedsDiscountClaim()
    {
        // AM keeps ordinary discount editing everywhere the loophole does not apply.
        var request = SetupSessionOnFile(7, storedDiscount: 0m);
        request.Discount = 15m;
        UseCaller((Neurocorp.Api.Core.Authorization.SystemClaims.PermissionClaimType,
                   Neurocorp.Api.Web.Authorization.Permissions.SessionsDiscountEdit));
        _mockSessionEventHandler.Setup(x => x.UpdateAsync(7, request)).ReturnsAsync(true);

        var result = await _controller.UpdateSession(7, request);

        result.Should().BeOfType<NoContentResult>();
    }
}