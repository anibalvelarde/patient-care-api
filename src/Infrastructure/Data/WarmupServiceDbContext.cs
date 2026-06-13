using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Neurocorp.Api.Infrastructure.Data;

public class DbContextWarmupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public DbContextWarmupService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Execute a trivial query to force EF model compilation + open the DB connection.
            // AnyAsync (SELECT EXISTS ...) does this without fetching an arbitrary row, so it
            // avoids the FirstWithoutOrderByAndFilter warning (EF 10103) a bare FirstOrDefault
            // would raise — the result is discarded either way.
            await dbContext.TherapySessions.AnyAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
