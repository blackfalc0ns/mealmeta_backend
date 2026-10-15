using FluentAssertions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.CustomerCheckout;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.IdentityAccess;

/// <summary>
/// Customer credentials remain customer-owned while family data access remains
/// independently gated. Real PostgreSQL verifies the persisted membership path.
/// </summary>
public sealed class FamilyPasswordGateTests : IAsyncLifetime
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

    [Fact]
    public async Task Password_change_without_family_permission_remains_customer_owned()
    {
        var (db, handler, users) = await BuildHandlerAsync(grantChangePassword: false);
        using var _ = db;
        var result = await handler.Handle(new ChangeCustomerPasswordCommand("Old#1", "New#2Pass"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await users.Received(1).ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Password_change_with_permission_passes_the_gate()
    {
        var (db, handler, users) = await BuildHandlerAsync(grantChangePassword: true);
        using var _ = db;
        var result = await handler.Handle(new ChangeCustomerPasswordCommand("Old#1", "New#2Pass"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await users.Received(1).ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Family_access_disabled_or_paused_does_not_take_away_customer_credentials(bool separate, bool paused)
    {
        var (db, handler, users) = await BuildHandlerAsync(true);
        using var _ = db;
        var access = await db.FamilyMemberAccesses.SingleAsync();
        access.HasSeparateAccount = separate;
        access.IsPaused = paused;
        await db.SaveChangesAsync();
        var result = await handler.Handle(new ChangeCustomerPasswordCommand("Old#1", "New#2Pass"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await users.Received(1).ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await new FamilyActionPolicy(db).EvaluateAsync(access.LinkedCustomerUserId!.Value,
            access.FamilyMemberId, FamilyAction.ViewCalendar)).Allowed.Should().BeFalse();
    }

    private async Task<(AppDbContext Db, ChangeCustomerPasswordCommandHandler Handler, IUserAccountService Users)> BuildHandlerAsync(bool grantChangePassword)
    {
        var db = CreateDb();
        var managerId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Active,
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "Member",
            RelationshipCode = "child",
            NutritionalGoalCode = "maintain",
        };
        group.Members.Add(member);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            HasSeparateAccount = true,
            LinkedCustomerUserId = linkedUserId,
        });
        db.FamilyMemberPermissions.Add(new FamilyMemberPermissions
        {
            FamilyMemberId = member.Id,
            ChangePassword = grantChangePassword,
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(linkedUserId);
        currentUser.UserType.Returns(UserType.Customer);
        var users = Substitute.For<IUserAccountService>();
        users.ChangePasswordAsync(linkedUserId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var sessions = Substitute.For<IAuthSessionService>();
        sessions.RevokeAllUserTokensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        var audit = Substitute.For<IAuditLogger>();
        var policy = new FamilyActionPolicy(db);
        var handler = new ChangeCustomerPasswordCommandHandler(db, currentUser, users, sessions, audit, policy);
        return (db, handler, users);
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
