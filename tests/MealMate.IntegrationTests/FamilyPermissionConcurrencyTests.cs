using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.FamilyManager;

public sealed class FamilyPermissionConcurrencyTests : IAsyncLifetime
{
    private MealMate.Testing.PostgresTestDatabase _container = null!;
    public async Task InitializeAsync()
    {
        _container = new();
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_updates_with_same_version_commit_once_and_return_conflict(bool seedPermissions)
    {
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Concurrency", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        group.Members.Add(member);
        await using (var db = CreateDb())
        {
            db.Add(group);
            if (seedPermissions) db.Add(new FamilyMemberPermissions { FamilyMemberId = member.Id });
            await db.SaveChangesAsync();
        }
        var barrier = new PermissionWriteBarrier();
        await using var first = CreateDb(barrier);
        await using var second = CreateDb(barrier);
        var request = new UpdateFamilyMemberPermissionsRequest(false, false, false, false, false, false, false, false,
            ExpectedVersion: 0, ViewCalendar: true, ViewFamilyPlans: true);
        var results = await Task.WhenAll(
            Handler(first, manager).Handle(new UpdateFamilyMemberPermissionsCommand(member.Id, request), CancellationToken.None),
            Handler(second, manager).Handle(new UpdateFamilyMemberPermissionsCommand(member.Id, request with { ViewInvoices = true }), CancellationToken.None));
        results.Count(x => x.IsSuccess).Should().Be(1);
        results.Single(x => x.IsFailure).Error.Code.Should().Be("family.permissions_changed");
        await using var read = CreateDb();
        var persisted = await read.FamilyMemberPermissions.SingleAsync(x => x.FamilyMemberId == member.Id);
        persisted.Version.Should().Be(1);
        persisted.ViewCalendar.Should().BeTrue();
        persisted.ViewFamilyPlans.Should().BeTrue();
        persisted.ViewInvoices.Should().Be(results[1].IsSuccess);
    }

    private static FamilyManagerHandlers Handler(AppDbContext db, Guid manager)
    {
        var actor = Substitute.For<ICurrentUser>();
        actor.UserId.Returns(manager);
        actor.UserType.Returns(UserType.Customer);
        return new(db, actor, Substitute.For<IDateTimeProvider>(), Substitute.For<IFamilyPricingSettingsReader>(), Substitute.For<IUserAccountService>());
    }
    private AppDbContext CreateDb(IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, Substitute.For<IDomainEventDispatcher>());
    }
    private sealed class PermissionWriteBarrier : SaveChangesInterceptor
    {
        private int _arrived;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrived) == 2) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
