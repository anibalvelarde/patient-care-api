using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-63B (B7): a statement charge carried only money fields, so the UI labelled every $0-due
/// line "Paid" — wrong for a cancelled session or a waived fee. Each charge now carries the
/// session's StatusName and IsFeeWaived (TherapySession.FeeWaivedOn set — the WP-49 waive stamp).
/// </summary>
public class AccountStatementServiceTests
{
    private const int CaretakerId = 1135;
    private const int PatientId = 922;

    private readonly Mock<ICaretakerProfileService> _caretakers = new();
    private readonly Mock<ITherapySessionRepository> _sessions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly AccountStatementService _sut;

    public AccountStatementServiceTests()
    {
        _caretakers.Setup(s => s.GetByIdAsync(CaretakerId)).ReturnsAsync(new CaretakerProfile
        {
            CaretakerId = CaretakerId,
            Patients = new List<CaretakerPatientSummary> { new() { PatientId = PatientId, PatientName = "Synthetic, Pat" } },
        });
        _payments.Setup(r => r.GetByCaretakerIdAndDateRangeAsync(CaretakerId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<Payment>());
        _sut = new AccountStatementService(Mock.Of<ILogger<AccountStatementService>>(), _caretakers.Object,
            _sessions.Object, _payments.Object, Mock.Of<IPatientCaretakerRepository>());
    }

    [Fact]
    public async Task Charges_CarryStatusName_AndIsFeeWaived()
    {
        _sessions.Setup(r => r.GetByPatientIdsAndDateRangeAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(new List<TherapySession>
            {
                new() { Id = 1, PatientId = PatientId, Amount = 0m, AppointmentStatusId = SessionStatus.Cancelled,
                        AppointmentStatus = new AppointmentStatus { Id = SessionStatus.Cancelled, Name = SessionStatus.Names.Cancelled } },
                new() { Id = 2, PatientId = PatientId, Amount = 85m, DiscountAmount = 85m, AppointmentStatusId = SessionStatus.NoShow,
                        FeeWaivedOn = new DateOnly(2026, 11, 20) },   // status nav not loaded → canonical-map fallback
                new() { Id = 3, PatientId = PatientId, Amount = 50m, AmountPaid = 50m, AppointmentStatusId = SessionStatus.Completed,
                        AppointmentStatus = new AppointmentStatus { Id = SessionStatus.Completed, Name = SessionStatus.Names.Completed } },
            });

        var statement = await _sut.GetStatementAsync(CaretakerId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        var charges = statement!.Charges.ToDictionary(c => c.SessionId);
        Assert.Equal(SessionStatus.Names.Cancelled, charges[1].StatusName);
        Assert.False(charges[1].IsFeeWaived);
        Assert.Equal(SessionStatus.Names.NoShow, charges[2].StatusName);
        Assert.True(charges[2].IsFeeWaived);
        Assert.Equal(SessionStatus.Names.Completed, charges[3].StatusName);
        Assert.False(charges[3].IsFeeWaived);
    }
}
