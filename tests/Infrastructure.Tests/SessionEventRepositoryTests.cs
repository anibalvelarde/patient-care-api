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

    // ── WP-29 (U3): GetAllPastDueAsync must filter in SQL, not materialize the table ──────
    //
    // The repository returns past-due CANDIDATES (money owed + session date at/before the
    // date-only cutoff — a strict superset of the exact date+time GetPastDue predicate);
    // SessionEventHandler applies the exact IsPastDue filter on the small remainder.

    private static TherapySession MoneySession(int id, Patient patient, Therapist therapist,
        DateOnly date, decimal amount, decimal discount, decimal paid) => new()
    {
        Id = id,
        Patient = patient,
        Therapist = therapist,
        SessionDate = date,
        SessionTime = new TimeOnly(9, 0),
        Amount = amount,
        DiscountAmount = discount,
        AmountPaid = paid,
    };

    [Fact]
    public async Task GetAllPastDueAsync_ReturnsOnlyOwedAndOldCandidates()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp29Candidates")
            .Options;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var oldDate = today.AddDays(-90);

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            var patient = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } };
            var therapist = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } };

            context.TherapySessions.AddRange(
                MoneySession(1, patient, therapist, oldDate, amount: 100, discount: 0, paid: 0),    // owed + old -> candidate
                MoneySession(2, patient, therapist, oldDate, amount: 100, discount: 20, paid: 80),  // fully settled -> excluded
                MoneySession(3, patient, therapist, today, amount: 100, discount: 0, paid: 0),      // owed but recent -> excluded
                MoneySession(4, patient, therapist, oldDate, amount: 100, discount: 100, paid: 0)); // discounted to zero -> excluded
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            var result = await repository.GetAllPastDueAsync();

            var candidate = Assert.Single(result);
            Assert.Equal(1, candidate.SessionId);
            Assert.True(candidate.IsPastDue);
        }
    }

    [Fact]
    public async Task GetAllPastDueAsync_FiltersByPatientOrTherapist()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp29PartyFilters")
            .Options;

        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-90);

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            var patient1 = new Patient { Id = 1, User = new User { FirstName = "John", LastName = "Doe" } };
            var patient2 = new Patient { Id = 2, User = new User { FirstName = "Alice", LastName = "Wonder" } };
            var therapist1 = new Therapist { Id = 1, User = new User { FirstName = "Jane", LastName = "Smith" } };
            var therapist2 = new Therapist { Id = 2, User = new User { FirstName = "Bob", LastName = "Builder" } };

            var s1 = MoneySession(1, patient1, therapist1, oldDate, amount: 100, discount: 0, paid: 0);
            s1.PatientId = 1; s1.TherapistId = 1;
            var s2 = MoneySession(2, patient2, therapist1, oldDate, amount: 200, discount: 0, paid: 0);
            s2.PatientId = 2; s2.TherapistId = 1;
            var s3 = MoneySession(3, patient2, therapist2, oldDate, amount: 300, discount: 0, paid: 0);
            s3.PatientId = 2; s3.TherapistId = 2;
            context.TherapySessions.AddRange(s1, s2, s3);
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SessionEventRepository(context);

            var forPatient2 = await repository.GetAllPastDueAsync(patientId: 2, therapistId: null);
            Assert.Equal(new[] { 2, 3 }, forPatient2.Select(se => se.SessionId).OrderBy(id => id).ToArray());

            var forTherapist1 = await repository.GetAllPastDueAsync(patientId: null, therapistId: 1);
            Assert.Equal(new[] { 1, 2 }, forTherapist1.Select(se => se.SessionId).OrderBy(id => id).ToArray());
        }
    }

    // WP-29 (U3): the slim owing-rows query behind pending-summary/report — allocation sum via
    // correlated subquery, owing-only filter in SQL.
    [Fact]
    public async Task GetOwedProviderSessionRowsAsync_SumsAllocations_AndKeepsOnlyOwingRows()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase_Wp29OwedRows")
            .Options;

        using (var context = new ApplicationDbContext(options))
        {
            context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });
            context.TherapySessions.AddRange(
                new TherapySession { Id = 1, TherapistId = 42, PatientId = 1, SessionDate = new DateOnly(2026, 4, 2), SessionTime = new TimeOnly(9, 0), Amount = 200m, ProviderAmount = 80m, AppointmentStatusId = 4 },
                new TherapySession { Id = 2, TherapistId = 42, PatientId = 2, SessionDate = new DateOnly(2026, 4, 10), SessionTime = new TimeOnly(9, 0), Amount = 120m, ProviderAmount = 60m, AppointmentStatusId = 4 },
                new TherapySession { Id = 3, TherapistId = 43, PatientId = 3, SessionDate = new DateOnly(2026, 4, 5), SessionTime = new TimeOnly(9, 0), Amount = 100m, ProviderAmount = 50m, AppointmentStatusId = 4 },
                // Not "Completed" -> excluded regardless of money.
                new TherapySession { Id = 4, TherapistId = 42, PatientId = 1, SessionDate = new DateOnly(2026, 4, 6), SessionTime = new TimeOnly(9, 0), Amount = 100m, ProviderAmount = 50m, AppointmentStatusId = 8 });
            context.SessionServicePayments.AddRange(
                new SessionServicePayment { Id = 1, ServicePaymentId = 99, TherapySessionId = 2, AmountApplied = 20m },  // partial -> 40 remains
                new SessionServicePayment { Id = 2, ServicePaymentId = 99, TherapySessionId = 3, AmountApplied = 30m },
                new SessionServicePayment { Id = 3, ServicePaymentId = 98, TherapySessionId = 3, AmountApplied = 20m }); // 30+20 -> fully paid, excluded
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new TherapySessionRepository(context);

            var rows = await repository.GetOwedProviderSessionRowsAsync(
                new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), new[] { 4 });

            Assert.Equal(2, rows.Count);
            var unpaid = rows.Single(r => r.SessionId == 1);
            Assert.Equal(0m, unpaid.Applied);
            Assert.Equal(80m, unpaid.ProviderAmount);
            var partial = rows.Single(r => r.SessionId == 2);
            Assert.Equal(20m, partial.Applied);
            Assert.Equal(40m, partial.ProviderAmount - partial.Applied);
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
