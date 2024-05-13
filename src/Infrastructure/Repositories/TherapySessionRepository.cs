using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapySessionRepository(ApplicationDbContext dbContext) : 
    EfRepository<TherapySession>(dbContext), ITherapySessionRepository
{

    // Additional methods specific to Patient can be implemented here
}
