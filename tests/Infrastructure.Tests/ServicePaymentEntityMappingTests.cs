using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests.Repositories;

public class ServicePaymentEntityMappingTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    private static void SeedTherapistAndPaymentType(ApplicationDbContext context)
    {
        context.PaymentTypes.Add(new PaymentType { Id = 1, Abbreviation = "ACH", Name = "Bank Transfer" });
        context.Therapists.Add(new Therapist
        {
            Id = 1,
            User = new User { Id = 1, FirstName = "Jane", LastName = "Smith" }
        });
    }

    [Fact]
    public async Task ServicePayment_CanBePersisted_WithAllProperties()
    {
        var options = CreateInMemoryOptions(nameof(ServicePayment_CanBePersisted_WithAllProperties));
        var paymentDate = DateTime.UtcNow;

        using (var context = new ApplicationDbContext(options))
        {
            SeedTherapistAndPaymentType(context);
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 1,
                TherapistId = 1,
                PaymentTypeId = 1,
                Amount = 500.00m,
                PaymentDate = paymentDate,
                ReferenceNumber = "TX-100",
                Notes = "April quincena",
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var sp = await context.ServicePayments.FindAsync(1);
            Assert.NotNull(sp);
            Assert.Equal(1, sp!.TherapistId);
            Assert.Equal(1, sp.PaymentTypeId);
            Assert.Equal(500.00m, sp.Amount);
            Assert.Equal(paymentDate, sp.PaymentDate);
            Assert.Equal("TX-100", sp.ReferenceNumber);
            Assert.Equal("April quincena", sp.Notes);
            Assert.Null(sp.ReversesServicePaymentId);
        }
    }

    [Fact]
    public async Task ServicePayment_TherapistNavigation_LoadsCorrectly()
    {
        var options = CreateInMemoryOptions(nameof(ServicePayment_TherapistNavigation_LoadsCorrectly));

        using (var context = new ApplicationDbContext(options))
        {
            SeedTherapistAndPaymentType(context);
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 1,
                TherapistId = 1,
                PaymentTypeId = 1,
                Amount = 200.00m,
                PaymentDate = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var sp = await context.ServicePayments
                .Include(p => p.Therapist).ThenInclude(t => t.User)
                .FirstAsync(p => p.Id == 1);

            Assert.NotNull(sp.Therapist);
            Assert.NotNull(sp.Therapist.User);
            Assert.Equal("Smith", sp.Therapist.User!.LastName);
        }
    }

    [Fact]
    public async Task ServicePayment_SessionServicePaymentsCollection_LoadsCorrectly()
    {
        var options = CreateInMemoryOptions(nameof(ServicePayment_SessionServicePaymentsCollection_LoadsCorrectly));

        using (var context = new ApplicationDbContext(options))
        {
            SeedTherapistAndPaymentType(context);
            context.Patients.Add(new Patient { Id = 1, User = new User { Id = 2, FirstName = "Dan", LastName = "Green" } });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1, PatientId = 1, TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow), SessionTime = new TimeOnly(9, 0),
                Amount = 100m, ProviderAmount = 60m, Notes = "S1"
            });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 2, PatientId = 1, TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow), SessionTime = new TimeOnly(10, 0),
                Amount = 100m, ProviderAmount = 60m, Notes = "S2"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 1, TherapistId = 1, PaymentTypeId = 1, Amount = 120.00m, PaymentDate = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.SessionServicePayments.Add(new SessionServicePayment { Id = 1, ServicePaymentId = 1, TherapySessionId = 1, AmountApplied = 60.00m });
            context.SessionServicePayments.Add(new SessionServicePayment { Id = 2, ServicePaymentId = 1, TherapySessionId = 2, AmountApplied = 60.00m });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var sp = await context.ServicePayments
                .Include(p => p.SessionServicePayments)
                .FirstAsync(p => p.Id == 1);

            Assert.NotNull(sp.SessionServicePayments);
            Assert.Equal(2, sp.SessionServicePayments.Count);
            Assert.All(sp.SessionServicePayments, ssp => Assert.Equal(60.00m, ssp.AmountApplied));
        }
    }

    [Fact]
    public async Task ServicePayment_AuditColumns_AreStamped_OnSave()
    {
        var options = CreateInMemoryOptions(nameof(ServicePayment_AuditColumns_AreStamped_OnSave));

        using (var context = new ApplicationDbContext(options))
        {
            SeedTherapistAndPaymentType(context);
            await context.SaveChangesAsync();
        }

        DateTime before;
        using (var context = new ApplicationDbContext(options))
        {
            before = DateTime.UtcNow;
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 1, TherapistId = 1, PaymentTypeId = 1, Amount = 250.00m, PaymentDate = before,
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var sp = await context.ServicePayments.FindAsync(1);
            Assert.NotNull(sp);
            Assert.NotEqual(default, sp!.CreatedTimestamp);
            Assert.NotEqual(default, sp.LastUpdatedTimestamp);
        }
    }

    [Fact]
    public async Task ServicePayment_NegativeAmounts_RoundTrip_ForAppendOnlyReversals()
    {
        // Append-only readiness (WP-14.5): Amount / AmountApplied may be negative.
        var options = CreateInMemoryOptions(nameof(ServicePayment_NegativeAmounts_RoundTrip_ForAppendOnlyReversals));

        using (var context = new ApplicationDbContext(options))
        {
            SeedTherapistAndPaymentType(context);
            context.Patients.Add(new Patient { Id = 1, User = new User { Id = 2, FirstName = "Dan", LastName = "Green" } });
            context.TherapySessions.Add(new TherapySession
            {
                Id = 1, PatientId = 1, TherapistId = 1,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow), SessionTime = new TimeOnly(9, 0),
                Amount = 100m, ProviderAmount = 60m, Notes = "S1"
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 2, TherapistId = 1, PaymentTypeId = 1, Amount = -60.00m, PaymentDate = DateTime.UtcNow,
                ReversesServicePaymentId = 1, Notes = "reversal",
            });
            context.SessionServicePayments.Add(new SessionServicePayment { Id = 1, ServicePaymentId = 2, TherapySessionId = 1, AmountApplied = -60.00m });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var sp = await context.ServicePayments.FindAsync(2);
            Assert.NotNull(sp);
            Assert.Equal(-60.00m, sp!.Amount);
            Assert.Equal(1, sp.ReversesServicePaymentId);

            var ssp = await context.SessionServicePayments.FindAsync(1);
            Assert.NotNull(ssp);
            Assert.Equal(-60.00m, ssp!.AmountApplied);
        }
    }
}
