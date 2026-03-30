using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class AppointmentStatusServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllStatuses()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var statuses = new List<AppointmentStatus>
        {
            new() { Id = 1, StatusName = "Proposed", StatusDescription = "Requested" },
            new() { Id = 2, StatusName = "Confirmed", StatusDescription = "Confirmed by patient" }
        };
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetAllAsync()).ReturnsAsync(statuses);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<AppointmentStatusInfo>>(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsStatus_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var expected = new AppointmentStatus { Id = 1, StatusName = "Proposed", StatusDescription = "Requested" };
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(expected);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected.Id, result.AppointmentStatusId);
        Assert.Equal(expected.StatusName, result.StatusName);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((AppointmentStatus?)null);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedStatus()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var request = new AppointmentStatusCreateRequest { StatusName = "NewStatus", StatusDescription = "Desc" };
        var created = new AppointmentStatus { Id = 10, StatusName = "NewStatus", StatusDescription = "Desc" };
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.AddAsync(It.IsAny<AppointmentStatus>())).ReturnsAsync(created);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.AppointmentStatusId);
        Assert.Equal("NewStatus", result.StatusName);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var existing = new AppointmentStatus { Id = 1, StatusName = "Old" };
        var request = new AppointmentStatusUpdateRequest { StatusName = "New" };
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(existing);
        mockRepo.Setup(repo => repo.UpdateAsync(It.IsAny<AppointmentStatus>())).Returns(Task.CompletedTask);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<AppointmentStatusService>>();
        var request = new AppointmentStatusUpdateRequest { StatusName = "New" };
        var mockRepo = new Mock<IRepository<AppointmentStatus>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((AppointmentStatus?)null);
        var svc = new AppointmentStatusService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(99, request);

        // Assert
        Assert.False(result);
    }
}
