using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

public class PaymentRepository(ApplicationDbContext dbContext) :
    EfRepository<Payment>(dbContext), IPaymentRepository
{

    // Additional methods specific to Patient can be implemented here
}
