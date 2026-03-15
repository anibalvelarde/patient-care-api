using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

public class PatientProfileRepositoryTests
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
        var options = CreateInMemoryOptions($"PatientActiveStatus_{initialStatus}_{requestedStatus}");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.Add(new Patient
            {
                Id = 1,
                Gender = "M",
                MedicalRecordNumber = "MRN-001",
                User = new User
                {
                    Id = 1,
                    FirstName = "John",
                    LastName = "Doe",
                    ActiveStatus = initialStatus
                }
            });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new PatientProfileRepository(context);
            var updateRequest = new PatientProfileUpdateRequest
            {
                FirstName = "John",
                LastName = "Doe",
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
