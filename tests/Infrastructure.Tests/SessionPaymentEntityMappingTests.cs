using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests.Repositories;

public class SessionPaymentEntityMappingTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public async Task SessionPayment_CanBePersisted_WithAmountAllocated()
    {
        // Arrange
        var options = CreateInMemoryOptions("SessionPayment_CanBePersisted_WithAmountAllocated");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test caretaker",
                User = new User { Id = 1, FirstName = "Alice", LastName = "Smith" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CSH",
                PmtTypeName = "Cash"
            });
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 2, FirstName = "Bob", LastName = "Jones" }
            });
            context.Therapists.Add(new Therapist
            {
                Id = 1,
                User = new User { Id = 3, FirstName = "Carol", LastName = "White" }
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SessionTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                Amount = 200m,
                Notes = "Test session"
            });
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                Amount = 200m,
                PaymentDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.SessionPayments.Add(new SessionPayment
            {
                Id = 1,
                PaymentId = 1,
                TherapySessionId = 1,
                AmountAllocated = 175.50m
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var sessionPayment = await context.SessionPayments.FindAsync(1);
            Assert.NotNull(sessionPayment);
            Assert.Equal(175.50m, sessionPayment.AmountAllocated);
            Assert.Equal(1, sessionPayment.PaymentId);
            Assert.Equal(1, sessionPayment.TherapySessionId);
        }
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(99.99)]
    [InlineData(1000.00)]
    public async Task SessionPayment_AmountAllocated_RoundTripsCorrectly(double amount)
    {
        // Arrange
        var decimalAmount = (decimal)amount;
        var dbName = $"SessionPayment_AmountAllocated_RoundTrip_{amount}";
        var options = CreateInMemoryOptions(dbName);

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test",
                User = new User { Id = 1, FirstName = "A", LastName = "B" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CSH",
                PmtTypeName = "Cash"
            });
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 2, FirstName = "C", LastName = "D" }
            });
            context.Therapists.Add(new Therapist
            {
                Id = 1,
                User = new User { Id = 3, FirstName = "E", LastName = "F" }
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SessionTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                Amount = 1000m,
                Notes = "Test"
            });
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                Amount = 1000m,
                PaymentDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.SessionPayments.Add(new SessionPayment
            {
                Id = 1,
                PaymentId = 1,
                TherapySessionId = 1,
                AmountAllocated = decimalAmount
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var sessionPayment = await context.SessionPayments.FindAsync(1);
            Assert.NotNull(sessionPayment);
            Assert.Equal(decimalAmount, sessionPayment.AmountAllocated);
        }
    }

    [Fact]
    public async Task SessionPayment_NavigationProperties_LoadCorrectly()
    {
        // Arrange
        var options = CreateInMemoryOptions("SessionPayment_NavigationProperties_LoadCorrectly");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test caretaker",
                User = new User { Id = 1, FirstName = "Alice", LastName = "Smith" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CHK",
                PmtTypeName = "Check"
            });
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 2, FirstName = "Bob", LastName = "Jones" }
            });
            context.Therapists.Add(new Therapist
            {
                Id = 1,
                User = new User { Id = 3, FirstName = "Carol", LastName = "White" }
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SessionTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                Amount = 100m,
                Notes = "Therapy session"
            });
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                Amount = 100m,
                CheckNumber = "5678",
                PaymentDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.SessionPayments.Add(new SessionPayment
            {
                Id = 1,
                PaymentId = 1,
                TherapySessionId = 1,
                AmountAllocated = 100m
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var sessionPayment = await context.SessionPayments
                .Include(sp => sp.Payment)
                .Include(sp => sp.TherapySession)
                .FirstAsync(sp => sp.Id == 1);

            Assert.NotNull(sessionPayment.Payment);
            Assert.Equal(1, sessionPayment.Payment.Id);
            Assert.Equal(100m, sessionPayment.Payment.Amount);

            Assert.NotNull(sessionPayment.TherapySession);
            Assert.Equal(1, sessionPayment.TherapySession.Id);
            Assert.Equal("Therapy session", sessionPayment.TherapySession.Notes);
        }
    }
}
