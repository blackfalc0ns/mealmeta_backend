using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class ConvertIndividualToFamilyHandlerTests
{
    [Fact]
    public async Task Convert_ActiveIndividual_CreatesDraftFamily_LinksSameSubscription()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
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
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            IncludeFriday = true,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(150m, "KWD"),
        };
        db.AddRange(program, bundle, sub);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                customerId, "c@example.com", "50000001", "أحمد", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));

        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 24));

        var sut = new ConvertIndividualToFamilyHandler(db, currentUser, users, settings, clock);
        var result = await sut.Handle(new ConvertIndividualToFamilyCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(FamilyGroupStatus.Draft);
        result.Value.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager);

        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        group.ConvertedFromSubscriptionId.Should().Be(sub.Id);
        group.Members.Single().MemberSubscriptionId.Should().Be(sub.Id);
        group.Members.Single().ProgramId.Should().Be(program.Id);
        group.Members.Single().SelectedTier.Should().Be(RestaurantTier.Platinum);

        var linked = await db.Subscriptions.SingleAsync(x => x.Id == sub.Id);
        linked.FamilyGroupId.Should().Be(group.Id);
        linked.FamilyMemberId.Should().Be(group.Members.Single().Id);
        linked.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Convert_WhenUnconvertedDraftExists_AdoptsDraftAndLinksSameSubscription()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
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
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            IncludeFriday = true,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(150m, "KWD"),
        };
        var leftover = new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "مسودة قديمة",
            Status = FamilyGroupStatus.Draft,
        };
        leftover.Members.Add(new FamilyMember
        {
            FamilyGroupId = leftover.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 30,
            NutritionalGoalCode = string.Empty,
            ActivityLevel = FamilyActivityLevel.Sedentary,
        });
        db.AddRange(program, bundle, sub, leftover);
        await db.SaveChangesAsync();
        var leftoverId = leftover.Id;

        var sut = CreateHandler(db, customerId, "أحمد");
        var result = await sut.Handle(new ConvertIndividualToFamilyCommand("عائلتي"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FamilyGroupId.Should().Be(leftoverId);
        result.Value.Name.Should().Be("عائلتي");
        (await db.FamilyGroups.CountAsync()).Should().Be(1);

        var group = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        group.Id.Should().Be(leftoverId);
        group.ConvertedFromSubscriptionId.Should().Be(sub.Id);
        var manager = group.Members.Single(m => m.Role == FamilyMemberRole.Manager);
        manager.MemberSubscriptionId.Should().Be(sub.Id);
        manager.ProgramId.Should().Be(program.Id);
        manager.BundleId.Should().Be(bundle.Id);
        manager.SelectedTier.Should().Be(RestaurantTier.Platinum);
        manager.DurationDays.Should().Be(26);
        manager.IncludeFriday.Should().BeTrue();

        var linked = await db.Subscriptions.SingleAsync(x => x.Id == sub.Id);
        linked.FamilyGroupId.Should().Be(leftoverId);
        linked.FamilyMemberId.Should().Be(manager.Id);
    }

    [Fact]
    public async Task Convert_WhenActiveFamilyExists_ReturnsAlreadyInFamily()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        }, new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة نشطة",
            Status = FamilyGroupStatus.Active,
        });
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, customerId).Handle(new ConvertIndividualToFamilyCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conversion.already_in_family");
    }

    [Fact]
    public async Task Convert_WhenUnconvertedCheckoutPending_ReturnsAlreadyInFamily()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        }, new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "دفع جارٍ",
            Status = FamilyGroupStatus.CheckoutPending,
        });
        await db.SaveChangesAsync();

        var result = await CreateHandler(db, customerId).Handle(new ConvertIndividualToFamilyCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conversion.already_in_family");
    }

    [Fact]
    public async Task GetCurrent_WithConvertibleIndividual_DoesNotSeedDraft_ReturnsConvertRequired()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var handlers = new FamilyDraftHandlers(
            db,
            currentUser,
            Substitute.For<IUserAccountService>(),
            settings,
            Substitute.For<IFamilyPlanEligibilityService>(),
            Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IDateTimeProvider>());

        var result = await handlers.Handle(new GetCurrentFamilyGroupQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conversion.convert_required");
        (await db.FamilyGroups.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ConversionOptions_PendingPayment_MarksConvertUnavailable()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
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
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        });
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new ConvertIndividualToFamilyHandler(
            db,
            currentUser,
            Substitute.For<IUserAccountService>(),
            Substitute.For<IFamilyPricingSettingsReader>(),
            Substitute.For<IDateTimeProvider>());

        var options = await sut.Handle(new GetSubscriptionConversionOptionsQuery(), CancellationToken.None);
        options.IsSuccess.Should().BeTrue();
        var convert = options.Value.Options.Single(x => x.Code == "CONVERT_TO_FAMILY_MANAGER");
        convert.Available.Should().BeFalse();
        convert.DisabledReason.Should().Be("conversion.individual_not_convertible");
        options.Value.Options.Single(x => x.Code == "CHANGE_INDIVIDUAL_PLAN").Available.Should().BeFalse();
    }

    [Fact]
    public async Task Convert_PreservesLocked72hSubscriptionDays()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = new Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        };
        var lockedDay = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = sub.Id,
            ServiceDate = new DateOnly(2026, 8, 25),
            Status = MealMate.Domain.Enums.SubscriptionDayStatus.Locked72h,
            SelectionDeadlineUtc = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc),
            LockAtUtc = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc),
        };
        db.AddRange(program, bundle, sub, lockedDay);
        await db.SaveChangesAsync();
        var dayId = lockedDay.Id;
        var lockAt = lockedDay.LockAtUtc;

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                customerId, "c@example.com", "50000001", "أحمد", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new ConvertIndividualToFamilyHandler(
            db, currentUser, users, settings, Substitute.For<IDateTimeProvider>());
        var result = await sut.Handle(new ConvertIndividualToFamilyCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        var day = await db.SubscriptionDays.SingleAsync(x => x.Id == dayId);
        day.Status.Should().Be(MealMate.Domain.Enums.SubscriptionDayStatus.Locked72h);
        day.LockAtUtc.Should().Be(lockAt);
        day.SubscriptionId.Should().Be(sub.Id);
    }

    private static ConvertIndividualToFamilyHandler CreateHandler(AppDbContext db, Guid customerId, string fullName = "أحمد")
    {
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                customerId, "c@example.com", "50000001", fullName, UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(customerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 24));
        return new ConvertIndividualToFamilyHandler(db, currentUser, users, settings, clock);
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
