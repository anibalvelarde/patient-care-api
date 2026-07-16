using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

// WP-31 (U1): every profile/event mapper attaches the audit block. Critically, BOTH SessionEvent
// mappers (SessionEventRepository + BookingRepository) carry it — the standing two-mapper contract
// that B3 once broke. The DbContext stamps LastUpdatedByUserId from ICurrentUserService on save, so
// we seed through a context whose current user is a known id and assert the mapper surfaces it.
// UpdatedBy stays "System" here (name resolution is a service-layer concern); the timestamp/updater
// selection logic itself is unit-tested in AuditInfoTests.
public class AuditPopoverMapperTests
{
    private const int SeedUserId = 42;

    private static DbContextOptions<ApplicationDbContext> NewDb(string tag) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"AuditMapper_{tag}_" + Guid.NewGuid())
            .Options;

    private static ApplicationDbContext SeedingContext(DbContextOptions<ApplicationDbContext> options)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(SeedUserId);
        return new ApplicationDbContext(options, currentUser.Object);
    }

    [Fact]
    public async Task SessionEventRepository_ExtractSessionEvent_CarriesAudit()
    {
        var options = NewDb("SER");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var ctx = SeedingContext(options))
        {
            ctx.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "done" });
            ctx.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                Patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } },
                Therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                SessionDate = date,
                SessionTime = new TimeOnly(9, 0),
                Notes = "n",
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var se = (await new SessionEventRepository(ctx).GetAllByTargetDateAsync(date)).Single();

            Assert.NotNull(se.Audit);
            Assert.NotEqual(default, se.Audit!.CreatedAt);
            Assert.NotEqual(default, se.Audit.UpdatedAt);
            Assert.Equal(SeedUserId, se.Audit.UpdatedByUserId);
            Assert.Equal("System", se.Audit.UpdatedBy); // repo layer does not resolve names
        }
    }

    [Fact]
    public async Task BookingRepository_MapToSessionEvent_CarriesAudit()
    {
        var options = NewDb("Booking");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var ctx = SeedingContext(options))
        {
            ctx.AppointmentStatuses.Add(new AppointmentStatus { Id = 1, Name = "Proposed", Description = "req" });
            ctx.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                Patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } },
                Therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                SessionDate = date,
                SessionTime = new TimeOnly(9, 0),
                AppointmentStatusId = 1,
                Notes = "n",
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var se = (await new BookingRepository(ctx).GetByStatusAndDateRangeAsync(1, date, date)).Single();

            Assert.NotNull(se.Audit);
            Assert.NotEqual(default, se.Audit!.CreatedAt);
            Assert.Equal(SeedUserId, se.Audit.UpdatedByUserId);
        }
    }

    [Fact]
    public async Task PatientProfileRepository_CarriesAggregateAudit()
    {
        var options = NewDb("Patient");
        using (var ctx = SeedingContext(options))
        {
            ctx.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 10, FirstName = "John", LastName = "Doe" },
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var profile = await new PatientProfileRepository(ctx).GetByIdAsync(1);

            Assert.NotNull(profile!.Audit);
            Assert.NotEqual(default, profile.Audit!.CreatedAt);
            Assert.NotEqual(default, profile.Audit.UpdatedAt);
            Assert.Equal(SeedUserId, profile.Audit.UpdatedByUserId);
        }
    }

    [Fact]
    public async Task CaretakerProfileRepository_CarriesAggregateAudit()
    {
        var options = NewDb("Caretaker");
        using (var ctx = SeedingContext(options))
        {
            ctx.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "n",
                User = new User { Id = 20, FirstName = "Mary", LastName = "Doe" },
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var profile = await new CaretakerProfileRepository(ctx).GetByIdAsync(1);

            Assert.NotNull(profile!.Audit);
            Assert.Equal(SeedUserId, profile.Audit!.UpdatedByUserId);
        }
    }
}
