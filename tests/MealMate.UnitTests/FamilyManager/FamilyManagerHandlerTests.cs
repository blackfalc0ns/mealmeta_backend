using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using FamilyMembersQuery = MealMate.Application.Features.FamilyManager.GetFamilyMembersQuery;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilyManagerHandlerTests
{
    [Fact]
    public async Task Home_returns_enabled_shortcuts_and_unavailable_wallet()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.GroupId.Should().Be(fx.GroupId);
        result.Value.Members.Should().HaveCount(2);
        result.Value.Wallet.Available.Should().BeFalse();
        result.Value.Wallet.UnavailableReason.Should().Be("wallet_not_implemented");
        result.Value.Shortcuts.Select(s => s.Code).Should().Equal("calendar", "meals", "subscriptions", "wallet");
        result.Value.Shortcuts.Should().Contain(s => s.Code == "calendar" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "meals" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "subscriptions" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "wallet" && !s.Enabled && s.UnavailableReason == "wallet_not_implemented");
        result.Value.Members.Should().Contain(m => m.MemberId == fx.MemberId && m.RemainingDays == 7);
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
    public async Task Members_list_is_lightweight()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        var yusuf = result.Value.Single(m => m.MemberId == fx.MemberId);
        yusuf.AgeYears.Should().Be(16);
        yusuf.RemainingDays.Should().Be(7);
        yusuf.ProgramNameAr.Should().Be("متوازن");
    }

    [Fact]
    public async Task Member_detail_includes_remaining_days_renewal_and_today_nutrition()
    {
        var fx = await SeedAsync(withTodayMeal: true);
        var result = await fx.Handlers.Handle(new GetFamilyMemberQuery(fx.MemberId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PhoneNumber.Should().Be("+96551111112");
        result.Value.RemainingDays.Should().Be(7);
        result.Value.StartDate.Should().Be(new DateOnly(2026, 8, 1));
        result.Value.EndDate.Should().Be(new DateOnly(2026, 8, 31));
        result.Value.CanRenew.Should().BeTrue();
        result.Value.RenewalPath.Should().Be($"/api/v1/customer/subscriptions/{fx.MemberSubscriptionId}/renewal-requests");
        result.Value.TodayNutrition.Should().NotBeNull();
        result.Value.TodayNutrition!.Calories.Should().Be(500);
        result.Value.TodayNutrition.ProteinG.Should().Be(40);
        result.Value.TodayNutrition.CarbsG.Should().Be(30);
        result.Value.TodayNutrition.FatG.Should().Be(20);
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
        accept.Value.GeneratedPassword.Should().BeNull();
    }

    [Fact]
    public async Task Accept_invite_binds_accepter_default_address_when_member_had_manager_address()
    {
        var fx = await SeedAsync();
        var managerUserId = (await fx.Db.FamilyGroups.SingleAsync(g => g.Id == fx.GroupId)).ManagerCustomerUserId;
        var managerAddress = new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = managerUserId,
            Label = "Manager home",
            AddressLine = "Manager Street",
            IsDefault = true,
        };
        var accepterAddress = new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = fx.LinkedUserId,
            Label = "Accepter home",
            AddressLine = "Accepter Street",
            IsDefault = true,
        };
        fx.Db.CustomerAddresses.AddRange(managerAddress, accepterAddress);
        var member = await fx.Db.FamilyMembers.SingleAsync(m => m.Id == fx.MemberId);
        member.DeliveryAddressId = managerAddress.Id;
        await fx.Db.SaveChangesAsync();

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);
        access.IsSuccess.Should().BeTrue();
        var token = access.Value.InviteDeepLink!.Split('/').Last();

        fx.AsLinkedMember();
        var accept = await fx.Handlers.Handle(new AcceptFamilyInviteCommand(token), CancellationToken.None);
        accept.IsSuccess.Should().BeTrue();

        var updated = await fx.Db.FamilyMembers.SingleAsync(m => m.Id == fx.MemberId);
        updated.DeliveryAddressId.Should().Be(accepterAddress.Id);
    }

    [Fact]
    public async Task Enabling_access_returns_generated_password_once_for_new_login()
    {
        var fx = await SeedAsync();
        fx.Users.FindByPhoneAsync("+96551111112", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                fx.ProvisionedUserId, null, "+96551111112", "يوسف", UserType.Customer,
                AccountStatus.Active, null, false, true, DateTime.UtcNow.AddDays(-10), null));
        fx.Users.GeneratePasswordResetTokenAsync(fx.ProvisionedUserId, Arg.Any<CancellationToken>())
            .Returns("reset-token");
        fx.Users.ResetPasswordAsync(fx.ProvisionedUserId, "reset-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.GeneratedPassword.Should().NotBeNullOrWhiteSpace();
        access.Value.HasExistingLogin.Should().BeFalse();
        access.Value.LinkedCustomerUserId.Should().Be(fx.ProvisionedUserId);

        var again = await fx.Handlers.Handle(new GetFamilyMemberAccessQuery(fx.MemberId), CancellationToken.None);
        again.Value.GeneratedPassword.Should().BeNull();
        again.Value.HasSeparateAccount.Should().BeTrue();
        again.Value.PhoneNumber.Should().Be("+96551111112");
    }

    [Fact]
    public async Task Enabling_access_does_not_reset_password_for_existing_login()
    {
        var fx = await SeedAsync();
        fx.Users.FindByPhoneAsync("+96551111112", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                fx.ProvisionedUserId, null, "+96551111112", "يوسف", UserType.Customer,
                AccountStatus.Active, null, false, true, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddDays(-1)));

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.HasExistingLogin.Should().BeTrue();
        access.Value.GeneratedPassword.Should().BeNull();
        access.Value.LinkedCustomerUserId.Should().Be(fx.ProvisionedUserId);
        await fx.Users.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
    public async Task Create_member_tells_client_to_use_checkout_flow()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new CreateFamilyMemberCommand(new CreateFamilyMemberRequest(
                "Sara", "daughter", FamilyMemberGender.Female, 12, "balanced",
                FamilyActivityLevel.Moderate, null, null, null, null, false)),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.UseCheckoutFlow);
    }

    [Fact]
    public async Task Permissions_include_grouped_labels_and_are_immutable_for_manager()
    {
        var fx = await SeedAsync();
        var ok = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.MemberId, new UpdateFamilyMemberPermissionsRequest(
                true, false, true, false, false, false, false, false)),
            CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.EditDailyMeals.Should().BeTrue();
        ok.Value.Groups.Should().HaveCount(4);
        ok.Value.Groups.Select(g => g.Code).Should().Equal("meals", "program", "delivery", "account");
        ok.Value.Groups.Single(g => g.Code == "meals").Permissions
            .Single(p => p.Key == "editDailyMeals").Value.Should().BeTrue();

        var loaded = await fx.Handlers.Handle(new GetFamilyMemberPermissionsQuery(fx.MemberId), CancellationToken.None);
        loaded.Value.Groups.Should().HaveCount(4);

        var bad = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.ManagerMemberId, new UpdateFamilyMemberPermissionsRequest(
                true, true, true, true, true, true, true, true)),
            CancellationToken.None);
        bad.IsFailure.Should().BeTrue();
        bad.Error.Code.Should().Be(MessageCodes.Family.ManagerPermissionsImmutable);
    }

    private static async Task<Fixture> SeedAsync(bool withTodayMeal = false)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var managerUserId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        var provisionedUserId = Guid.NewGuid();
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
        var start = new DateOnly(2026, 8, 1);
        var end = new DateOnly(2026, 8, 31);
        var managerSub = new Subscription
        {
            CustomerUserId = managerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = start,
            EndDate = end,
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
            DurationDays = 26,
            StartDate = start,
            EndDate = end,
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
            PhoneNumber = "+96551111111",
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
            PhoneNumber = "+96551111112",
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

        if (withTodayMeal)
        {
            var day = new SubscriptionDay
            {
                SubscriptionId = memberSub.Id,
                ServiceDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionDayStatus.CustomerSelected
            };
            db.SubscriptionDays.Add(day);
            await db.SaveChangesAsync();
            var meal = new Meal
            {
                MenuId = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                Code = "M1",
                NameAr = "وجبة",
                NameEn = "Meal",
                Calories = 500,
                ProteinG = 40,
                CarbsG = 30,
                FatG = 20
            };
            db.Meals.Add(meal);
            await db.SaveChangesAsync();
            db.DailyMealSelections.Add(new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = meal.Id,
                SortOrder = 0,
                SelectedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

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
        var users = Substitute.For<IUserAccountService>();

        var handlers = new FamilyManagerHandlers(db, user, clock, pricing, users);
        return new Fixture(db, handlers, user, users, linkedUserId, provisionedUserId, group.Id, managerMember.Id, member.Id, memberSub.Id);
    }

    private sealed class Fixture(
        AppDbContext Db,
        FamilyManagerHandlers Handlers,
        ICurrentUser User,
        IUserAccountService Users,
        Guid LinkedUserId,
        Guid ProvisionedUserId,
        Guid GroupId,
        Guid ManagerMemberId,
        Guid MemberId,
        Guid MemberSubscriptionId)
    {
        public AppDbContext Db { get; } = Db;
        public FamilyManagerHandlers Handlers { get; } = Handlers;
        public IUserAccountService Users { get; } = Users;
        public Guid LinkedUserId { get; } = LinkedUserId;
        public Guid ProvisionedUserId { get; } = ProvisionedUserId;
        public Guid GroupId { get; } = GroupId;
        public Guid ManagerMemberId { get; } = ManagerMemberId;
        public Guid MemberId { get; } = MemberId;
        public Guid MemberSubscriptionId { get; } = MemberSubscriptionId;

        public void AsLinkedMember()
        {
            User.UserId.Returns(LinkedUserId);
            User.UserType.Returns(UserType.Customer);
        }
    }
}
