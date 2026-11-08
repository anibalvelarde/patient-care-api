using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-59 (E2E finding F-2): the double-booking guard. A therapist may not have two overlapping
/// busy sessions, and neither may a patient (with any therapist). Hard block, no override
/// (owner rulings 2026-11-08). Throws <see cref="ConflictException"/> → 409 ProblemDetails.
///
/// <para>Static over the session repository on purpose: every caller already holds one, so the
/// guard needs no new DI wiring and no new constructor parameter.</para>
///
/// <para>Residual (accepted): check-then-insert, so two near-simultaneous identical requests could
/// both pass. There is no DB constraint that can express overlapping time ranges.</para>
/// </summary>
public static class SessionConflictGuard
{
    public static async Task EnsureNoConflictAsync(
        ITherapySessionRepository repository,
        DateOnly date, TimeOnly time, int duration,
        int therapistId, int patientId, int? excludeSessionId)
    {
        var candidates = await repository.GetSlotCandidatesAsync(date, therapistId, patientId, excludeSessionId)
            ?? Array.Empty<TherapySession>();

        var clashes = candidates
            .Where(s => s.Id != excludeSessionId
                && s.SessionDate == date
                && SessionSlot.OccupiesSlot(s.AppointmentStatusId)
                && SessionSlot.Overlaps(s.SessionTime, s.Duration, time, duration))
            .OrderBy(s => s.SessionTime)
            .ToList();

        var therapistClash = clashes.FirstOrDefault(s => s.TherapistId == therapistId);
        if (therapistClash is not null)
        {
            var name = PersonName(therapistClash.Therapist?.User);
            throw new ConflictException(
                $"Therapist {(name.Length > 0 ? name : $"#{therapistId}")} already has a session on {date:yyyy-MM-dd} " +
                $"from {Range(therapistClash)} (session #{therapistClash.Id}). Choose another time or therapist.");
        }

        var patientClash = clashes.FirstOrDefault(s => s.PatientId == patientId);
        if (patientClash is not null)
        {
            var with = PersonName(patientClash.Therapist?.User);
            throw new ConflictException(
                $"This patient already has a session on {date:yyyy-MM-dd} from {Range(patientClash)}" +
                $"{(with.Length > 0 ? $" with {with}" : string.Empty)} (session #{patientClash.Id}).");
        }
    }

    public static string Range(TherapySession s) => Range(s.SessionTime, s.Duration);

    public static string Range(TimeOnly start, int duration) =>
        $"{start:HH:mm} to {start.AddMinutes(duration):HH:mm}";

    private static string PersonName(User? user) =>
        user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();
}
