using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class RoleTypeServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllRoles()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var roles = new List<RoleType>
        {
            new() { Id = 1, RoleName = "Patient" },
            new() { Id = 2, RoleName = "Therapist" }
        };
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetAllAsync()).ReturnsAsync(roles);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<RoleTypeInfo>>(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsRole_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var expected = new RoleType { Id = 1, RoleName = "Patient" };
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(expected);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.RoleId);
        Assert.Equal("Patient", result.RoleName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((RoleType?)null);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedRole()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var request = new RoleTypeCreateRequest { RoleName = "Admin" };
        var created = new RoleType { Id = 10, RoleName = "Admin" };
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.AddAsync(It.IsAny<RoleType>())).ReturnsAsync(created);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.RoleId);
        Assert.Equal("Admin", result.RoleName);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var existing = new RoleType { Id = 1, RoleName = "Patient" };
        var request = new RoleTypeUpdateRequest { RoleName = "Primary Patient" };
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(existing);
        mockRepo.Setup(repo => repo.UpdateAsync(It.IsAny<RoleType>())).Returns(Task.CompletedTask);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<RoleTypeService>>();
        var request = new RoleTypeUpdateRequest { RoleName = "Updated" };
        var mockRepo = new Mock<IRepository<RoleType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((RoleType?)null);
        var svc = new RoleTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(99, request);

        // Assert
        Assert.False(result);
    }
}
