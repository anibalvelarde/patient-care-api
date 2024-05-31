using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class UserRoleRepository(ApplicationDbContext dbContext) :
    EfRepository<UserRole>(dbContext), IUserRoleRepository
{

    // Additional methods specific to Patient can be implemented here
}
