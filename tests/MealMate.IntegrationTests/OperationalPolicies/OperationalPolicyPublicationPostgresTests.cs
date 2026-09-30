using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Common;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.OperationalPolicies;

public sealed class OperationalPolicyPublicationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public async Task InitializeAsync()
    { await _container.StartAsync(); await using var db = Db(); await db.Database.EnsureCreatedAsync(); }
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
    private static OperationalPolicyService Service(AppDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()), Substitute.For<IAuditLogger>());

    [Fact]
    public async Task Publication_IsVisibleAcrossContextsAndRejectsStaleVersion()
    {
        await using var writer = Db(); await using var reader = Db();
        var before = await Service(reader).GetSettingsAsync();
        var published = await Service(writer).UpdateSettingsAsync(before with { Cutoff = new(CutoffLeadMinutes: 5) }, Guid.NewGuid());
        published.IsSuccess.Should().BeTrue();
        (await Service(reader).GetSettingsAsync()).Cutoff.EffectiveLeadMinutes.Should().Be(5);
        (await Service(reader).UpdateSettingsAsync(before, Guid.NewGuid())).IsFailure.Should().BeTrue();
        (await reader.SystemSettings.CountAsync(x => x.Key.StartsWith(OperationalPolicyService.HistoryPrefix))).Should().Be(1);
    }

    [Fact]
    public async Task JsonbCompareAndSwap_RejectsStaleWriteAndRollsBackItsHistory()
    {
        await using var setup = Db();
        var current = await Service(setup).GetSettingsAsync();
        await Service(setup).UpdateSettingsAsync(current, Guid.NewGuid());
        await using var first = Db(); await using var stale = Db();
        var firstRow = await first.SystemSettings.SingleAsync(x => x.Key == OperationalPolicyService.SettingKey);
        var staleRow = await stale.SystemSettings.SingleAsync(x => x.Key == OperationalPolicyService.SettingKey);
        firstRow.ValueJson = "{\"Version\":101}"; await first.SaveChangesAsync();
        staleRow.ValueJson = "{\"Version\":102}";
        var historyKey = OperationalPolicyService.HistoryPrefix + Guid.NewGuid();
        stale.SystemSettings.Add(new SystemSetting { Key = historyKey, ValueJson = "{}" });
        var write = () => stale.SaveChangesAsync();
        await write.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verify = Db();
        (await verify.SystemSettings.AnyAsync(x => x.Key == historyKey)).Should().BeFalse();
        (await verify.SystemSettings.SingleAsync(x => x.Key == OperationalPolicyService.SettingKey)).ValueJson.Should().Contain("101");
    }

    [Fact]
    public async Task ResultTransaction_RollsBackSavedChangesOnBusinessFailure()
    {
        await using var db = Db(); var key = "trial.rollback." + Guid.NewGuid();
        var executor = new PostgresApplicationTransactionExecutor(db);
        var result = await executor.ExecuteResultAsync<int>(async ct =>
        {
            db.SystemSettings.Add(new SystemSetting { Key = key, ValueJson = "{}" }); await db.SaveChangesAsync(ct);
            return MealMate.Application.Common.Models.Error.Conflict("trial.failed");
        });
        result.IsFailure.Should().BeTrue();
        await using var verify = Db(); (await verify.SystemSettings.AnyAsync(x => x.Key == key)).Should().BeFalse();
    }
}
