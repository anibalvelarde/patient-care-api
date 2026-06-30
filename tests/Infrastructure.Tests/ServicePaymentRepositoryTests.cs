using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Repositories;

public class ServicePaymentRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> InMemory(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task GetByTherapistIdAndDateRange_OrdersByDateThenIssuanceOrder()
    {
        // Regression (WP-14.5): same-day rows (a reversal + its re-issuance) must be ordered by
        // issuance (auto-increment Id) within a date, so the History table reads newest-action-first
        // instead of an undefined tie order. Insertion order is 10,11,12; 11 and 12 share Jun 29 with
        // 12 issued last (highest Id) -> expected order 12, 11, 10.
        var options = InMemory(nameof(GetByTherapistIdAndDateRange_OrdersByDateThenIssuanceOrder));

        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.PaymentTypes.Add(new PaymentType { Id = 1, Abbreviation = "CHK", Name = "Check" });
            ctx.Therapists.Add(new Therapist { Id = 1, User = new User { Id = 1, FirstName = "Jane", LastName = "Smith" } });
            ctx.ServicePayments.Add(new ServicePayment { Id = 10, TherapistId = 1, PaymentTypeId = 1, Amount = 100m, PaymentDate = new DateTime(2026, 6, 20) });
            ctx.ServicePayments.Add(new ServicePayment { Id = 11, TherapistId = 1, PaymentTypeId = 1, Amount = -100m, PaymentDate = new DateTime(2026, 6, 29), ReversesServicePaymentId = 10 });
            ctx.ServicePayments.Add(new ServicePayment { Id = 12, TherapistId = 1, PaymentTypeId = 1, Amount = 100m, PaymentDate = new DateTime(2026, 6, 29) });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var repo = new ServicePaymentRepository(ctx);
            var result = await repo.GetByTherapistIdAndDateRangeAsync(1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));
            Assert.Equal(new[] { 12, 11, 10 }, result.Select(p => p.Id).ToArray());
        }
    }
}
