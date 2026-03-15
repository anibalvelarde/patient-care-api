using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

public class TherapistProfileRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task UpdateAsync_ActiveStatus_IsAlwaysMappedCorrectly(bool initialStatus, bool requestedStatus)
    {
        // Arrange
        var options = CreateInMemoryOptions($"TherapistActiveStatus_{initialStatus}_{requestedStatus}");

        using (var context = new ApplicationDbContext(options))
        {
            context.Therapists.Add(new Therapist
            {
                Id = 1,
                TherapistId = 1,
                UserId = 1,
                FeePerSession = 100m,
                FeePctPerSession = 0.5m,
                User = new User
                {
                    Id = 1,
                    FirstName = "Jane",
                    LastName = "Smith",
                    ActiveStatus = initialStatus
                }
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new TherapistProfileRepository(context);
            var updateRequest = new TherapistProfileUpdateRequest
            {
                FirstName = "Jane",
                LastName = "Smith",
                ActiveStatus = requestedStatus
            };

            // Act
            var result = await repository.UpdateAsync(1, 1, updateRequest);

            // Assert
            Assert.Equal(requestedStatus, result.IsActive);
        }

        // Verify the change was persisted to the database
        using (var context = new ApplicationDbContext(options))
        {
            var user = await context.Users.FindAsync(1);
            Assert.NotNull(user);
            Assert.Equal(requestedStatus, user.ActiveStatus);
        }
    }
}
