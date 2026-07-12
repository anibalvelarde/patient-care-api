using Moq;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

public class SessionEventRepositoryTests
{
    [Fact]
    public async Task GetAllByTargetDateAsync_ReturnsCorrectSessionEvents()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase")
            .Options;

        var targetDateTime = DateTime.UtcNow;

        using (var context = new ApplicationDbContext(options))
        {
            // Seed appointment statuses needed for the Include
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            await context.SaveChangesAsync();

            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                Patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } },
                Therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                SessionDate = DateOnly.FromDateTime(targetDateTime),
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                TherapyTypes = "TherapyType1",
                Amount = 100,
                DiscountAmount = 10,
                AmountPaid = 90,
                IsPaidOff = true,
                Notes = "Session Note"
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 2,
                Patient = new Patient { Id = 2, User = new User { FirstName = "Alice", LastName = "Wonder" } },
                Therapist = new Therapist { Id = 2, User = new User { FirstName = "Bob", LastName = "Builder" } },
                SessionDate = DateOnly.FromDateTime(targetDateTime),
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                TherapyTypes = "TherapyType2",
                Amount = 200,
                DiscountAmount = 20,
                AmountPaid = 90,
                IsPaidOff = false,
                Notes = "Another Session Note"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            // Act
            var result = await repository.GetAllByTargetDateAsync(DateOnly.FromDateTime(targetDateTime));

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            // ---------------- Session #1
            Assert.Contains(result, se => se.SessionId == 1);
            Assert.Contains(result, se => se.Patient == "Doe, John");
            Assert.Contains(result, se => se.PatientId == 1);
            Assert.Contains(result, se => se.TherapistId == 1);
            Assert.Contains(result, se => se.Therapist == "Smith, Jane");
            Assert.Contains(result, se => se.TherapyTypes == "TherapyType1");
            Assert.Contains(result, se => se.IsPaidOff);
            Assert.Contains(result, se => se.Amount == 100);
            Assert.Contains(result, se => se.Discount == 10);
            Assert.Contains(result, se => se.AmountPaid == 90);
            Assert.Contains(result, se => se.AmountDue == 0);
            Assert.Contains(result, se => se.Notes == "Session Note");
            Assert.Contains(result, se => !se.IsPastDue);
            // ---------------- Session #2
            Assert.Contains(result, se => se.SessionId == 2);
            Assert.Contains(result, se => se.PatientId == 2);
            Assert.Contains(result, se => se.Patient == "Wonder, Alice");            
            Assert.Contains(result, se => se.TherapistId == 2);            
            Assert.Contains(result, se => se.Therapist == "Builder, Bob");            
            Assert.Contains(result, se => se.TherapyTypes == "TherapyType2");            
            Assert.Contains(result, se => !se.IsPaidOff);            
            Assert.Contains(result, se => se.Amount == 200);            
            Assert.Contains(result, se => se.Discount == 20);            
            Assert.Contains(result, se => se.AmountPaid == 90);            
            Assert.Contains(result, se => se.AmountDue == 90);            
            Assert.Contains(result, se => se.Notes == "Another Session Note");
            Assert.Contains(result, se => !se.IsPastDue);
        }
    }

    // ── WP-21 (F1): GetByPatientIdAsync paging ───────────────────────────────────────

    private static TherapySession Session(int id, int patientId, Patient patient, Therapist therapist,
        DateOnly date, TimeOnly time, decimal amount = 100) => new()
    {
        Id = id,
        PatientId = patientId,
        Patient = patient,
        Therapist = therapist,
        SessionDate = date,
        SessionTime = time,
        Amount = amount,
    };

    [Fact]
    public async Task GetByPatientIdAsync_PagesNewestFirst_WithStableTiebreaks()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp21Paging")
            .Options;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            var patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } };
            var therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } };

            // Three dates + a same-date/different-time pair + a same-date/same-time id tie.
            context.TherapySessions.AddRange(
                Session(1, 1, patient, therapist, new DateOnly(2026, 5, 1), new TimeOnly(9, 0)),
                Session(2, 1, patient, therapist, new DateOnly(2026, 7, 1), new TimeOnly(9, 0)),
                Session(3, 1, patient, therapist, new DateOnly(2026, 7, 1), new TimeOnly(14, 0)),
                Session(4, 1, patient, therapist, new DateOnly(2026, 6, 1), new TimeOnly(9, 0)),
                Session(5, 1, patient, therapist, new DateOnly(2026, 7, 1), new TimeOnly(14, 0)));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            // Newest first: date DESC, time DESC, id DESC → 5, 3, 2, 4, 1.
            var page1 = await repository.GetByPatientIdAsync(1, page: 1, pageSize: 2);
            Assert.Equal(5, page1.TotalCount);
            Assert.Equal(1, page1.Page);
            Assert.Equal(2, page1.PageSize);
            Assert.Equal(new[] { 5, 3 }, page1.Items.Select(se => se.SessionId).ToArray());

            var page2 = await repository.GetByPatientIdAsync(1, page: 2, pageSize: 2);
            Assert.Equal(5, page2.TotalCount);
            Assert.Equal(new[] { 2, 4 }, page2.Items.Select(se => se.SessionId).ToArray());

            var page3 = await repository.GetByPatientIdAsync(1, page: 3, pageSize: 2);
            Assert.Equal(new[] { 1 }, page3.Items.Select(se => se.SessionId).ToArray());
        }
    }

    [Fact]
    public async Task GetByPatientIdAsync_FiltersAndTotalCountAgree()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp21Filters")
            .Options;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.AddRange(
                new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" },
                new AppointmentStatus { Id = 8, Name = "Cancelled", Description = "Session cancelled" });
            var patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } };
            var therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } };

            var completed = Session(1, 1, patient, therapist, new DateOnly(2026, 7, 1), new TimeOnly(9, 0));
            completed.AppointmentStatusId = 4;
            var cancelled = Session(2, 1, patient, therapist, new DateOnly(2026, 7, 2), new TimeOnly(9, 0));
            cancelled.AppointmentStatusId = 8;
            var otherPatientSession = Session(3, 2,
                new Patient { Id = 2, User = new User { FirstName = "Alice", LastName = "Wonder" } },
                therapist, new DateOnly(2026, 7, 3), new TimeOnly(9, 0));
            otherPatientSession.AppointmentStatusId = 4;

            context.TherapySessions.AddRange(completed, cancelled, otherPatientSession);
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            // status filter narrows both the items AND the totalCount (not the patient's full set).
            var filtered = await repository.GetByPatientIdAsync(1, page: 1, pageSize: 25, status: "Completed");
            Assert.Equal(1, filtered.TotalCount);
            Assert.Equal(1, Assert.Single(filtered.Items).SessionId);

            var unfiltered = await repository.GetByPatientIdAsync(1, page: 1, pageSize: 25);
            Assert.Equal(2, unfiltered.TotalCount);
        }
    }

    [Fact]
    public async Task GetByPatientIdAsync_PageBeyondEnd_ReturnsEmptyItemsWithTotalCount()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp21BeyondEnd")
            .Options;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            context.TherapySessions.Add(Session(1, 1,
                new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } },
                new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } },
                new DateOnly(2026, 7, 1), new TimeOnly(9, 0)));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            var result = await repository.GetByPatientIdAsync(1, page: 5, pageSize: 25);

            Assert.Empty(result.Items);
            Assert.Equal(1, result.TotalCount);
            Assert.Equal(5, result.Page);
        }
    }

    // B3 (2026-07-07 punch list): Session Details must carry the primary caretaker's
    // name/phone/email so the Proposed > Session Details panel can display them.
    [Fact]
    public async Task GetAllByTargetDateAsync_ProjectsPrimaryCaretakerContactInfo()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_B3Caretaker")
            .Options;

        var targetDateTime = DateTime.UtcNow;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });

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
                SessionDate = DateOnly.FromDateTime(targetDateTime),
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                Amount = 100,
                Notes = "With caretakers"
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 2,
                Patient = new Patient { Id = 2, User = new User { FirstName = "Alice", LastName = "Wonder" } },
                Therapist = new Therapist { Id = 2, User = new User { FirstName = "Bob", LastName = "Builder" } },
                SessionDate = DateOnly.FromDateTime(targetDateTime),
                SessionTime = TimeOnly.FromDateTime(targetDateTime),
                Amount = 200,
                Notes = "No caretakers"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            var result = await repository.GetAllByTargetDateAsync(DateOnly.FromDateTime(targetDateTime));

            Assert.Equal(2, result.Count);

            // The primary caretaker wins over the non-primary link.
            var withCaretaker = result.Single(se => se.SessionId == 1);
            Assert.Equal("Doe, Mary", withCaretaker.CaretakerName);
            Assert.Equal("555-0001", withCaretaker.CaretakerPhone);
            Assert.Equal("mary@example.com", withCaretaker.CaretakerEmail);

            // No caretaker links -> nulls, not an exception.
            var withoutCaretaker = result.Single(se => se.SessionId == 2);
            Assert.Null(withoutCaretaker.CaretakerName);
            Assert.Null(withoutCaretaker.CaretakerPhone);
            Assert.Null(withoutCaretaker.CaretakerEmail);
        }
    }
}
