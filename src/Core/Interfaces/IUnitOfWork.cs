using System;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Interfaces;

/// <summary>
/// Runs a multi-write operation atomically: every repository write inside
/// <paramref name="operation"/> commits together or rolls back together.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
}
