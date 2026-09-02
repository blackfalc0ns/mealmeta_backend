using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilyCalendarAccessGateTests
{
    [Fact]
    public async Task Individual_owner_can_write()
    {
        var fx = await SeedIndividualAsync();
        var gate = new FamilyCalendarAccessGate(fx.Db, fx.Owner);
        var result = await gate.AuthorizeAsync(fx.Subscription, requireWrite: true, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Manager_can_write_member_subscription()
    {
        var fx = await SeedFamilyAsync();
        var gate = new FamilyCalendarAccessGate(fx.Db, fx.Manager);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.IsManager.Should().BeTrue();
    }

    [Fact]
    public async Task Linked_member_with_flag_can_write()
    {
        var fx = await SeedFamilyAsync(linkMember: true, editMeals: true);
        var gate = new FamilyCalendarAccessGate(fx.Db, fx.LinkedMember);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Linked_member_without_flag_cannot_write()
    {
        var fx = await SeedFamilyAsync(linkMember: true, editMeals: false);
        var gate = new FamilyCalendarAccessGate(fx.Db, fx.LinkedMember);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.PermissionDenied);
    }

    [Fact]
    public async Task Paused_member_blocks_writes()
    {
        var fx = await SeedFamilyAsync(paused: true);
        var gate = new FamilyCalendarAccessGate(fx.Db, fx.Manager);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.MemberPaused);
    }

    [Fact]
    public async Task Inactive_family_group_allows_direct_subscription_owner()
    {
        var fx = await SeedFamilyAsync();
        var group = await fx.Db.FamilyGroups.FirstAsync();
        group.Status = FamilyGroupStatus.Draft;
        await fx.Db.SaveChangesAsync();

        var gate = new FamilyCalendarAccessGate(fx.Db, fx.Manager);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Inactive_family_group_blocks_non_owner_member()
    {
        var fx = await SeedFamilyAsync(linkMember: true, editMeals: true);
        var group = await fx.Db.FamilyGroups.FirstAsync();
        group.Status = FamilyGroupStatus.Draft;
        await fx.Db.SaveChangesAsync();

        var gate = new FamilyCalendarAccessGate(fx.Db, fx.LinkedMember);
        var result = await gate.AuthorizeAsync(fx.MemberSubscription, requireWrite: true, CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.GroupNotActive);
    }

    private static async Task<(AppDbContext Db, ICurrentUser Owner, Subscription Subscription)> SeedIndividualAsync()
    {
        var db = NewDb();
        var userId = Guid.NewGuid();
        var sub = new Subscription
        {
            CustomerUserId = userId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(10m, "KWD"),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        db.Add(sub);
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.UserType.Returns(UserType.Customer);
        return (db, user, sub);
    }

    private static async Task<FamilyFx> SeedFamilyAsync(
        bool linkMember = false,
        bool editMeals = false,
        bool paused = false)
    {
        var db = NewDb();
        var managerId = Guid.NewGuid();
        var linkedId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Active,
            CurrencyCode = "KWD"
        };
        var memberSub = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(10m, "KWD"),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "Ahmed",
            RelationshipCode = "son",
            Gender = FamilyMemberGender.Male,
            AgeYears = 20,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            MemberSubscriptionId = memberSub.Id
        };
        var access = new FamilyMemberAccess
        {
            FamilyMemberId = member.Id,
            HasSeparateAccount = linkMember,
            LinkedCustomerUserId = linkMember ? linkedId : null,
            IsPaused = paused,
            PausedAtUtc = paused ? DateTime.UtcNow : null
        };
        var permissions = new FamilyMemberPermissions
        {
            FamilyMemberId = member.Id,
            EditDailyMeals = editMeals
        };
        // Fix FKs: member.Id is set on construction; access needs same id after member created
        db.AddRange(group, memberSub);
        await db.SaveChangesAsync();
        member.MemberSubscriptionId = memberSub.Id;
        member.FamilyGroupId = group.Id;
        db.Add(member);
        await db.SaveChangesAsync();
        access.FamilyMemberId = member.Id;
        permissions.FamilyMemberId = member.Id;
        memberSub.FamilyGroupId = group.Id;
        db.AddRange(access, permissions);
        await db.SaveChangesAsync();

        var manager = Substitute.For<ICurrentUser>();
        manager.UserId.Returns(managerId);
        manager.UserType.Returns(UserType.Customer);
        var linked = Substitute.For<ICurrentUser>();
        linked.UserId.Returns(linkedId);
        linked.UserType.Returns(UserType.Customer);
        return new FamilyFx(db, manager, linked, memberSub);
    }

    private static AppDbContext NewDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed record FamilyFx(
        AppDbContext Db,
        ICurrentUser Manager,
        ICurrentUser LinkedMember,
        Subscription MemberSubscription);
}

