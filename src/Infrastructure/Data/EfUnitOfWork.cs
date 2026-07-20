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
            try
            {
                var result = await operation();
                await transaction.CommitAsync();
                return result;
            }
            catch
            {
                // WP-36 (G1a): the transaction rolls back, but the context would still TRACK the
                // phantom writes (e.g. an Added Patient carrying the collided minted MRN). Clear
                // them so a caller-level retry — the MRN mint-collision retry re-runs the whole
                // unit of work on this same scoped context — starts clean instead of replaying
                // the rolled-back entities on its first SaveChanges.
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
