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
}
