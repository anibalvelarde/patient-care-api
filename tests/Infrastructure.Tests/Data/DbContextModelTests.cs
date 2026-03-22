using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests.Data;

public class DbContextModelTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Theory]
    [InlineData(typeof(Patient), "Patient")]
    [InlineData(typeof(User), "SystemUser")]
    [InlineData(typeof(Caretaker), "Caretaker")]
    [InlineData(typeof(Therapist), "Therapist")]
    [InlineData(typeof(TherapySession), "TherapySession")]
    [InlineData(typeof(UserRole), "UserRole")]
    [InlineData(typeof(Payment), "Payment")]
    [InlineData(typeof(SessionPayment), "SessionPayment")]
    [InlineData(typeof(PaymentType), "PaymentType")]
    [InlineData(typeof(PatientCaretaker), "PatientCaretaker")]
    public void Entity_Maps_To_Expected_Table_Name(Type entityType, string expectedTableName)
    {
        // Arrange
        var options = CreateInMemoryOptions($"TableName_{entityType.Name}");
        using var context = new ApplicationDbContext(options);

        // Act
        var entityTypeMetadata = context.Model.FindEntityType(entityType);

        // Assert
        Assert.NotNull(entityTypeMetadata);
        Assert.Equal(expectedTableName, entityTypeMetadata.GetTableName());
    }

    [Theory]
    [InlineData(typeof(Patient), "PatientID")]
    [InlineData(typeof(User), "UserID")]
    [InlineData(typeof(Caretaker), "CaretakerID")]
    [InlineData(typeof(Therapist), "TherapistID")]
    [InlineData(typeof(TherapySession), "SessionID")]
    [InlineData(typeof(UserRole), "UserRoleID")]
    [InlineData(typeof(Payment), "PaymentID")]
    [InlineData(typeof(SessionPayment), "SessionPaymentID")]
    [InlineData(typeof(PaymentType), "PaymentTypeID")]
    [InlineData(typeof(PatientCaretaker), "PatientCaretakerID")]
    public void Entity_Has_Expected_Primary_Key_Column_Name(Type entityType, string expectedPkColumn)
    {
        // Arrange
        var options = CreateInMemoryOptions($"PK_{entityType.Name}");
        using var context = new ApplicationDbContext(options);

        // Act
        var entityTypeMetadata = context.Model.FindEntityType(entityType);
        Assert.NotNull(entityTypeMetadata);

        var primaryKey = entityTypeMetadata.FindPrimaryKey();
        Assert.NotNull(primaryKey);

        var pkColumnName = primaryKey.Properties[0].GetColumnName();

        // Assert
        Assert.Equal(expectedPkColumn, pkColumnName);
    }

    [Fact]
    public void Payment_CaretakerId_Maps_To_PaidBy_Column()
    {
        // Arrange
        var options = CreateInMemoryOptions("Payment_PaidBy_Column");
        using var context = new ApplicationDbContext(options);

        // Act
        var entityType = context.Model.FindEntityType(typeof(Payment));
        Assert.NotNull(entityType);

        var property = entityType.FindProperty(nameof(Payment.CaretakerId));
        Assert.NotNull(property);

        var columnName = property.GetColumnName();

        // Assert
        Assert.Equal("PaidBy", columnName);
    }

    [Fact]
    public void PatientCaretaker_Has_Unique_Index_On_PatientId_And_CaretakerId()
    {
        // Arrange
        var options = CreateInMemoryOptions("PatientCaretaker_UniqueIndex");
        using var context = new ApplicationDbContext(options);

        // Act
        var entityType = context.Model.FindEntityType(typeof(PatientCaretaker));
        Assert.NotNull(entityType);

        var indexes = entityType.GetIndexes().ToList();
        var uniqueIndex = indexes.FirstOrDefault(i =>
            i.IsUnique &&
            i.Properties.Count == 2 &&
            i.Properties.Any(p => p.Name == nameof(PatientCaretaker.PatientId)) &&
            i.Properties.Any(p => p.Name == nameof(PatientCaretaker.CaretakerId)));

        // Assert
        Assert.NotNull(uniqueIndex);
    }

    [Fact]
    public void No_Filter_Expressions_Contain_SqlServer_Bracket_Syntax()
    {
        // Arrange
        var options = CreateInMemoryOptions("No_Bracket_Syntax");
        using var context = new ApplicationDbContext(options);

        // Act
        var entityTypes = context.Model.GetEntityTypes().ToList();

        // Assert
        foreach (var entityType in entityTypes)
        {
            var annotations = entityType.GetAnnotations().ToList();
            foreach (var annotation in annotations)
            {
                if (annotation.Value is string stringValue)
                {
                    Assert.DoesNotContain("[", stringValue,
                        StringComparison.Ordinal);
                }
            }

            // Also check query filter expressions for bracket syntax
            var queryFilter = entityType.GetQueryFilter();
            if (queryFilter != null)
            {
                var filterString = queryFilter.ToString();
                Assert.DoesNotContain("[", filterString,
                    StringComparison.Ordinal);
            }
        }
    }
}
