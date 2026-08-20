using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using FamilyPricingSettings = MealMate.Application.Features.ProgramsSubscriptions.FamilyPricingSettings;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerFamilySubscriptionFlowControllerTests
{
    [Fact]
    public async Task PutGroup_CreatesDraftWithManagerMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Name.Should().Be("عائلة أحمد");
        dto.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager);
        dto.Members[0].DisplayName.Should().Be("أحمد");
        dto.Members[0].RelationshipCode.Should().Be("self");
        dto.Members[0].IsCurrentUser.Should().BeTrue();
        dto.Status.Should().Be(FamilyGroupStatus.Draft);
    }

    [Fact]
    public async Task GetRelationships_ReturnsKinshipCatalog()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId);

        var result = await controller.Relationships(CancellationToken.None);
        var items = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<IReadOnlyList<FamilyLookupDto>>().Subject;

        items.Select(x => x.Code).Should().Contain(
            ["self", "spouse", "father", "mother", "child", "sibling", "friend", "parent", "other"]);
        items.Should().ContainSingle(x => x.Code == "self").Which.ForDependents.Should().BeFalse();
        items.Should().Contain(x => x.Code == "father" && x.NameAr == "أب" && x.ForDependents);
        items.Should().Contain(x => x.Code == "child" && x.NameAr == "ابن / ابنة");
        items.Should().Contain(x => x.Code == "friend" && x.NameAr == "صديق / صديقة");
    }

    [Fact]
    public async Task PutGroup_HydratesManagerFromCustomerProfile()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = userId,
            DateOfBirth = new DateOnly(1990, 1, 15),
            DietaryPattern = "weight_loss",
        });
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var manager = dto.Members.Should().ContainSingle(m => m.IsCurrentUser).Subject;
        manager.DisplayName.Should().Be("أحمد");
        manager.AgeYears.Should().Be(36);
        manager.DeliveryAddressId.Should().Be(address.Id);
        manager.RelationshipCode.Should().Be("self");
    }

    [Fact]
    public async Task GetCurrent_WhenNoDraft_SeedsManagerFromCustomerProfile()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = userId,
            DateOfBirth = new DateOnly(1990, 1, 15),
            DietaryPattern = "balanced",
        });
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.Current(CancellationToken.None);
        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Name.Should().Be("عائلة أحمد");
        dto.Members.Should().ContainSingle();
        var manager = dto.Members[0];
        manager.IsCurrentUser.Should().BeTrue();
        manager.Role.Should().Be(FamilyMemberRole.Manager);
        manager.DisplayName.Should().Be("أحمد");
        manager.AgeYears.Should().Be(36);
        manager.DeliveryAddressId.Should().Be(address.Id);
    }

    [Fact]
    public async Task AddMember_RejectsFifthMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        created.Should().BeOfType<OkObjectResult>();

        for (var i = 0; i < 3; i++)
        {
            var add = await controller.AddMember(NewMemberWrite($"Member {i + 2}", address.Id), CancellationToken.None);
            add.Should().BeOfType<OkObjectResult>();
        }

        var fifth = await controller.AddMember(NewMemberWrite("Member 5", address.Id), CancellationToken.None);
        var conflict = fifth.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.max_members");
    }

    [Fact]
    public async Task DeleteMember_RejectsManagerWithDistinctCode()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = CreateController(db, userId);

        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        var ok = created.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = dto.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var deleted = await controller.DeleteMember(managerId, CancellationToken.None);
        var conflict = deleted.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.cannot_delete_manager");
    }

    [Fact]
    public async Task PutMemberPlan_WithAddressAndCatalogPlan_SetsSetupStatusReady()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            6,
            IncludeFriday: false), CancellationToken.None);

        var ok = planResult.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var member = dto.Members.Should().ContainSingle(m => m.MemberId == memberId).Subject;
        member.SetupStatus.Should().Be(FamilyMemberSetupStatus.Ready);
        member.ProgramId.Should().Be(program.Id);
        member.BundleId.Should().Be(bundle.Id);
        member.SelectedTier.Should().Be(RestaurantTier.Basic);
        member.DurationDays.Should().Be(6);
        member.DeliveryAddressId.Should().Be(address.Id);
    }

    [Fact]
    public async Task GetSummary_TwoReadyMembers_MatchesQuoteAndAllowsCheckout()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var settings = new FamilyPricingSettings
        {
            MaxMembers = 4,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    NameAr = "باقة عائلية",
                    NameEn = "Family package",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                },
            ],
        };
        var controller = CreateController(db, userId, users, new StubFamilyPricingSettingsReader(settings));

        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        var added = await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);
        var afterAdd = added.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var spouseId = afterAdd.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Member).Subject.MemberId;
        await controller.SetMemberPlan(spouseId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        var tracked = await db.FamilyGroups.Include(x => x.Members).SingleAsync();
        var quote = await CreateQuoteService(db, settings).QuoteAsync(tracked, CancellationToken.None);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;

        summary.FinalPrice.Should().Be(quote.FinalPrice);
        summary.Subtotal.Should().Be(quote.Subtotal);
        summary.FamilyDiscountAmount.Should().Be(quote.FamilyDiscountAmount);
        summary.CanCheckout.Should().BeTrue();
        summary.CheckoutBlockReason.Should().BeNull();
        summary.CheckoutEndpoint.Should().Be("POST /api/v1/customer/family-subscription-flow/checkout");
        summary.SelectedMemberId.Should().Be(managerId);
        summary.MealOverview.Should().HaveCount(2);
        summary.MealOverview.Should().OnlyContain(r =>
            r.Breakfast == false
            && r.Lunch == true
            && r.Snack == false
            && r.Dinner == false
            && r.MealsPerDay == 1);
    }

    [Fact]
    public async Task GetSummary_IncompleteMember_BlocksCheckout()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        SeedPricingRules(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);

        await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var current = await controller.Current(CancellationToken.None);
        var group = current.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var managerId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        await controller.UpdateMember(managerId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        await controller.SetMemberPlan(managerId, new SetFamilyMemberPlanRequest(
            program.Id, bundle.Id, RestaurantTier.Basic, 6, IncludeFriday: false), CancellationToken.None);

        await controller.AddMember(NewMemberWrite("سارة", address.Id), CancellationToken.None);

        var summaryResult = await controller.Summary(memberId: null, CancellationToken.None);
        var ok = summaryResult.Should().BeOfType<OkObjectResult>().Subject;
        var summary = ok.Value.Should().BeOfType<FamilyCheckoutSummaryDto>().Subject;
        summary.CanCheckout.Should().BeFalse();
        summary.CheckoutBlockReason.Should().Be("family.member_incomplete");
    }

    [Fact]
    public async Task PutMemberPlan_DurationThreeDays_ReturnsFamilyDurationNotAvailable()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            3,
            IncludeFriday: false), CancellationToken.None);

        var badRequest = planResult.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.duration_not_available");
    }

    [Fact]
    public async Task PutMemberPlan_WhenProgramNotAvailableForFamily_ReturnsCatalogNotAvailable()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var (program, bundle) = await SeedProgramBundleOfferAsync(db);
        program.AvailableForFamily = false;
        await db.SaveChangesAsync();

        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var group = created.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        var memberId = group.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager).Subject.MemberId;

        var updated = await controller.UpdateMember(memberId, NewMemberWrite("أحمد", address.Id) with
        {
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
        }, CancellationToken.None);
        updated.Should().BeOfType<OkObjectResult>();

        var planResult = await controller.SetMemberPlan(memberId, new SetFamilyMemberPlanRequest(
            program.Id,
            bundle.Id,
            RestaurantTier.Basic,
            6,
            IncludeFriday: false), CancellationToken.None);

        var badRequest = planResult.Should().BeOfType<ObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.catalog_not_available");
    }

    private static async Task<(MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle)> SeedProgramBundleOfferAsync(AppDbContext db)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_WEIGHT_LOSS",
            ShortCode = "WL",
            NameAr = "خسارة الوزن",
            NameEn = "Weight Loss",
            DescriptionAr = "وجبات محسوبة السعرات.",
            DescriptionEn = "Calorie-controlled meals.",
            KeywordsAr = ["سعرات محسوبة", "نتائج مستدامة"],
            KeywordsEn = ["Calorie controlled", "Sustainable results"],
            ImageStorageKey = "uploads/admin/programs/wl.webp",
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE",
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
            AvailableForFamily = true,
        };
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            IsActive = true,
        });
        // Locked pricing so GetCustomerSubscriptionPricingCatalogQuery yields Basic for the single restaurant.
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(41.6m, "KWD"),
            LockStatus = PriceLockStatus.Locked,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();
        return (program, bundle);
    }

    private static void SeedPricingRules(AppDbContext db)
    {
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromFraction(.3m),
            RMin = Percentage.FromFraction(.05m),
            DShort = 6,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = .1m,
            IsActive = true,
        });
        db.SaveChanges();
    }

    private static IFamilyQuoteService CreateQuoteService(AppDbContext db, FamilyPricingSettings settings)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var pricingHandlers = new PricingAndClassificationHandlers(
            db, Substitute.For<IAuditLogger>(), clock, tiers);
        var catalogSender = new CatalogQuerySender(pricingHandlers);
        return new FamilyQuoteService(
            catalogSender,
            new StubFamilyPricingSettingsReader(settings),
            new SubscriptionScheduleService(clock));
    }

    private static FamilyMemberWriteDto NewMemberWrite(string name, Guid addressId) => new(
        name,
        "spouse",
        FamilyMemberGender.Female,
        30,
        addressId);

    private static CustomerFamilySubscriptionFlowController CreateController(
        AppDbContext db,
        Guid userId,
        IUserAccountService? users = null,
        IFamilyPricingSettingsReader? settings = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        users ??= Substitute.For<IUserAccountService>();
        var pricingSettings = settings ?? new StubFamilyPricingSettingsReader(new FamilyPricingSettings
        {
            MaxMembers = 4,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
            Packages =
            [
                new FamilySizePackageSettings
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    NameAr = "باقة عائلية",
                    NameEn = "Family package",
                    MinMembers = 2,
                    MaxMembers = 4,
                    DiscountPercent = 0.10m,
                    IsActive = true,
                    SortOrder = 0,
                },
            ],
        });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var pricingHandlers = new PricingAndClassificationHandlers(
            db, Substitute.For<IAuditLogger>(), clock, tiers);
        var catalogSender = new CatalogQuerySender(pricingHandlers);
        var eligibility = new FamilyPlanEligibilityService(
            db,
            new AllowedSubscriptionDurationService(db),
            tiers,
            catalogSender);
        var quote = new FamilyQuoteService(
            catalogSender,
            pricingSettings,
            new SubscriptionScheduleService(clock));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 19));
        var handlers = new FamilyDraftHandlers(db, currentUser, users, pricingSettings, eligibility, quote, clock);
        var sender = new DirectFamilyDraftSender(handlers, db);

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        messages.GetMessage(Arg.Any<string>(), arguments: Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
        var localizer = new ProblemDetailsLocalizer(messages);

        var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .AddSingleton(localizer)
            .BuildServiceProvider();

        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        return new CustomerFamilySubscriptionFlowController(
            currentUser,
            db,
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<IFamilyCheckoutService>(),
            configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed class StubFamilyPricingSettingsReader(FamilyPricingSettings settings) : IFamilyPricingSettingsReader
    {
        public Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(settings);
    }

    private sealed class CatalogQuerySender(PricingAndClassificationHandlers pricingHandlers) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetCustomerSubscriptionPricingCatalogQuery q)
                return (TResponse)(object)await pricingHandlers.Handle(q, cancellationToken);
            if (request is PreviewCustomerPricingQuery preview)
                return (TResponse)(object)await pricingHandlers.Handle(preview, cancellationToken);
            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Dispatches family draft requests to handlers and persists successful commands (test stand-in for MediatR + UnitOfWork).</summary>
    private sealed class DirectFamilyDraftSender(FamilyDraftHandlers handlers, AppDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetFamilyDraftMetadataQuery q => await handlers.Handle(q, cancellationToken),
                GetFamilyRelationshipsQuery q => await handlers.Handle(q, cancellationToken),
                GetFamilySizePackagesQuery q => await handlers.Handle(q, cancellationToken),
                GetCurrentFamilyGroupQuery q => await handlers.Handle(q, cancellationToken),
                UpsertFamilyGroupCommand c => await handlers.Handle(c, cancellationToken),
                AddFamilyDraftMemberCommand c => await handlers.Handle(c, cancellationToken),
                UpdateFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                DeleteFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                SetFamilyMemberPlanCommand c => await handlers.Handle(c, cancellationToken),
                GetFamilyCheckoutSummaryQuery q => await handlers.Handle(q, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };

            if (request is IBaseCommand && result is Result { IsSuccess: true })
            {
                await db.SaveChangesAsync(cancellationToken);
                // Mimic request-scoped DbContext: detach after commit so the next call reloads cleanly.
                db.ChangeTracker.Clear();
            }

            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
