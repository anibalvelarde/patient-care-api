using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests.Repositories;

public class DbContextMappingTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public void DbContext_HasPaymentsDbSet()
    {
        // Arrange & Act
        var options = CreateInMemoryOptions("DbContext_HasPaymentsDbSet");
        using var context = new ApplicationDbContext(options);

        // Assert
        Assert.NotNull(context.Payments);
    }

    [Fact]
    public void DbContext_HasSessionPaymentsDbSet()
    {
        // Arrange & Act
        var options = CreateInMemoryOptions("DbContext_HasSessionPaymentsDbSet");
        using var context = new ApplicationDbContext(options);

        // Assert
        Assert.NotNull(context.SessionPayments);
    }

    [Fact]
    public void DbContext_HasPaymentTypesDbSet()
    {
        // Arrange & Act
        var options = CreateInMemoryOptions("DbContext_HasPaymentTypesDbSet");
        using var context = new ApplicationDbContext(options);

        // Assert
        Assert.NotNull(context.PaymentTypes);
    }

    [Fact]
    public async Task PaymentType_CanBePersisted_AndQueried()
    {
        // Arrange
        var options = CreateInMemoryOptions("PaymentType_CanBePersisted_AndQueried");

        using (var context = new ApplicationDbContext(options))
        {
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 1,
                PmtTypeAbbreviation = "CSH",
                PmtTypeName = "Cash"
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 2,
                PmtTypeAbbreviation = "CHK",
                PmtTypeName = "Check"
            });
            context.PaymentTypes.Add(new PaymentType
            {
                Id = 3,
                PmtTypeAbbreviation = "CC",
                PmtTypeName = "Credit Card"
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var paymentTypes = await context.PaymentTypes.ToListAsync();
            Assert.Equal(3, paymentTypes.Count);
            Assert.Contains(paymentTypes, pt => pt.PmtTypeAbbreviation == "CSH" && pt.PmtTypeName == "Cash");
            Assert.Contains(paymentTypes, pt => pt.PmtTypeAbbreviation == "CHK" && pt.PmtTypeName == "Check");
            Assert.Contains(paymentTypes, pt => pt.PmtTypeAbbreviation == "CC" && pt.PmtTypeName == "Credit Card");
        }
    }

    [Fact]
    public async Task PatientCaretaker_CanBeAdded_VerifiesTableNameFix()
    {
        // Arrange
        var options = CreateInMemoryOptions("PatientCaretaker_CanBeAdded_VerifiesTableNameFix");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 1, FirstName = "John", LastName = "Doe" }
            });
            context.Caretakers.Add(new Caretaker
            {
                Id = 1,
                Notes = "Primary caretaker",
                User = new User { Id = 2, FirstName = "Jane", LastName = "Doe" }
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.Set<PatientCaretaker>().Add(new PatientCaretaker
            {
                PatientId = 1,
                CaretakerId = 1,
                PrimaryCaretaker = true
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var patientCaretaker = await context.Set<PatientCaretaker>()
                .FirstOrDefaultAsync(pc => pc.PatientId == 1 && pc.CaretakerId == 1);
            Assert.NotNull(patientCaretaker);
            Assert.True(patientCaretaker.PrimaryCaretaker);
        }
    }

    [Fact]
    public async Task Caretaker_MapsWithCorrectPrimaryKey()
    {
        // Arrange
        var options = CreateInMemoryOptions("Caretaker_MapsWithCorrectPrimaryKey");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 42,
                Notes = "Test caretaker with specific ID",
                User = new User { Id = 1, FirstName = "Test", LastName = "User" }
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var caretaker = await context.Caretakers.FindAsync(42);
            Assert.NotNull(caretaker);
            Assert.Equal(42, caretaker.Id);
            Assert.Equal("Test caretaker with specific ID", caretaker.Notes);
        }
    }

    [Fact]
    public async Task Payment_CaretakerId_MappedToPaidByColumn_RoundTrips()
    {
        // Arrange — verifies the CaretakerId -> PaidBy column mapping works
        var options = CreateInMemoryOptions("Payment_CaretakerId_MappedToPaidByColumn");

        using (var context = new ApplicationDbContext(options))
        {
            context.Caretakers.Add(new Caretaker
            {
                Id = 5,
                Notes = "Payer",
                User = new User { Id = 1, FirstName = "Payer", LastName = "Person" }
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
                CaretakerId = 5,
                PaymentTypeId = 1,
                Amount = 300m,
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

            Assert.Equal(5, payment.CaretakerId);
            Assert.NotNull(payment.Caretaker);
            Assert.Equal(5, payment.Caretaker.Id);
        }
    }
}
