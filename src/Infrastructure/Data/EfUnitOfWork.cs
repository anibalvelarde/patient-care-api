using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Interfaces;

namespace Neurocorp.Api.Infrastructure.Data;

/// <summary>
/// EF-backed unit of work: runs the operation inside an explicit transaction so multi-repository
/// writes commit or roll back together. The transaction is opened through the configured execution
/// strategy — required because the MySQL provider is registered with EnableRetryOnFailure, whose
/// retrying strategy forbids bare BeginTransaction calls.
/// </summary>
public class EfUnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            var result = await operation();
            await transaction.CommitAsync();
            return result;
        });
    }
}
