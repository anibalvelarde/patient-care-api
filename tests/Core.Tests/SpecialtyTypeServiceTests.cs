using Neurocorp.Api.Core.Interfaces.Repositories;
using Moq;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Core.Tests;

public class SpecialtyTypeServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAllSpecialties()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var specialties = new List<SpecialtyType>
        {
            new() { Id = 1, SpecialtyAbbreviation = "ET", SpecialtyName = "Early Stimulation" },
            new() { Id = 2, SpecialtyAbbreviation = "FS", SpecialtyName = "Physiotherapy" }
        };
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetAllAsync()).ReturnsAsync(specialties);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IEnumerable<SpecialtyTypeInfo>>(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSpecialty_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var expected = new SpecialtyType { Id = 1, SpecialtyAbbreviation = "ET", SpecialtyName = "Early Stimulation" };
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(expected);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.SpecialtyId);
        Assert.Equal("ET", result.Abbreviation);
        Assert.Equal("Early Stimulation", result.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((SpecialtyType?)null);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.GetByIdAsync(99);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedSpecialty()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var request = new SpecialtyTypeCreateRequest { Abbreviation = "OT", Name = "Occupational Therapy" };
        var created = new SpecialtyType { Id = 10, SpecialtyAbbreviation = "OT", SpecialtyName = "Occupational Therapy" };
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.AddAsync(It.IsAny<SpecialtyType>())).ReturnsAsync(created);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.CreateAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.SpecialtyId);
        Assert.Equal("OT", result.Abbreviation);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsTrue_WhenExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var existing = new SpecialtyType { Id = 1, SpecialtyAbbreviation = "ET", SpecialtyName = "Old Name" };
        var request = new SpecialtyTypeUpdateRequest { Name = "Early Stimulation Therapy" };
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(1)).ReturnsAsync(existing);
        mockRepo.Setup(repo => repo.UpdateAsync(It.IsAny<SpecialtyType>())).Returns(Task.CompletedTask);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(1, request);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotExists()
    {
        // Arrange
        var fakeLogger = Mock.Of<ILogger<SpecialtyTypeService>>();
        var request = new SpecialtyTypeUpdateRequest { Name = "Updated" };
        var mockRepo = new Mock<IRepository<SpecialtyType>>(MockBehavior.Strict);
        mockRepo.Setup(repo => repo.GetByIdAsync(99)).ReturnsAsync((SpecialtyType?)null);
        var svc = new SpecialtyTypeService(fakeLogger, mockRepo.Object);

        // Act
        var result = await svc.UpdateAsync(99, request);

        // Assert
        Assert.False(result);
    }
}
