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

    // Regression (intake 2026-06-29-001 item 3): a present-but-blank Cedula in the update request
    // is an explicit "erase" and must clear the column to NULL. Previously the blank was silently
    // ignored (same non-empty⇒assign pattern as MRN), so a Cedula could never be removed.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_BlankCedula_ClearsToNull(string blank)
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

            // Act
            var result = await repository.UpdateAsync(1, 1, updateRequest);

            // Assert — projection reflects the erase
            Assert.Null(result.Cedula);
        }

        // Assert — NULL persisted (not the old value, not the blank string)
        using (var context = new ApplicationDbContext(options))
        {
            var patient = await context.Patients.FindAsync(1);
            Assert.NotNull(patient);
            Assert.Null(patient.Cedula);
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
