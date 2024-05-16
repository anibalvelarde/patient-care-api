using Neurocorp.Api.Core.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Neurocorp.Api.Core.Interfaces;

public interface IService<T> where T : class
{
    Task<IEnumerable<T>> GetAllAsync();
    Task<T?> GetByIdAsync(int id);
    Task<T> CreateAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
}
