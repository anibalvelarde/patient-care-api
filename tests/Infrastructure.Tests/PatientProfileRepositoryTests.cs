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

    // WP-29 (U3): batched GetByIdsAsync — same projection as GetByIdAsync, one round trip.
    [Fact]
    public async Task GetByIdsAsync_ReturnsMatchingProfiles_AndEmptyForEmptyInput()
    {
        var options = CreateInMemoryOptions("Wp29GetByIds");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.AddRange(
                new Patient { Id = 1, MedicalRecordNumber = "MRN-001", User = new User { Id = 1, FirstName = "John", LastName = "Doe" } },
                new Patient { Id = 2, MedicalRecordNumber = "MRN-002", User = new User { Id = 2, FirstName = "Alice", LastName = "Wonder" } },
                new Patient { Id = 3, MedicalRecordNumber = "MRN-003", User = new User { Id = 3, FirstName = "Bob", LastName = "Builder" } });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new PatientProfileRepository(context);

            var result = await repository.GetByIdsAsync(new[] { 1, 3, 99 });

            Assert.Equal(2, result.Count);
            Assert.Contains(result, p => p.PatientId == 1 && p.PatientName == "Doe, John");
            Assert.Contains(result, p => p.PatientId == 3 && p.PatientName == "Builder, Bob");

            Assert.Empty(await repository.GetByIdsAsync(System.Array.Empty<int>()));
        }
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

    [Fact]
    public async Task UpdateAsync_Cedula_IsMappedPersistedAndProjected()
    {
        // Arrange
        var options = CreateInMemoryOptions("PatientCedula_RoundTrip");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.Add(new Patient
            {
                Id = 1,
                Gender = "M",
                MedicalRecordNumber = "MRN-001",
                User = new User { Id = 1, FirstName = "John", LastName = "Doe", ActiveStatus = true }
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
                ActiveStatus = true,
                Cedula = "001-1234567-8"
            };

            // Act — the returned profile is built by ExtractPatientProfile
            var result = await repository.UpdateAsync(1, 1, updateRequest);

            // Assert — read projection surfaces the new value
            Assert.Equal("001-1234567-8", result.Cedula);
        }

        // Assert — value was persisted to the Patient entity (MapToUpdatedPatient)
        using (var context = new ApplicationDbContext(options))
        {
            var patient = await context.Patients.FindAsync(1);
            Assert.NotNull(patient);
            Assert.Equal("001-1234567-8", patient.Cedula);
        }
    }

    // WP-25 (F5) — supersedes intake 2026-06-29-001 item 3 (blank-erase removed): Cedula |
    // Passport is required once on file, so a present-but-blank value is now a rejected clear
    // attempt: ArgumentException (→ 400 via GlobalExceptionHandler) and the stored value must
    // remain untouched. Omitted/null still means "leave as-is" (legacy tolerance, next test).
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_BlankCedula_Throws(string blank)
    {
        // Arrange — patient already has a Cedula on file
        var options = CreateInMemoryOptions($"PatientCedula_Clear_{blank.Length}");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.Add(new Patient
            {
                Id = 1,
                Gender = "M",
                MedicalRecordNumber = "MRN-001",
                Cedula = "001-1234567-8",
                User = new User { Id = 1, FirstName = "John", LastName = "Doe", ActiveStatus = true }
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
                ActiveStatus = true,
                Cedula = blank
            };

            // Act + Assert — the clear attempt is rejected before anything is saved
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => repository.UpdateAsync(1, 1, updateRequest));
            Assert.Contains("cannot be cleared", ex.Message);
        }

        // Assert — the stored value is unchanged (not NULL, not the blank string)
        using (var context = new ApplicationDbContext(options))
        {
            var patient = await context.Patients.FindAsync(1);
            Assert.NotNull(patient);
            Assert.Equal("001-1234567-8", patient.Cedula);
        }
    }

    // Companion: an OMITTED Cedula (null in the DTO) still means "leave as-is" — partial updates
    // that don't mention the field must not wipe it.
    [Fact]
    public async Task UpdateAsync_OmittedCedula_LeavesExistingValueUnchanged()
    {
        // Arrange
        var options = CreateInMemoryOptions("PatientCedula_Omitted");

        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.Add(new Patient
            {
                Id = 1,
                Gender = "M",
                MedicalRecordNumber = "MRN-001",
                Cedula = "001-1234567-8",
                User = new User { Id = 1, FirstName = "John", LastName = "Doe", ActiveStatus = true }
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
                ActiveStatus = true,
                Cedula = null
            };

            // Act
            var result = await repository.UpdateAsync(1, 1, updateRequest);

            // Assert
            Assert.Equal("001-1234567-8", result.Cedula);
        }

        using (var context = new ApplicationDbContext(options))
        {
            var patient = await context.Patients.FindAsync(1);
            Assert.NotNull(patient);
            Assert.Equal("001-1234567-8", patient.Cedula);
        }
    }
}
