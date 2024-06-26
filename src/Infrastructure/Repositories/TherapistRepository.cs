using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class TherapistRepository(ApplicationDbContext dbContext) :
    EfRepository<Therapist>(dbContext), ITherapistRepository
{

    // Additional methods specific to Patient can be implemented here
}
