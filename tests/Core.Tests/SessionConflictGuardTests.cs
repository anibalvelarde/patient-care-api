using Moq;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>WP-59 (F-2): the overlap rule and the double-booking guard.</summary>
public class SessionConflictGuardTests
{
    private static readonly DateOnly Day = DateOnly.Parse("2026-11-09");

    [Theory]
    [InlineData("10:00", 30, "10:00", 30, true)]   // same slot
    [InlineData("10:00", 60, "10:30", 30, true)]   // starts inside
    [InlineData("10:30", 30, "10:00", 60, true)]   // contained
    [InlineData("10:00", 30, "10:30", 30, false)]  // back-to-back
    [InlineData("10:30", 30, "10:00", 30, false)]  // back-to-back, other order
    [InlineData("09:00", 45, "09:30", 30, true)]   // tail overlap
    [InlineData("23:30", 60, "23:45", 15, true)]   // late legacy row does not wrap past midnight
    public void Overlaps_HalfOpenRanges(string s1, int d1, string s2, int d2, bool expected)
    {
        Assert.Equal(expected, SessionSlot.Overlaps(TimeOnly.Parse(s1), d1, TimeOnly.Parse(s2), d2));
    }

    [Theory]
    [InlineData(SessionStatus.Proposed, true)]
    [InlineData(SessionStatus.Confirmed, true)]
    [InlineData(SessionStatus.Completed, true)]
    [InlineData(SessionStatus.NoShow, true)]
    [InlineData(SessionStatus.CheckedIn, true)]
    [InlineData(SessionStatus.InTherapy, true)]
    [InlineData(SessionStatus.Cancelled, false)]
    public void OccupiesSlot_EverythingButCancelled(int status, bool expected)
    {
        Assert.Equal(expected, SessionSlot.OccupiesSlot(status));
    }

    private static TherapySession Existing(int id, int therapistId, int patientId, string time, int duration,
        int status = SessionStatus.Confirmed) => new()
    {
        Id = id, TherapistId = therapistId, PatientId = patientId, SessionDate = Day,
        SessionTime = TimeOnly.Parse(time), Duration = duration, AppointmentStatusId = status,
        Therapist = new Therapist { User = new User { FirstName = "Ana", LastName = "Synthetic" } },
    };

    private static Mock<ITherapySessionRepository> Repo(params TherapySession[] rows)
    {
        var repo = new Mock<ITherapySessionRepository>();
        repo.Setup(r => r.GetSlotCandidatesAsync(Day, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync(rows);
        return repo;
    }

    [Fact]
    public async Task SameTherapistSameSlot_Throws409_NamingTherapistAndSession()
    {
        var repo = Repo(Existing(12359, therapistId: 3, patientId: 922, "10:00", 30));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:00"), 30, therapistId: 3, patientId: 500, excludeSessionId: null));

        Assert.Contains("Therapist Ana Synthetic already has a session on 2026-11-09 from 10:00 to 10:30 (session #12359)", ex.Message);
    }

    [Fact]
    public async Task SamePatientOtherTherapist_Overlapping_Throws409()
    {
        var repo = Repo(Existing(40, therapistId: 7, patientId: 922, "10:00", 60));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:30"), 30, therapistId: 3, patientId: 922, excludeSessionId: null));

        Assert.StartsWith("This patient already has a session on 2026-11-09 from 10:00 to 11:00 with Ana Synthetic (session #40)", ex.Message);
    }

    [Fact]
    public async Task BackToBack_Passes()
    {
        var repo = Repo(Existing(40, therapistId: 3, patientId: 922, "10:00", 30));

        await SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:30"), 30, therapistId: 3, patientId: 922, excludeSessionId: null);
    }

    [Fact]
    public async Task CancelledRow_DoesNotBlock_EvenIfRepositoryReturnsIt()
    {
        var repo = Repo(Existing(40, therapistId: 3, patientId: 922, "10:00", 30, SessionStatus.Cancelled));

        await SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:00"), 30, therapistId: 3, patientId: 922, excludeSessionId: null);
    }

    [Fact]
    public async Task OwnRow_IsExcluded()
    {
        var repo = Repo(Existing(77, therapistId: 3, patientId: 922, "10:00", 30));

        await SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:00"), 60, therapistId: 3, patientId: 922, excludeSessionId: 77);
    }

    [Fact]
    public async Task UnrelatedSessions_DoNotBlock()
    {
        // same time, but a different therapist AND a different patient
        var repo = Repo(Existing(40, therapistId: 9, patientId: 111, "10:00", 30));

        await SessionConflictGuard.EnsureNoConflictAsync(
            repo.Object, Day, TimeOnly.Parse("10:00"), 30, therapistId: 3, patientId: 922, excludeSessionId: null);
    }
}
