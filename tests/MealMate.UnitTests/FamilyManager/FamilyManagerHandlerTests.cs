using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilyManagerHandlerTests
{
    [Fact]
    public async Task Home_returns_members_and_wave_shortcuts_for_manager()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.GroupId.Should().Be(fx.GroupId);
        result.Value.Members.Should().HaveCount(2);
        result.Value.Shortcuts.Should().OnlyContain(s => !s.Enabled);
        result.Value.Shortcuts.Select(s => s.Code).Should().BeEquivalentTo(
            ["addresses", "subscriptions", "payments", "reports"]);
    }

    [Fact]
    public async Task Home_forbidden_for_non_manager()
    {
        var fx = await SeedAsync();
        fx.AsLinkedMember();
        var result = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.NotManager);
    }

    [Fact]
    public async Task Update_access_invite_and_accept_roundtrip()
    {
        var fx = await SeedAsync();
        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.InviteDeepLink.Should().NotBeNullOrEmpty();
        var token = access.Value.InviteDeepLink!.Split('/').Last();

        fx.AsLinkedMember();
        var accept = await fx.Handlers.Handle(new AcceptFamilyInviteCommand(token), CancellationToken.None);
        accept.IsSuccess.Should().BeTrue();
        accept.Value.LinkedCustomerUserId.Should().Be(fx.LinkedUserId);
    }

    [Fact]
    public async Task Pause_blocks_are_persisted()
    {
        var fx = await SeedAsync();
        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: null, IsPaused: true, Remove: null)),
            CancellationToken.None);
        access.IsSuccess.Should().BeTrue();
        access.Value.IsPaused.Should().BeTrue();

        var again = await fx.Handlers.Handle(new GetFamilyMemberAccessQuery(fx.MemberId), CancellationToken.None);
        again.Value.IsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task Cannot_remove_manager()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.ManagerMemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: null, IsPaused: null, Remove: true)),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.CannotRemoveManager);
    }

    [Fact]
    public async Task Create_member_requires_payment_in_wave1()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new CreateFamilyMemberCommand(new CreateFamilyMemberRequest(
                "Sara", "daughter", FamilyMemberGender.Female, 12, "balanced",
                FamilyActivityLevel.Moderate, null, null, null, null, false)),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.MemberRequiresPayment);
    }

    [Fact]
    public async Task Permissions_update_for_member_and_immutable_for_manager()
    {
        var fx = await SeedAsync();
        var ok = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.MemberId, new UpdateFamilyMemberPermissionsRequest(
                true, false, true, false, false, false, false, false)),
            CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.EditDailyMeals.Should().BeTrue();

        var bad = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.ManagerMemberId, new UpdateFamilyMemberPermissionsRequest(
                true, true, true, true, true, true, true, true)),
            CancellationToken.None);
        bad.IsFailure.Should().BeTrue();
        bad.Error.Code.Should().Be(MessageCodes.Family.ManagerPermissionsImmutable);
    }

    private static async Task<Fixture> SeedAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var managerUserId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        var program = new Program { Code = "BAL", NameAr = "متوازن", NameEn = "Balanced", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "3M", NameAr = "3", NameEn = "3",
            BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 0,
            MealsPerDay = 3, Status = ProgramStatus.Active, IsActive = true
        };
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerUserId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Active,
            QuotedFinalPrice = 37.5m,
            CurrencyCode = "KWD"
        };
        var managerSub = new Subscription
        {
            CustomerUserId = managerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(20m, "KWD"),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        var memberSub = new Subscription
        {
            CustomerUserId = managerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(17.5m, "KWD"),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        db.AddRange(program, bundle, group, managerSub, memberSub);
        await db.SaveChangesAsync();

        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            MemberSubscriptionId = managerSub.Id
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "يوسف",
            RelationshipCode = "son",
            Gender = FamilyMemberGender.Male,
            AgeYears = 16,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Active,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            MemberSubscriptionId = memberSub.Id
        };
        db.AddRange(managerMember, member);
        await db.SaveChangesAsync();
        db.AddRange(
            new FamilyMemberAccess { FamilyMemberId = managerMember.Id },
            new FamilyMemberAccess { FamilyMemberId = member.Id },
            new FamilyMemberPermissions { FamilyMemberId = managerMember.Id },
            new FamilyMemberPermissions { FamilyMemberId = member.Id });
        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerUserId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 25));
        var pricing = Substitute.For<IFamilyPricingSettingsReader>();
        pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings
        {
            Packages =
            [
                new FamilySizePackageSettings
                {
                    NameAr = "باقة عائلية", NameEn = "Family package", MaxMembers = 4, IsActive = true
                }
            ]
        });

        var handlers = new FamilyManagerHandlers(db, user, clock, pricing);
        return new Fixture(db, handlers, user, managerUserId, linkedUserId, group.Id, managerMember.Id, member.Id);
    }

    private sealed class Fixture(
        AppDbContext Db,
        FamilyManagerHandlers Handlers,
        ICurrentUser User,
        Guid ManagerUserId,
        Guid LinkedUserId,
        Guid GroupId,
        Guid ManagerMemberId,
        Guid MemberId)
    {
        public AppDbContext Db { get; } = Db;
        public FamilyManagerHandlers Handlers { get; } = Handlers;
        public Guid LinkedUserId { get; } = LinkedUserId;
        public Guid GroupId { get; } = GroupId;
        public Guid ManagerMemberId { get; } = ManagerMemberId;
        public Guid MemberId { get; } = MemberId;

        public void AsLinkedMember()
        {
            User.UserId.Returns(LinkedUserId);
            User.UserType.Returns(UserType.Customer);
        }
    }
}
