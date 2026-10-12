using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

/// <summary>
/// FamilyActionPolicy decision matrix: the manager manages everything,
/// members act only through granted keys (default deny on the new keys),
/// read keys never grant write, and pause/removal cuts access immediately.
/// </summary>
public sealed class FamilyActionPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Capabilities_endpoint_rejects_outsider_instead_of_disclosing_member_decisions()
    {
        using var fx = new Fixture();
        var actor = Substitute.For<ICurrentUser>();
        actor.UserId.Returns(Guid.NewGuid());
        actor.UserType.Returns(UserType.Customer);
        var result = await new GetMyFamilyCapabilitiesHandler(fx.Db, actor)
            .Handle(new GetMyFamilyCapabilitiesQuery(fx.Member.Id), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Manager_Manages_All_Actions_On_Every_Member_Even_When_Paused()
    {
        using var fx = new Fixture();
        fx.MemberAccess.IsPaused = true;
        await fx.Db.SaveChangesAsync();

        foreach (var action in new[]
                 {
                     FamilyAction.ViewCalendar, FamilyAction.ViewInvoices, FamilyAction.EditDailyMeals,
                     FamilyAction.ConfirmMeals, FamilyAction.RequestFreeze, FamilyAction.RequestRenewal,
                     FamilyAction.RequestUpgrade, FamilyAction.CancelSubscription, FamilyAction.ChangePassword,
                 })
        {
            var decision = await fx.Policy.EvaluateAsync(fx.ManagerId, fx.Member.Id, action);
            decision.Allowed.Should().BeTrue($"manager manages everything: {action}");
        }
    }

    [Fact]
    public async Task Member_Default_Deny_On_All_New_Keys_When_No_Permissions_Row()
    {
        using var fx = new Fixture(seedPermissions: false);

        foreach (var action in new[]
                 {
                     FamilyAction.ViewCalendar, FamilyAction.ViewInvoices, FamilyAction.SelectRestaurant,
                     FamilyAction.ConfirmMeals, FamilyAction.RequestFreeze, FamilyAction.RequestRenewal,
                     FamilyAction.RequestUpgrade, FamilyAction.CancelSubscription,
                 })
        {
            var decision = await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, action);
            decision.Allowed.Should().BeFalse($"new keys default deny: {action}");
            decision.ReasonCode.Should().Be("family.permission_required");
        }
    }

    [Fact]
    public async Task Member_Granted_Keys_Allow_Own_Actions_And_Read_Never_Grants_Write()
    {
        using var fx = new Fixture();
        fx.Permissions!.ViewCalendar = true;
        fx.Permissions.ViewInvoices = true;
        fx.Permissions.ConfirmMeals = true;
        await fx.Db.SaveChangesAsync();

        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ViewCalendar)).Allowed
            .Should().BeTrue();
        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ViewInvoices)).Allowed
            .Should().BeTrue();
        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ConfirmMeals)).Allowed
            .Should().BeTrue();

        // Read keys never grant write; ungranted writes stay denied.
        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.EditDailyMeals)).Allowed
            .Should().BeFalse();
        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.EditMealsAndDays)).Allowed
            .Should().BeFalse();
        (await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ChangePassword)).Allowed
            .Should().BeFalse();
    }

    [Fact]
    public async Task Family_Scope_View_Keys_Are_Distinct_From_Own_Keys()
    {
        using var fx = new Fixture();
        fx.Permissions!.ViewFamilyCalendars = true;
        fx.Permissions.ViewFamilyPlans = true;
        fx.Permissions.ViewFamilyInvoices = true;
        await fx.Db.SaveChangesAsync();

        // The sibling member (actor) evaluating the OTHER member's row: family-scope
        // keys are read grants for the actor's own consumption; acting on another
        // member's row is always denied to a non-manager.
        var siblingDecision = await fx.Policy.EvaluateAsync(fx.SiblingUserId, fx.Member.Id, FamilyAction.ViewCalendar);
        siblingDecision.Allowed.Should().BeFalse();
        siblingDecision.ReasonCode.Should().Be("family.not_member");
    }

    [Fact]
    public async Task Member_Without_Separate_Account_Is_Denied_Like_The_Gate()
    {
        // R07: capabilities must report the same denial the calendar gate enforces
        // when the member's independent account is disabled.
        using var fx = new Fixture();
        fx.Permissions!.ViewCalendar = true;
        fx.MemberAccess.HasSeparateAccount = false;
        await fx.Db.SaveChangesAsync();

        var decision = await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ViewCalendar);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("family.access_disabled");
    }

    [Fact]
    public async Task Family_Scope_Grant_Lets_Sibling_Read_Another_Member_Capability()
    {
        // R07/B11: the actor's own ViewFamilyCalendars grant permits reading a
        // sibling's calendar capability — the policy and the gate agree.
        using var fx = new Fixture();
        var siblingAccess = new FamilyMemberAccess
        {
            FamilyMemberId = fx.SiblingMember.Id,
            HasSeparateAccount = true,
            LinkedCustomerUserId = fx.SiblingUserId,
        };
        var siblingPermissions = new FamilyMemberPermissions
        {
            FamilyMemberId = fx.SiblingMember.Id,
            ViewFamilyCalendars = true,
        };
        fx.Db.FamilyMemberAccesses.Add(siblingAccess);
        fx.Db.FamilyMemberPermissions.Add(siblingPermissions);
        await fx.Db.SaveChangesAsync();

        var decision = await fx.Policy.EvaluateAsync(fx.SiblingUserId, fx.Member.Id, FamilyAction.ViewCalendar);
        decision.Allowed.Should().BeTrue();

        // Reads never grant writes: a sibling write stays denied.
        var write = await fx.Policy.EvaluateAsync(fx.SiblingUserId, fx.Member.Id, FamilyAction.EditDailyMeals);
        write.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task Outside_User_Is_Denied_With_Not_Member()
    {
        using var fx = new Fixture();

        var decision = await fx.Policy.EvaluateAsync(Guid.NewGuid(), fx.Member.Id, FamilyAction.ViewCalendar);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("family.not_member");
    }

    [Fact]
    public async Task Paused_Access_Cuts_Member_Access_Immediately()
    {
        using var fx = new Fixture();
        fx.Permissions!.ViewCalendar = true;
        fx.Permissions.EditDailyMeals = true;
        fx.MemberAccess.IsPaused = true;
        await fx.Db.SaveChangesAsync();

        var read = await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ViewCalendar);
        read.Allowed.Should().BeFalse();
        read.ReasonCode.Should().Be("family.access_paused");

        var write = await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.EditDailyMeals);
        write.Allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(FamilyAction.ViewCalendar)]
    [InlineData(FamilyAction.ViewPlan)]
    [InlineData(FamilyAction.ViewInvoices)]
    public async Task Sibling_cannot_view_removed_target_even_with_family_grants(FamilyAction action)
    {
        using var fx = new Fixture();
        fx.Db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = fx.SiblingMember.Id,
            HasSeparateAccount = true, LinkedCustomerUserId = fx.SiblingUserId });
        fx.Db.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = fx.SiblingMember.Id,
            ViewFamilyCalendars = true, ViewFamilyPlans = true, ViewFamilyInvoices = true });
        fx.MemberAccess.RemovedAtUtc = Now;
        await fx.Db.SaveChangesAsync();
        var decision = await fx.Policy.EvaluateAsync(fx.SiblingUserId, fx.Member.Id, action);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("family.member_not_found");
        (await fx.Policy.EvaluateAsync(fx.ManagerId, fx.Member.Id, action)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Removed_Member_Loses_Linked_Access_But_Manager_Keeps_Managing()
    {
        using var fx = new Fixture();
        fx.Permissions!.ViewCalendar = true;
        fx.MemberAccess.RemovedAtUtc = Now;
        await fx.Db.SaveChangesAsync();

        var memberDecision = await fx.Policy.EvaluateAsync(fx.MemberUserId, fx.Member.Id, FamilyAction.ViewCalendar);
        memberDecision.Allowed.Should().BeFalse();
        memberDecision.ReasonCode.Should().Be("family.not_member");

        var managerDecision = await fx.Policy.EvaluateAsync(fx.ManagerId, fx.Member.Id, FamilyAction.CancelSubscription);
        managerDecision.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Converted_Manager_Keeps_Supported_Financial_Actions()
    {
        using var fx = new Fixture(convertedFamily: true);
        fx.Permissions = null;
        await fx.Db.FamilyMemberPermissions.AddAsync(new FamilyMemberPermissions
        {
            FamilyMemberId = fx.ManagerMember.Id,
            RequestRenewal = true,
            RequestUpgrade = true,
            CancelSubscription = true,
            EditMealsAndDays = true,
            ChangeProgram = true,
        });
        await fx.Db.SaveChangesAsync();

        foreach (var action in new[]
                 {
                     FamilyAction.RequestRenewal, FamilyAction.RequestUpgrade,
                     FamilyAction.CancelSubscription,
                 })
        {
            var decision = await fx.Policy.EvaluateAsync(fx.ManagerId, fx.ManagerMember.Id, action);
            decision.Allowed.Should().BeTrue($"manager retains supported action {action}");
            decision.ReasonCode.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(FamilyAction.EditMealsAndDays)]
    [InlineData(FamilyAction.ChangeProgram)]
    [InlineData(FamilyAction.ChangeDeliveryTime)]
    [InlineData(FamilyAction.UnitExchange)]
    public async Task Unimplemented_actions_are_unavailable_even_for_manager(FamilyAction action)
    {
        using var fx = new Fixture();
        var decision = await fx.Policy.EvaluateAsync(fx.ManagerId, fx.Member.Id, action);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be("family.action_not_available");
    }

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }
        public FamilyActionPolicy Policy { get; }
        public Guid ManagerId { get; }
        public Guid MemberUserId { get; }
        public Guid SiblingUserId { get; }
        public FamilyGroup Group { get; }
        public FamilyMember ManagerMember { get; }
        public FamilyMember Member { get; }
        public FamilyMember SiblingMember { get; private set; } = null!;
        public FamilyMemberAccess MemberAccess { get; }
        public FamilyMemberPermissions? Permissions { get; set; }

        public Fixture(bool seedPermissions = true, bool convertedFamily = false)
        {
            Db = new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                Substitute.For<IDomainEventDispatcher>());
            Policy = new FamilyActionPolicy(Db);

            ManagerId = Guid.NewGuid();
            MemberUserId = Guid.NewGuid();
            SiblingUserId = Guid.NewGuid();

            Group = new FamilyGroup
            {
                ManagerCustomerUserId = ManagerId,
                Name = "Family",
                Status = FamilyGroupStatus.Active,
                ConvertedFromSubscriptionId = convertedFamily ? Guid.NewGuid() : null,
            };
            ManagerMember = new FamilyMember
            {
                FamilyGroupId = Group.Id,
                Role = FamilyMemberRole.Manager,
                DisplayName = "Manager",
                RelationshipCode = "self",
                NutritionalGoalCode = "maintain",
            };
            Member = new FamilyMember
            {
                FamilyGroupId = Group.Id,
                Role = FamilyMemberRole.Member,
                DisplayName = "Member",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            Group.Members.Add(ManagerMember);
            Group.Members.Add(Member);
            SiblingMember = new FamilyMember
            {
                FamilyGroupId = Group.Id,
                Role = FamilyMemberRole.Member,
                DisplayName = "Sibling",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            Group.Members.Add(SiblingMember);

            Db.FamilyGroups.Add(Group);
            Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = ManagerMember.Id,
                HasSeparateAccount = false,
                LinkedCustomerUserId = ManagerId,
            });
            MemberAccess = new FamilyMemberAccess
            {
                FamilyMemberId = Member.Id,
                HasSeparateAccount = true,
                LinkedCustomerUserId = MemberUserId,
            };
            Db.FamilyMemberAccesses.Add(MemberAccess);
            if (seedPermissions)
            {
                Permissions = new FamilyMemberPermissions { FamilyMemberId = Member.Id };
                Db.FamilyMemberPermissions.Add(Permissions);
            }
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }
}
