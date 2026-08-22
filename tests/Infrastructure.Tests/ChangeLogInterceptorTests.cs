using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Neurocorp.Api.Core.Configurations;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Data.ChangeLog;
using Xunit;

namespace Neurocorp.Api.Infrastructure.Tests;

/// <summary>
/// WP-54B capture. Exercises the interceptor on the InMemory provider (fires SaveChanges
/// interceptors and generates keys). The one relational-only path — the purge ExecuteDelete — is
/// validated at the local MySQL rehearsal, not here.
/// </summary>
public class ChangeLogInterceptorTests
{
    private sealed class StubUser(int? id) : ICurrentUserService
    {
        public int? UserId => id;
        public bool IsAuthenticated => id is not null;
    }

    private sealed class StubCorrelation(string? cid) : IRequestCorrelation
    {
        public string? CurrentId => cid;
    }

    private static ApplicationDbContext NewContext(
        string db, bool enabled = true, int? userId = 7, string? correlationId = "req-abc")
    {
        var interceptor = new ChangeLogInterceptor(
            Options.Create(new ChangeLogOptions { Enabled = enabled }),
            new StubUser(userId),
            new StubCorrelation(correlationId));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(db)
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static List<EntityChangeLog> Logs(string db)
    {
        using var ctx = NewContext(db);
        return ctx.EntityChangeLogs.AsNoTracking().OrderBy(e => e.Id).ToList();
    }

    private static List<string> Fields(EntityChangeLog row)
        => JsonSerializer.Deserialize<List<string>>(row.Changes)!;

    [Fact]
    public void Insert_writes_one_row_with_generated_id_and_no_values()
    {
        var db = nameof(Insert_writes_one_row_with_generated_id_and_no_values);
        using (var ctx = NewContext(db))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "ZZ", Name = "SecretRoleName", SortOrder = 5 });
            ctx.SaveChanges();
        }

        var row = Assert.Single(Logs(db));
        Assert.Equal(ChangeAction.Insert, row.Action);
        Assert.Equal("RoleType", row.EntityType);
        Assert.True(int.Parse(row.EntityId) > 0);          // generated key was read back
        Assert.Equal(7, row.UserId);
        Assert.Equal("req-abc", row.CorrelationId);

        var fields = Fields(row);
        Assert.Contains("Name", fields);
        Assert.DoesNotContain("CreatedTimestamp", fields);  // audit/lookup timestamps excluded
        Assert.DoesNotContain("LastUpdatedTimestamp", fields);
        // Field NAMES only — the value never appears anywhere in the row.
        Assert.DoesNotContain("SecretRoleName", row.Changes);
        Assert.DoesNotContain("SecretRoleName", row.EntityLabel ?? string.Empty);
    }

    [Fact]
    public void Update_logs_only_the_changed_field()
    {
        var db = nameof(Update_logs_only_the_changed_field);
        using (var ctx = NewContext(db))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "AA", Name = "Before", SortOrder = 1 });
            ctx.SaveChanges();
        }
        using (var ctx = NewContext(db))
        {
            var role = ctx.RoleTypes.First(r => r.Abbreviation == "AA");
            role.Name = "After";
            ctx.SaveChanges();
        }

        var update = Logs(db).Single(l => l.Action == ChangeAction.Update);
        Assert.Equal(["Name"], Fields(update));             // ONLY the changed field
    }

    [Fact]
    public void Update_touching_only_excluded_fields_logs_nothing()
    {
        var db = nameof(Update_touching_only_excluded_fields_logs_nothing);
        using (var ctx = NewContext(db))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "BB", Name = "N", SortOrder = 1 });
            ctx.SaveChanges();
        }
        using (var ctx = NewContext(db))
        {
            var role = ctx.RoleTypes.First(r => r.Abbreviation == "BB");
            role.LastUpdatedTimestamp = role.LastUpdatedTimestamp.AddDays(1); // excluded-only change
            ctx.SaveChanges();
        }

        Assert.Equal(0, Logs(db).Count(l => l.Action == ChangeAction.Update));
    }

    [Fact]
    public void Delete_writes_a_delete_row()
    {
        var db = nameof(Delete_writes_a_delete_row);
        using (var ctx = NewContext(db))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "CC", Name = "N", SortOrder = 1 });
            ctx.SaveChanges();
        }
        using (var ctx = NewContext(db))
        {
            var role = ctx.RoleTypes.First(r => r.Abbreviation == "CC");
            ctx.RoleTypes.Remove(role);
            ctx.SaveChanges();
        }

        var del = Logs(db).Single(l => l.Action == ChangeAction.Delete);
        Assert.Equal("RoleType", del.EntityType);
        Assert.NotEmpty(Fields(del));
    }

    [Fact]
    public void Disabled_kill_switch_logs_nothing()
    {
        var db = nameof(Disabled_kill_switch_logs_nothing);
        using (var ctx = NewContext(db, enabled: false))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "DD", Name = "N", SortOrder = 1 });
            ctx.SaveChanges();
        }
        Assert.Empty(Logs(db));
    }

    [Fact]
    public void No_current_user_records_system_id_zero()
    {
        var db = nameof(No_current_user_records_system_id_zero);
        using (var ctx = NewContext(db, userId: null, correlationId: null))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "EE", Name = "N", SortOrder = 1 });
            ctx.SaveChanges();
        }
        var row = Assert.Single(Logs(db));
        Assert.Equal(0, row.UserId);
        Assert.Null(row.CorrelationId);
    }

    [Fact]
    public void The_log_table_itself_is_never_audited()
    {
        var db = nameof(The_log_table_itself_is_never_audited);
        using (var ctx = NewContext(db))
        {
            ctx.RoleTypes.Add(new RoleType { Abbreviation = "FF", Name = "N", SortOrder = 1 });
            ctx.SaveChanges();
        }
        // Exactly one row (the RoleType insert) — the interceptor's own EntityChangeLog insert did
        // not recursively log itself.
        Assert.Single(Logs(db));
    }
}
