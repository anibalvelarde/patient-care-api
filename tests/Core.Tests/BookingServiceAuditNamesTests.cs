using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-63B (B5): the booking-lifecycle responses (/status, /confirm, /cancel, /complete, /noshow,
/// /checkin, /start-therapy) returned audit.updatedBy = "System" because the updater id was never
/// resolved to a name (the DB stamp itself was right). BookingService now enriches like
/// SessionEventHandler does.
/// </summary>
public class BookingServiceAuditNamesTests
{
    private const int Updater = 4321;
    private const string UpdaterName = "Synthetic, Fran";

    private readonly Mock<IBookingRepository> _booking = new();
    private readonly Mock<ITherapySessionRepository> _sessions = new();
    private readonly Mock<ISessionTransitionMoneyService> _money = new();
    private readonly Mock<IUserNameResolver> _resolver = new();
    private readonly BookingService _sut;

    public BookingServiceAuditNamesTests()
    {
        _money.Setup(m => m.PrepareTransitionAsync(It.IsAny<TherapySession>(), It.IsAny<int>()))
            .ReturnsAsync((SessionTransitionMoneyPatch?)null);
        _sessions.Setup(r => r.GetByIdAsync(77)).ReturnsAsync(new TherapySession
        {
            Id = 77, TherapistId = 3, PatientId = 922, SessionDate = DateOnly.Parse("2026-11-09"),
            SessionTime = TimeOnly.Parse("10:00"), Duration = 30, AppointmentStatusId = SessionStatus.Proposed,
        });
        _booking.Setup(b => b.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<SessionTransitionMoneyPatch?>()))
            .ReturnsAsync(() => Stamped());
        _booking.Setup(b => b.GetSessionEventByIdAsync(77)).ReturnsAsync(() => Stamped());
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new Dictionary<int, string> { [Updater] = UpdaterName });
        _sut = new BookingService(Mock.Of<ILogger<BookingService>>(), _booking.Object, _sessions.Object,
            _money.Object, _resolver.Object);
    }

    private static SessionEvent Stamped() => new()
    {
        SessionId = 77,
        Audit = new AuditInfo { UpdatedByUserId = Updater },
    };

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.NoShow)]
    [InlineData(SessionStatus.CheckedIn)]
    [InlineData(SessionStatus.InTherapy)]
    [InlineData(SessionStatus.Cancelled)]
    public async Task UpdateStatus_ResolvesUpdaterName(int status)
    {
        var result = await _sut.UpdateStatusAsync(77, status);
        Assert.Equal(UpdaterName, result.Audit!.UpdatedBy);
    }

    [Fact]
    public async Task Cancel_ResolvesUpdaterName()
    {
        var result = await _sut.CancelAppointmentAsync(77, "synthetic reason");
        Assert.Equal(UpdaterName, result.Audit!.UpdatedBy);
    }

    [Theory]
    [InlineData(ConfirmationValues.Confirmed)]
    [InlineData(ConfirmationValues.Declined)]
    [InlineData(ConfirmationValues.NoAnswer)]
    public async Task Confirm_ResolvesUpdaterName(string outcome)
    {
        var result = await _sut.ConfirmAppointmentAsync(77,
            new ConfirmationRequest { ConfirmationMethod = ConfirmationValues.MethodPhone, ConfirmationResult = outcome });
        Assert.Equal(UpdaterName, result.Audit!.UpdatedBy);
    }
}
