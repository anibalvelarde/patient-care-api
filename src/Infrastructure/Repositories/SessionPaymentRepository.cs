using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class SessionPaymentRepository(ApplicationDbContext dbContext) : 
    EfRepository<SessionPayment>(dbContext), ISessionPaymentRepository
{

    // Additional methods specific to Patient can be implemented here
}
