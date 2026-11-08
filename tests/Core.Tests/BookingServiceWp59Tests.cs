using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-59 (F-2): reinstating a Cancelled session (/confirm Confirmed, /checkin, /start-therapy,
/// /complete, /noshow) runs the double-booking guard. Moves between busy statuses never do.
/// </summary>
public class BookingServiceWp59Tests
{
    private static readonly DateOnly Day = DateOnly.Parse("2026-11-09");

    private readonly Mock<IBookingRepository> _booking = new();
    private readonly Mock<ITherapySessionRepository> _sessions = new();
    private readonly Mock<ISessionTransitionMoneyService> _money = new();
    private readonly BookingService _sut;

    public BookingServiceWp59Tests()
    {
        _money.Setup(m => m.PrepareTransitionAsync(It.IsAny<TherapySession>(), It.IsAny<int>()))
            .ReturnsAsync((SessionTransitionMoneyPatch?)null);
        _booking.Setup(b => b.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<SessionTransitionMoneyPatch?>()))
            .ReturnsAsync(new SessionEvent());
        _sut = new BookingService(Mock.Of<ILogger<BookingService>>(), _booking.Object, _sessions.Object, _money.Object);
    }

    private void OnFile(int status)
    {
        _sessions.Setup(r => r.GetByIdAsync(77)).ReturnsAsync(new TherapySession
        {
            Id = 77, TherapistId = 3, PatientId = 922, SessionDate = Day,
            SessionTime = TimeOnly.Parse("10:00"), Duration = 30, AppointmentStatusId = status,
        });
    }

    private void SlotTaken() =>
        _sessions.Setup(r => r.GetSlotCandidatesAsync(Day, 3, 922, 77)).ReturnsAsync(new List<TherapySession>
        {
            new() { Id = 12360, TherapistId = 3, PatientId = 500, SessionDate = Day,
                    SessionTime = TimeOnly.Parse("10:00"), Duration = 30, AppointmentStatusId = SessionStatus.Proposed },
        });

    [Theory]
    [InlineData(SessionStatus.CheckedIn)]
    [InlineData(SessionStatus.InTherapy)]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.NoShow)]
    public async Task UpdateStatus_FromCancelled_IntoTakenSlot_Throws409_NothingWritten(int target)
    {
        OnFile(SessionStatus.Cancelled);
        SlotTaken();

        await Assert.ThrowsAsync<ConflictException>(() => _sut.UpdateStatusAsync(77, target));

        _booking.Verify(b => b.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<SessionTransitionMoneyPatch?>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_FromCancelled_IntoTakenSlot_Throws409_NoConfirmationRecorded()
    {
        OnFile(SessionStatus.Cancelled);
        SlotTaken();

        await Assert.ThrowsAsync<ConflictException>(() => _sut.ConfirmAppointmentAsync(77,
            new ConfirmationRequest { ConfirmationResult = ConfirmationValues.Confirmed }));

        _booking.Verify(b => b.AddConfirmationAsync(It.IsAny<int>(), It.IsAny<ConfirmationRequest>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatus_BetweenBusyStatuses_IsNotChecked()
    {
        OnFile(SessionStatus.Confirmed);
        SlotTaken(); // even a legacy overlap must not block a check-in

        await _sut.UpdateStatusAsync(77, SessionStatus.CheckedIn);

        _sessions.Verify(r => r.GetSlotCandidatesAsync(It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatus_FromCancelled_FreeSlot_Reinstates()
    {
        OnFile(SessionStatus.Cancelled);

        await _sut.UpdateStatusAsync(77, SessionStatus.CheckedIn);

        _booking.Verify(b => b.UpdateStatusAsync(77, SessionStatus.CheckedIn, It.IsAny<SessionTransitionMoneyPatch?>()), Times.Once);
    }
}
