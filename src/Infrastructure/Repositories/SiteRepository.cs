using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SiteRepository(ApplicationDbContext dbContext) :
    EfRepository<Site>(dbContext), ISiteRepository
{

    // Additional methods specific to Site can be implemented here
}
