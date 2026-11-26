using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-63B (B6): GetUnpaidByPatientIdsAsync must load SpecialtyType + AppointmentStatus so the
/// unpaid-sessions picker can show specialtyName / statusName.
/// </summary>
public class Wp63UnpaidSessionsRepositoryTests
{
    [Fact]
    public async Task GetUnpaidByPatientIds_LoadsSpecialtyAndStatus()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: nameof(GetUnpaidByPatientIds_LoadsSpecialtyAndStatus)).Options;
        using (var seed = new ApplicationDbContext(options))
        {
            seed.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "x" });
            seed.Set<SpecialtyType>().Add(new SpecialtyType { Id = 6, Abbreviation = "TL", Name = "Language Therapy" });
            seed.Patients.Add(new Patient { Id = 63001, User = new User { FirstName = "Pat", LastName = "Synthetic" } });
            seed.Set<Therapist>().Add(new Therapist { Id = 63002, User = new User { FirstName = "Ana", LastName = "Synthetic" } });
            seed.TherapySessions.Add(new TherapySession
            {
                Id = 63101, PatientId = 63001, TherapistId = 63002, SessionDate = new DateOnly(2026, 11, 9),
                SessionTime = new TimeOnly(10, 0), Amount = 50m, AppointmentStatusId = 4, SpecialtyTypeId = 6,
                Notes = "", TherapyTypes = "TL",
            });
            await seed.SaveChangesAsync();
        }

        using var context = new ApplicationDbContext(options);
        var rows = await new TherapySessionRepository(context).GetUnpaidByPatientIdsAsync(new[] { 63001 });

        var row = Assert.Single(rows);
        Assert.Equal("Language Therapy", row.SpecialtyType?.Name);
        Assert.Equal("Completed", row.AppointmentStatus?.Name);
    }
}
