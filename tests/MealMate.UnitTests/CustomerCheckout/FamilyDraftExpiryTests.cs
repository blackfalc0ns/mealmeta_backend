using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyDraftExpiryTests
{
    [Fact]
    public async Task Detached_member_private_profile_and_address_book_are_not_readable_by_former_manager()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member,
            DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        group.Members.Add(member);
        db.AddRange(group, new FamilyMemberAccess { FamilyMemberId = member.Id,
            LinkedCustomerUserId = Guid.NewGuid(), HasSeparateAccount = true, DetachedFromFamilyManagement = true });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerId);
        user.UserType.Returns(UserType.Customer);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyDraftHandlers(db, user, Substitute.For<IUserAccountService>(), settings,
            Substitute.For<IFamilyPlanEligibilityService>(), Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IDateTimeProvider>(), Substitute.For<ICustomerAddressBook>());

        var allergies = await handler.Handle(new GetFamilyMemberAllergiesQuery(member.Id), default);
        var dislikes = await handler.Handle(new GetFamilyMemberDislikedFoodsQuery(member.Id), default);
        var dietary = await handler.Handle(new GetFamilyMemberDietaryQuery(member.Id), default);
        var addresses = await handler.Handle(new GetFamilyMemberAddressesQuery(member.Id), default);
        allergies.IsFailure.Should().BeTrue();
        dislikes.IsFailure.Should().BeTrue();
        dietary.IsFailure.Should().BeTrue();
        addresses.IsFailure.Should().BeTrue();
        new[] { allergies.Error.Code, dislikes.Error.Code, dietary.Error.Code, addresses.Error.Code }
            .Should().OnlyContain(x => x == "family.member_detached");
    }

    [Fact]
    public async Task Basic_member_edit_preserves_existing_nutrition_goal_and_activity()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member,
            DisplayName = "Before", RelationshipCode = "child", AgeYears = 15,
            NutritionalGoalCode = "maintain", ActivityLevel = FamilyActivityLevel.Active };
        group.Members.Add(member);
        db.Add(group);
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerId);
        user.UserType.Returns(UserType.Customer);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyDraftHandlers(db, user, Substitute.For<IUserAccountService>(), settings,
            Substitute.For<IFamilyPlanEligibilityService>(), Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IDateTimeProvider>(), Substitute.For<ICustomerAddressBook>());

        var result = await handler.Handle(new UpdateFamilyMemberCommand(member.Id,
            new FamilyMemberWriteDto("After", "child", FamilyMemberGender.Female, 16, null, Occupation: "Student")), default);

        result.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Clear();
        var stored = await db.FamilyMembers.SingleAsync();
        stored.DisplayName.Should().Be("After");
        stored.AgeYears.Should().Be(16);
        stored.Occupation.Should().Be("Student");
        stored.NutritionalGoalCode.Should().Be("maintain");
        stored.ActivityLevel.Should().Be(FamilyActivityLevel.Active);
    }

    [Fact]
    public async Task Manager_member_edit_is_denied_after_detachment_without_changing_roster_data()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member,
            DisplayName = "Before", RelationshipCode = "child", AgeYears = 15,
            NutritionalGoalCode = "maintain", ActivityLevel = FamilyActivityLevel.Active };
        group.Members.Add(member);
        db.AddRange(group, new FamilyMemberAccess { FamilyMemberId = member.Id, DetachedFromFamilyManagement = true });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerId);
        user.UserType.Returns(UserType.Customer);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyDraftHandlers(db, user, Substitute.For<IUserAccountService>(), settings,
            Substitute.For<IFamilyPlanEligibilityService>(), Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IDateTimeProvider>(), Substitute.For<ICustomerAddressBook>());

        var result = await handler.Handle(new UpdateFamilyMemberCommand(member.Id,
            new FamilyMemberWriteDto("After", "child", FamilyMemberGender.Female, 16, null, Occupation: "Student")), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.member_detached");
        db.ChangeTracker.Clear();
        var stored = await db.FamilyMembers.SingleAsync();
        stored.DisplayName.Should().Be("Before");
        stored.AgeYears.Should().Be(15);
        stored.FamilyGroupId.Should().Be(group.Id);
    }

    [Fact]
    public async Task Manager_cannot_change_phone_of_a_linked_customer_without_phone_verification()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member,
            DisplayName = "Before", RelationshipCode = "child", AgeYears = 15,
            PhoneNumber = "+96551111111", NutritionalGoalCode = "maintain", ActivityLevel = FamilyActivityLevel.Active };
        group.Members.Add(member);
        db.AddRange(group, new FamilyMemberAccess { FamilyMemberId = member.Id, HasSeparateAccount = true,
            LinkedCustomerUserId = linkedUserId });
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerId);
        user.UserType.Returns(UserType.Customer);
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(linkedUserId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            linkedUserId, null, "+96551111111", "Member", UserType.Customer, AccountStatus.Active,
            null, true, true, DateTime.UtcNow.AddDays(-1), null));
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyDraftHandlers(db, user, users, settings,
            Substitute.For<IFamilyPlanEligibilityService>(), Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IDateTimeProvider>(), Substitute.For<ICustomerAddressBook>());

        var result = await handler.Handle(new UpdateFamilyMemberCommand(member.Id,
            new FamilyMemberWriteDto("After", "child", FamilyMemberGender.Female, 16, null,
                Phone: "+96552222222")), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.member_phone_change_requires_verification");
        db.ChangeTracker.Clear();
        (await db.FamilyMembers.SingleAsync()).PhoneNumber.Should().Be("+96551111111");
    }

    [Fact]
    public async Task ExpireDueAsync_expires_quote_and_preserves_roster_history()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var group = StaleGroup(FamilyGroupStatus.Draft, now);
        db.FamilyGroups.Add(group);
        db.FamilyCheckoutBatches.Add(new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = "expired-draft", CreatedAtUtc = now.AddMinutes(-20) });
        await db.SaveChangesAsync();

        var expired = await FamilyDraftExpiry.ExpireDueAsync(db, now, CancellationToken.None);

        expired.Should().Be(1);
        (await db.FamilyCheckoutBatches.SingleAsync()).Status.Should().Be(FamilyCheckoutBatchStatus.Expired);
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExpireDueAsync_does_not_remove_checkout_pending_group()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        db.FamilyGroups.Add(StaleGroup(FamilyGroupStatus.CheckoutPending, now));
        await db.SaveChangesAsync();

        var expired = await FamilyDraftExpiry.ExpireDueAsync(db, now, CancellationToken.None);

        expired.Should().Be(0);
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveOrphanCheckoutPending_keeps_group_with_pending_payment()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var group = StaleGroup(FamilyGroupStatus.CheckoutPending, now);
        var (program, bundle) = AddCatalog(db);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = group.ManagerCustomerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 22),
            PaidAmount = new Money(50m, "KWD"),
            FamilyGroupId = group.Id,
            LastOperationAtUtc = now,
        });
        await db.SaveChangesAsync();

        var removed = await FamilyDraftExpiry.RemoveOrphanCheckoutPendingAsync(db, now, CancellationToken.None);

        removed.Should().Be(0);
        (await db.FamilyGroups.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RemoveOrphanCheckoutPending_returns_roster_to_draft_when_unpaid_siblings_are_gone()
    {
        var now = new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc);
        var db = CreateDb();
        var group = StaleGroup(FamilyGroupStatus.CheckoutPending, now);
        var (program, bundle) = AddCatalog(db);
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = group.ManagerCustomerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.Cancelled,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 22),
            PaidAmount = new Money(50m, "KWD"),
            FamilyGroupId = group.Id,
            LastOperationAtUtc = now,
        });
        await db.SaveChangesAsync();

        var removed = await FamilyDraftExpiry.RemoveOrphanCheckoutPendingAsync(db, now, CancellationToken.None);

        removed.Should().Be(1);
        (await db.FamilyGroups.SingleAsync()).Status.Should().Be(FamilyGroupStatus.Draft);
        (await db.Subscriptions.SingleAsync()).FamilyGroupId.Should().Be(group.Id);
    }

    private static FamilyGroup StaleGroup(FamilyGroupStatus status, DateTime now)
        => new()
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "عائلة",
            Status = status,
            CreatedAtUtc = now.AddMinutes(-20),
            ModifiedAtUtc = now.AddMinutes(-20),
        };

    private static (Program Program, Bundle Bundle) AddCatalog(AppDbContext db)
    {
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "B",
            NameAr = "ب",
            NameEn = "B",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.AddRange(program, bundle);
        return (program, bundle);
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
