using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Audit;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class AuditQueriesTests
{
    [Fact]
    public async Task List_enriches_actor_and_user_target_with_one_batch_lookup()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var unknownActorId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = CreateDbContext();
        db.AuditLogs.AddRange(
            Audit(actorId, "UserRoleAssigned", "User", targetId, now),
            Audit(null, "UserLoggedIn", "uSeR", targetId, now.AddMinutes(-1)),
            Audit(unknownActorId, "RoleCreated", "Role", Guid.NewGuid(), now.AddMinutes(-2)));
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new UserIdentityDto(actorId, "Security Admin", "security@mealmate.local"),
                new UserIdentityDto(targetId, "Target User", "target@mealmate.local"),
            ]);

        var result = await new ListAuditLogsQueryHandler(db, users)
            .Handle(new ListAuditLogsQuery(PageSize: 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var assigned = result.Value.Items.Single(item => item.Action == "UserRoleAssigned");
        assigned.ActorFullName.Should().Be("Security Admin");
        assigned.ActorEmail.Should().Be("security@mealmate.local");
        assigned.TargetDisplayName.Should().Be("Target User");
        assigned.TargetEmail.Should().Be("target@mealmate.local");

        var login = result.Value.Items.Single(item => item.Action == "UserLoggedIn");
        login.ActorUserId.Should().BeNull();
        login.ActorFullName.Should().BeNull();
        login.ActorEmail.Should().BeNull();
        login.TargetDisplayName.Should().Be("Target User");
        login.TargetEmail.Should().Be("target@mealmate.local");

        var unknown = result.Value.Items.Single(item => item.ActorUserId == unknownActorId);
        unknown.ActorFullName.Should().BeNull();
        unknown.ActorEmail.Should().BeNull();
        unknown.TargetDisplayName.Should().BeNull();
        unknown.TargetEmail.Should().BeNull();

        await users.Received(1).GetUserIdentitiesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 3
                && ids.Contains(actorId)
                && ids.Contains(targetId)
                && ids.Contains(unknownActorId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_enriches_actor_and_user_target()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var log = Audit(actorId, "AdminStatusChanged", "User", targetId, DateTime.UtcNow);

        await using var db = CreateDbContext();
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([
                new UserIdentityDto(actorId, "Actor", "actor@mealmate.local"),
                new UserIdentityDto(targetId, "Target", "target@mealmate.local"),
            ]);

        var result = await new GetAuditLogQueryHandler(db, users)
            .Handle(new GetAuditLogQuery(log.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActorFullName.Should().Be("Actor");
        result.Value.TargetDisplayName.Should().Be("Target");
    }

    [Fact]
    public async Task ListEntity_delegates_identity_enrichment()
    {
        var targetId = Guid.NewGuid();
        await using var db = CreateDbContext();
        db.AuditLogs.Add(Audit(null, "TotpLoginVerified", "User", targetId, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([new UserIdentityDto(targetId, "Signed In User", "signed-in@mealmate.local")]);

        var result = await new ListEntityAuditLogsQueryHandler(db, users)
            .Handle(new ListEntityAuditLogsQuery("User", targetId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].ActorUserId.Should().BeNull();
        result.Value.Items[0].TargetDisplayName.Should().Be("Signed In User");
    }

    private static AuditLog Audit(
        Guid? actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        DateTime occurredAtUtc) =>
        new()
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            SourceChannel = "Web",
            OccurredAtUtc = occurredAtUtc,
        };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
