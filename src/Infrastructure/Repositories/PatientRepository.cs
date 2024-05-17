using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PatientRepository(ApplicationDbContext dbContext) :
    EfRepository<Patient>(dbContext), IPatientRepository
{

    // Additional methods specific to Patient can be implemented here
}
