using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

public class BookingRepositoryTests
{
    // B3 follow-up: the booking endpoints (/unconfirmed, /upcoming, confirm/cancel responses)
    // are served by BookingRepository's own SessionEvent mapper, which had drifted from the
    // contract — it carried neither the caretaker fields (B3) nor the WP-9B specialty fields.
    [Fact]
    public async Task GetByStatusAndDateRangeAsync_ProjectsCaretakerAndSpecialtyFields()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_BookingB3")
            .Options;

        var targetDateTime = DateTime.UtcNow;
        var targetDate = DateOnly.FromDateTime(targetDateTime);

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 1, Name = "Proposed", Description = "Requested, not yet confirmed" });
            context.SpecialtyTypes.Add(new SpecialtyType { Id = 14, Abbreviation = "NM", Name = "Neuro Motor", IsDiscovery = false });

            var patientWithCaretakers = new Patient
            {
                Id = 1,
                User = new User { FirstName = "John", LastName = "Doe" },
                Caretakers = new List<PatientCaretaker>
                {
                    new PatientCaretaker
                    {
                        PatientId = 1,
                        CaretakerId = 1,
                        PrimaryCaretaker = false,
                        Caretaker = new Caretaker
                        {
                            Id = 1,
                            User = new User { FirstName = "Backup", LastName = "Uncle", PhoneNumber = "555-0002", Email = "uncle@example.com" },
                        },
                    },
                    new PatientCaretaker
                    {
                        PatientId = 1,
                        CaretakerId = 2,
                        PrimaryCaretaker = true,
                        Caretaker = new Caretaker
                        {
                            Id = 2,
                            User = new User { FirstName = "Mary", LastName = "Doe", PhoneNumber = "555-0001", Email = "mary@example.com" },
                        },
                    },
                },
            };

            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                Patient = patientWithCaretakers,
                Therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                SessionDate = targetDate,
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                AppointmentStatusId = 1,
                SpecialtyTypeId = 14,
                Amount = 100,
                Notes = "Proposed with caretakers"
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 2,
                Patient = new Patient { Id = 2, User = new User { FirstName = "Alice", LastName = "Wonder" } },
                Therapist = new Therapist { Id = 2, User = new User { FirstName = "Bob", LastName = "Builder" } },
                SessionDate = targetDate,
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                AppointmentStatusId = 1,
                Amount = 200,
                Notes = "Proposed without caretakers"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new BookingRepository(context);

            var result = (await repository.GetByStatusAndDateRangeAsync(1, targetDate, targetDate)).ToList();

            Assert.Equal(2, result.Count);

            var withCaretaker = result.Single(se => se.SessionId == 1);
            Assert.Equal("Doe, Mary", withCaretaker.CaretakerName);
            Assert.Equal("555-0001", withCaretaker.CaretakerPhone);
            Assert.Equal("mary@example.com", withCaretaker.CaretakerEmail);
            Assert.Equal(14, withCaretaker.SpecialtyTypeId);
            Assert.Equal("NM", withCaretaker.SpecialtyAbbreviation);
            Assert.Equal("Neuro Motor", withCaretaker.SpecialtyName);
            Assert.False(withCaretaker.IsDiscovery);

            var withoutCaretaker = result.Single(se => se.SessionId == 2);
            Assert.Null(withoutCaretaker.CaretakerName);
            Assert.Null(withoutCaretaker.CaretakerPhone);
            Assert.Null(withoutCaretaker.CaretakerEmail);
        }
    }

    [Fact]
    public async Task GetSessionEventByIdAsync_ProjectsCaretakerFields()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_BookingB3_ById")
            .Options;

        var targetDateTime = DateTime.UtcNow;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 1, Name = "Proposed", Description = "Requested" });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 5,
                Patient = new Patient
                {
                    Id = 1,
                    User = new User { FirstName = "John", LastName = "Doe" },
                    Caretakers = new List<PatientCaretaker>
                    {
                        new PatientCaretaker
                        {
                            PatientId = 1,
                            CaretakerId = 1,
                            PrimaryCaretaker = true,
                            Caretaker = new Caretaker
                            {
                                Id = 1,
                                User = new User { FirstName = "Mary", LastName = "Doe", PhoneNumber = "555-0001", Email = "mary@example.com" },
                            },
                        },
                    },
                },
                Therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                SessionDate = DateOnly.FromDateTime(targetDateTime),
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                AppointmentStatusId = 1,
                Amount = 100,
                Notes = ""
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new BookingRepository(context);

            var result = await repository.GetSessionEventByIdAsync(5);

            Assert.NotNull(result);
            Assert.Equal("Doe, Mary", result!.CaretakerName);
            Assert.Equal("555-0001", result.CaretakerPhone);
            Assert.Equal("mary@example.com", result.CaretakerEmail);
        }
    }
}
