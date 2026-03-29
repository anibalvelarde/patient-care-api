using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

public class SiteRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public async Task AddAndGetByIdAsync_PersistsAndRetrievesSite()
    {
        // Arrange
        var options = CreateInMemoryOptions("SiteRepository_AddAndGet");

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SiteRepository(context);
            var site = new Site
            {
                SiteName = "Main Clinic",
                RUC = "12345678901",
                InceptionDate = new DateTime(2020, 1, 15),
                Address = "123 Main St, Suite 100",
                Latitude = 10.12345m,
                Longitude = -84.56789m
            };

            // Act
            var created = await repository.AddAsync(site);

            // Assert
            Assert.True(created.Id > 0);
            Assert.Equal("Main Clinic", created.SiteName);
        }

        // Verify retrieval in a new context
        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SiteRepository(context);

            // Act
            var retrieved = await repository.GetByIdAsync(1);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal("Main Clinic", retrieved.SiteName);
            Assert.Equal("12345678901", retrieved.RUC);
            Assert.Equal(new DateTime(2020, 1, 15), retrieved.InceptionDate);
            Assert.Equal("123 Main St, Suite 100", retrieved.Address);
            Assert.Equal(10.12345m, retrieved.Latitude);
            Assert.Equal(-84.56789m, retrieved.Longitude);
        }
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSites()
    {
        // Arrange
        var options = CreateInMemoryOptions("SiteRepository_GetAll");

        using (var context = new ApplicationDbContext(options))
        {
            context.Sites.AddRange(
                new Site { SiteName = "Clinic A", InceptionDate = DateTime.Now },
                new Site { SiteName = "Clinic B", InceptionDate = DateTime.Now }
            );
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var repository = new SiteRepository(context);
            var results = await repository.GetAllAsync();

            Assert.Equal(2, results.Count);
        }
    }

    [Fact]
    public async Task Site_WithNullOptionalProperties_RoundTripsCorrectly()
    {
        // Arrange
        var options = CreateInMemoryOptions("SiteRepository_NullProps");

        using (var context = new ApplicationDbContext(options))
        {
            context.Sites.Add(new Site
            {
                SiteName = "Minimal Site",
                InceptionDate = new DateTime(2024, 6, 1),
                Address = null,
                Latitude = null,
                Longitude = null,
                RUC = null
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var site = await context.Sites.FirstAsync();
            Assert.Equal("Minimal Site", site.SiteName);
            Assert.Null(site.Address);
            Assert.Null(site.Latitude);
            Assert.Null(site.Longitude);
            Assert.Null(site.RUC);
        }
    }

    [Fact]
    public async Task TherapySession_SiteId_ForeignKey_NavigationWorks()
    {
        // Arrange
        var options = CreateInMemoryOptions("SiteRepository_SessionFK");

        using (var context = new ApplicationDbContext(options))
        {
            var site = new Site
            {
                Id = 1,
                SiteName = "Therapy Center",
                InceptionDate = new DateTime(2023, 1, 1)
            };
            context.Sites.Add(site);

            context.Therapists.Add(new Therapist
            {
                Id = 1,
                User = new User { Id = 1, FirstName = "Jane", LastName = "Doe", ActiveStatus = true }
            });
            context.Patients.Add(new Patient
            {
                Id = 1,
                User = new User { Id = 2, FirstName = "Bob", LastName = "Smith", ActiveStatus = true }
            });
            await context.SaveChangesAsync();

            context.TherapySessions.Add(new TherapySession
            {
                Id = 100,
                PatientId = 1,
                TherapistId = 1,
                SessionDate = new DateOnly(2025, 3, 1),
                SessionTime = new TimeOnly(10, 0),
                Duration = 60,
                Amount = 100m,
                SiteId = 1
            });
            await context.SaveChangesAsync();
        }

        // Act & Assert
        using (var context = new ApplicationDbContext(options))
        {
            var session = await context.TherapySessions
                .Include(s => s.Site)
                .FirstAsync(s => s.Id == 100);

            Assert.NotNull(session.Site);
            Assert.Equal("Therapy Center", session.Site.SiteName);
            Assert.Equal(1, session.SiteId);
        }
    }
}
