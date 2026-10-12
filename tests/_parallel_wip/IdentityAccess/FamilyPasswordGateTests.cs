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
/// B14: a family member's ordinary password change is gated by the manager's
/// ChangePassword permission; recovery/OTP flows are untouched. Real PostgreSQL
/// proves the policy query path.
/// </summary>
public sealed class FamilyPasswordGateTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Password_change_without_family_permission_is_refused()
    {
        var (db, handler) = await BuildHandlerAsync(grantChangePassword: false);
        using var _ = db;
        var result = await handler.Handle(new ChangeCustomerPasswordCommand("Old#1", "New#2Pass"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.permission_required");
    }

    [Fact]
    public async Task Password_change_with_permission_passes_the_gate()
    {
        var (db, handler) = await BuildHandlerAsync(grantChangePassword: true);
        using var _ = db;
        var result = await handler.Handle(new ChangeCustomerPasswordCommand("Old#1", "New#2Pass"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
    }

    private async Task<(AppDbContext Db, ChangeCustomerPasswordCommandHandler Handler)> BuildHandlerAsync(bool grantChangePassword)
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
        return (db, handler);
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
