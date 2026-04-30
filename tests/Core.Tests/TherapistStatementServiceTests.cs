using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

public class TherapistStatementServiceTests
{
    private const int TherapistId = 42;

    private static readonly AppointmentStatus CompletedStatus = new() { Id = 4, Name = "Completed" };
    private static readonly AppointmentStatus CancelledStatus = new() { Id = 3, Name = "Cancelled" };
    private static readonly AppointmentStatus NoShowStatus = new() { Id = 5, Name = "NoShow" };
    private static readonly AppointmentStatus ScheduledStatus = new() { Id = 1, Name = "Scheduled" };

    private static (
        TherapistStatementService Service,
        Mock<ITherapySessionRepository> SessionRepoMock
    ) BuildService(IReadOnlyList<TherapySession> sessions)
    {
        var fakeLogger = Mock.Of<ILogger<TherapistStatementService>>();

        var profileService = new Mock<ITherapistProfileService>();
        profileService
            .Setup(s => s.GetByIdAsync(TherapistId))
            .ReturnsAsync(new TherapistProfile { TherapistId = TherapistId, TherapistName = "Smith, Jane" });

        var statusRepo = new Mock<IRepository<AppointmentStatus>>();
        statusRepo
            .Setup(r => r.GetAllAsync())
            .ReturnsAsync(new List<AppointmentStatus> { CompletedStatus, CancelledStatus, NoShowStatus, ScheduledStatus });

        var sessionRepo = new Mock<ITherapySessionRepository>();
        sessionRepo
            .Setup(r => r.GetByTherapistIdAndDateRangeAsync(
                It.IsAny<int>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((int _, DateOnly from, DateOnly to, IEnumerable<int> statusIds) =>
            {
                var ids = statusIds.ToHashSet();
                return sessions
                    .Where(s => s.SessionDate >= from && s.SessionDate <= to && ids.Contains(s.AppointmentStatusId))
                    .ToList();
            });

        var service = new TherapistStatementService(
            fakeLogger,
            profileService.Object,
            sessionRepo.Object,
            statusRepo.Object);

        return (service, sessionRepo);
    }

    private static Patient MakePatient(int id, string first, string last)
    {
        return new Patient
        {
            Id = id,
            User = new User { Id = id * 100, FirstName = first, LastName = last, MiddleName = string.Empty }
        };
    }

    private static TherapySession MakeSession(
        int id,
        Patient patient,
        DateOnly date,
        TimeOnly time,
        AppointmentStatus status,
        decimal amount = 100m,
        decimal discount = 0m,
        decimal providerAmount = 60m,
        SpecialtyType? specialty = null,
        string? legacyTherapyTypes = null)
    {
        return new TherapySession
        {
            Id = id,
            PatientId = patient.Id,
            Patient = patient,
            TherapistId = TherapistId,
            SessionDate = date,
            SessionTime = time,
            AppointmentStatusId = status.Id,
            AppointmentStatus = status,
            Amount = amount,
            DiscountAmount = discount,
            ProviderAmount = providerAmount,
            SpecialtyType = specialty,
            SpecialtyTypeId = specialty?.Id,
            TherapyTypes = legacyTherapyTypes ?? string.Empty,
        };
    }

    [Fact]
    public async Task GetStatementAsync_ReturnsNull_WhenTherapistNotFound()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<TherapistStatementService>>();
        var profileService = new Mock<ITherapistProfileService>();
        profileService.Setup(s => s.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((TherapistProfile?)null);
        var statusRepo = new Mock<IRepository<AppointmentStatus>>();
        var sessionRepo = new Mock<ITherapySessionRepository>();
        var service = new TherapistStatementService(fakeLogger, profileService.Object, sessionRepo.Object, statusRepo.Object);

        // Act
        var result = await service.GetStatementAsync(999, null, null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatementAsync_Throws_WhenToBeforeFrom()
    {
        // Arrange
        var (service, _) = BuildService(new List<TherapySession>());
        var from = new DateOnly(2026, 4, 10);
        var to = new DateOnly(2026, 4, 1);

        // Act
        var act = async () => await service.GetStatementAsync(TherapistId, from, to);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetStatementAsync_MixedStatuses_PreservesAllRowsAndZerosOutNonBillableProvider()
    {
        // Arrange
        var patient = MakePatient(1, "Alice", "Anderson");
        var sessions = new List<TherapySession>
        {
            MakeSession(101, patient, new DateOnly(2026, 4, 1), new TimeOnly(9, 0), CompletedStatus,
                amount: 100m, discount: 10m, providerAmount: 60m),
            MakeSession(102, patient, new DateOnly(2026, 4, 2), new TimeOnly(10, 0), CancelledStatus,
                amount: 100m, discount: 0m, providerAmount: 60m),
            MakeSession(103, patient, new DateOnly(2026, 4, 3), new TimeOnly(11, 0), NoShowStatus,
                amount: 100m, discount: 0m, providerAmount: 60m),
            MakeSession(104, patient, new DateOnly(2026, 4, 4), new TimeOnly(12, 0), CompletedStatus,
                amount: 200m, discount: 20m, providerAmount: 120m),
        };
        var (service, _) = BuildService(sessions);

        // Act
        var result = await service.GetStatementAsync(
            TherapistId,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 4, 30));

        // Assert — (a) all rows present
        result.Should().NotBeNull();
        result!.Patients.Should().HaveCount(1);
        var block = result.Patients[0];
        block.Sessions.Should().HaveCount(4);

        // (b) ProviderAmount = 0 on non-Completed rows
        var cancelled = block.Sessions.Single(s => s.SessionId == 102);
        var noShow = block.Sessions.Single(s => s.SessionId == 103);
        cancelled.ProviderAmount.Should().Be(0m);
        cancelled.IsBillable.Should().BeFalse();
        noShow.ProviderAmount.Should().Be(0m);
        noShow.IsBillable.Should().BeFalse();

        // Completed rows retain provider amount
        var completed1 = block.Sessions.Single(s => s.SessionId == 101);
        var completed2 = block.Sessions.Single(s => s.SessionId == 104);
        completed1.ProviderAmount.Should().Be(60m);
        completed1.IsBillable.Should().BeTrue();
        completed2.ProviderAmount.Should().Be(120m);
        completed2.IsBillable.Should().BeTrue();

        // (c) EstimatedAmountDue equals sum of Completed ProviderAmount
        result.Summary.TotalProviderAmount.Should().Be(180m);
        result.Summary.EstimatedAmountDue.Should().Be(180m);
        result.Summary.TotalCompletedSessions.Should().Be(2);
        result.Summary.TotalNonBillableSessions.Should().Be(2);

        // Per-patient subtotals: SubtotalProviderAmount only sums Completed
        block.SubtotalProviderAmount.Should().Be(180m);
        block.CompletedCount.Should().Be(2);
        block.NonBillableCount.Should().Be(2);
        block.SubtotalFee.Should().Be(500m);       // 100+100+100+200
        block.SubtotalDiscount.Should().Be(30m);   // 10+0+0+20

        // Pro-forma flag + empty ServicePayments
        result.IsProForma.Should().BeTrue();
        result.ServicePayments.Should().BeEmpty();
        result.Summary.TotalServicePaymentsApplied.Should().Be(0m);
    }

    [Fact]
    public async Task GetStatementAsync_TherapyTypeFallbackChain_ResolvesAllThreeBranches()
    {
        // Arrange — build one session per branch of the fallback chain
        var specialty = new SpecialtyType { Id = 7, Name = "Speech Therapy" };
        var patient = MakePatient(1, "Alice", "Anderson");

        var sessions = new List<TherapySession>
        {
            // Branch 1: SpecialtyType set → use SpecialtyType.Name
            MakeSession(201, patient, new DateOnly(2026, 4, 1), new TimeOnly(9, 0), CompletedStatus,
                specialty: specialty,
                legacyTherapyTypes: "OldLegacyValue"),
            // Branch 2: SpecialtyType null, legacy text set → use legacy
            MakeSession(202, patient, new DateOnly(2026, 4, 2), new TimeOnly(10, 0), CompletedStatus,
                specialty: null,
                legacyTherapyTypes: "Legacy ABA"),
            // Branch 3: both null/empty → "N/A"
            MakeSession(203, patient, new DateOnly(2026, 4, 3), new TimeOnly(11, 0), CompletedStatus,
                specialty: null,
                legacyTherapyTypes: null),
        };
        var (service, _) = BuildService(sessions);

        // Act
        var result = await service.GetStatementAsync(
            TherapistId,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 4, 30));

        // Assert
        result.Should().NotBeNull();
        var rows = result!.Patients.Single().Sessions;

        rows.Single(r => r.SessionId == 201).TherapyType.Should().Be("Speech Therapy");
        rows.Single(r => r.SessionId == 202).TherapyType.Should().Be("Legacy ABA");
        rows.Single(r => r.SessionId == 203).TherapyType.Should().Be("N/A");
    }

    [Fact]
    public async Task GetStatementAsync_GroupsByPatient_AndSortsBlocksAlphabetically()
    {
        // Arrange — two patients, intentionally inserted out of alphabetical order
        var patientB = MakePatient(1, "Bob", "Brown");
        var patientA = MakePatient(2, "Alice", "Anderson");
        var sessions = new List<TherapySession>
        {
            MakeSession(301, patientB, new DateOnly(2026, 4, 1), new TimeOnly(9, 0), CompletedStatus, providerAmount: 50m),
            MakeSession(302, patientA, new DateOnly(2026, 4, 2), new TimeOnly(9, 0), CompletedStatus, providerAmount: 70m),
            MakeSession(303, patientA, new DateOnly(2026, 4, 1), new TimeOnly(8, 0), CompletedStatus, providerAmount: 30m),
        };
        var (service, _) = BuildService(sessions);

        // Act
        var result = await service.GetStatementAsync(
            TherapistId,
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 4, 30));

        // Assert
        result!.Patients.Should().HaveCount(2);
        result.Patients[0].PatientName.Should().StartWith("Anderson");
        result.Patients[1].PatientName.Should().StartWith("Brown");

        // Within Anderson, sessions sorted by date then time
        var andersonSessions = result.Patients[0].Sessions;
        andersonSessions[0].SessionId.Should().Be(303); // 4/1 08:00
        andersonSessions[1].SessionId.Should().Be(302); // 4/2 09:00
    }

    [Fact]
    public async Task GetStatementAsync_AlwaysSetsIsProFormaTrue_AndReturnsEmptyServicePayments()
    {
        // Arrange — empty session set
        var (service, _) = BuildService(new List<TherapySession>());

        // Act
        var result = await service.GetStatementAsync(TherapistId, null, null);

        // Assert
        result.Should().NotBeNull();
        result!.IsProForma.Should().BeTrue();
        result.ServicePayments.Should().NotBeNull();
        result.ServicePayments.Should().BeEmpty();
        result.Summary.EstimatedAmountDue.Should().Be(0m);
    }
}
