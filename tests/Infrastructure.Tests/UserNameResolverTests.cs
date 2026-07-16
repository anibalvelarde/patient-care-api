using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Services;

namespace Infrastructure.Tests.Repositories;

// WP-31 (U1): batched updater-name resolution over SystemUsers.
public class UserNameResolverTests
{
    private static DbContextOptions<ApplicationDbContext> NewDb() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "UserNameResolver_" + Guid.NewGuid())
            .Options;

    [Fact]
    public async Task ResolveAsync_FormatsNames_ExcludesZeroAndUnknown_Dedupes()
    {
        var options = NewDb();
        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.Users.Add(new User { Id = 1, FirstName = "John", LastName = "Doe" });
            ctx.Users.Add(new User { Id = 2, FirstName = "Ana", MiddleName = "B", LastName = "Lopez" });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var resolver = new UserNameResolver(ctx);
            var dict = await resolver.ResolveAsync(new[] { 1, 2, 0, 99, 1 }); // 0 = system, 99 = unknown, 1 dup

            Assert.Equal(2, dict.Count);
            Assert.Equal("Doe, John", dict[1]);
            Assert.Equal("Lopez, Ana B", dict[2]);
            Assert.False(dict.ContainsKey(0));
            Assert.False(dict.ContainsKey(99));
        }
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmpty_ForNoUsableIds()
    {
        using var ctx = new ApplicationDbContext(NewDb());
        var resolver = new UserNameResolver(ctx);

        var dict = await resolver.ResolveAsync(new[] { 0, 0 });

        Assert.Empty(dict);
    }
}
