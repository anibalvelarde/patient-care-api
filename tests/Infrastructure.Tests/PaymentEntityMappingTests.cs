using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests.Repositories;

public class PaymentEntityMappingTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public async Task Payment_CanBePersisted_WithAllProperties()
    {
        // Arrange
        var options = CreateInMemoryOptions("Payment_CanBePersisted_WithAllProperties");
        var paymentDate = DateTime.UtcNow;

        using (var context = new ApplicationDbContext(options))
        {
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CHK",
                PmtTypeName = "Check"
            });
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test caretaker",
                User = new User { Id = 1, FirstName = "Alice", LastName = "Smith" }
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                CheckNumber = "1234",
                Amount = 150.00m,
                PaymentDate = paymentDate
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var payment = await context.Payments.FindAsync(1);
            Assert.NotNull(payment);
            Assert.Equal(1, payment.CaretakerId);
            Assert.Equal(1, payment.PaymentTypeId);
            Assert.Equal("1234", payment.CheckNumber);
            Assert.Equal(150.00m, payment.Amount);
            Assert.Equal(paymentDate, payment.PaymentDate);
        }
    }

    [Fact]
    public async Task Payment_CaretakerNavigation_LoadsCorrectly()
    {
        // Arrange
        var options = CreateInMemoryOptions("Payment_CaretakerNavigation_LoadsCorrectly");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Primary caretaker",
                User = new User { Id = 1, FirstName = "Bob", LastName = "Jones" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CSH",
                PmtTypeName = "Cash"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                Amount = 200.00m,
                PaymentDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var payment = await context.Payments
                .Include(p => p.Caretaker)
                .FirstAsync(p => p.Id == 1);

            Assert.NotNull(payment.Caretaker);
            Assert.Equal(1, payment.Caretaker.Id);
        }
    }

    [Fact]
    public async Task Payment_SessionPaymentsCollection_LoadsCorrectly()
    {
        // Arrange
        var options = CreateInMemoryOptions("Payment_SessionPaymentsCollection_LoadsCorrectly");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test caretaker",
                User = new User { Id = 1, FirstName = "Carol", LastName = "White" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CC",
                PmtTypeName = "Credit Card"
            });
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 2, FirstName = "Dan", LastName = "Green" }
            });
            context.Therapists.Add(new Therapist
            {
                Id = 1,
                User = new User { Id = 3, FirstName = "Eve", LastName = "Brown" }
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SessionTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                Amount = 100m,
                Notes = "Session 1"
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 2,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                SessionTime = TimeOnly.FromDateTime(DateTime.UtcNow),
                Amount = 100m,
                Notes = "Session 2"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                Amount = 150.00m,
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
                AmountAllocated = 100.00m
            });
            context.SessionPayments.Add(new SessionPayment
            {
                Id = 2,
                PaymentId = 1,
                TherapySessionId = 2,
                AmountAllocated = 50.00m
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var payment = await context.Payments
                .Include(p => p.SessionPayments)
                .FirstAsync(p => p.Id == 1);

            Assert.NotNull(payment.SessionPayments);
            Assert.Equal(2, payment.SessionPayments.Count);
            Assert.Contains(payment.SessionPayments, sp => sp.AmountAllocated == 100.00m);
            Assert.Contains(payment.SessionPayments, sp => sp.AmountAllocated == 50.00m);
        }
    }

    [Fact]
    public async Task Payment_CheckNumber_CanBeNull()
    {
        // Arrange
        var options = CreateInMemoryOptions("Payment_CheckNumber_CanBeNull");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Test caretaker",
                User = new User { Id = 1, FirstName = "Frank", LastName = "Miller" }
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CSH",
                PmtTypeName = "Cash"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.Payments.Add(new Payment
            {
                Id = 1,
                CaretakerId = 1,
                PaymentTypeId = 1,
                CheckNumber = null,
                Amount = 75.00m,
                PaymentDate = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var payment = await context.Payments.FindAsync(1);
            Assert.NotNull(payment);
            Assert.Null(payment.CheckNumber);
        }
    }
}
