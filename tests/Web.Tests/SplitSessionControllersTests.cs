using FluentAssertions;
using Moq;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Web.Controllers;

namespace Web.Tests.Controllers;

// Smoke tests for the controllers split out of the former god SessionsController in Chunk 4.
// Each now depends only on the single service it needs (the basis for cleaner future claims policies).
public class SplitSessionControllersTests
{
    [Fact]
    public void BookingController_initializes_with_only_booking_service()
    {
        var sut = new BookingController(Mock.Of<IBookingService>());
        sut.Should().NotBeNull();
    }

    [Fact]
    public void ScheduleController_initializes_with_only_schedule_service()
    {
        var sut = new ScheduleController(Mock.Of<IScheduleMatrixService>());
        sut.Should().NotBeNull();
    }

    [Fact]
    public void SessionPaymentsController_initializes_with_only_payment_service()
    {
        var sut = new SessionPaymentsController(Mock.Of<IPaymentRecordService>());
        sut.Should().NotBeNull();
    }
}
