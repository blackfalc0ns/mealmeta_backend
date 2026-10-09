using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Text.Json;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerSubscriptionFlowControllerTests
{
    [Fact]
    public async Task Types_ReturnsAvailableTypes_WhenCustomerHasNoSubscriptions()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        var result = await controller.Types(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsAssignableFrom<IReadOnlyList<SubscriptionTypeOptionDto>>(ok.Value);
        Assert.Equal(2, types.Count);
        Assert.Contains(types, x => x.Code == "INDIVIDUAL" && x.IsAvailable);
        Assert.Contains(types, x => x.Code == "FAMILY" && x.IsAvailable);
    }

    [Fact]
    public async Task Types_ReturnsBlockedIndividual_WhenCustomerHasActiveSubscription()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = customerId,
            Status = SubscriptionStatus.Active,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            PaidAmount = new Money(50, "KWD")
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(customerId);

        var result = await controller.Types(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsAssignableFrom<IReadOnlyList<SubscriptionTypeOptionDto>>(ok.Value);
        var ind = types.Single(x => x.Code == "INDIVIDUAL");
        Assert.False(ind.IsAvailable);
        Assert.Equal(CustomerIndividualSubscriptionGate.ExistingSubscriptionError, ind.DisabledReason);
    }

    [Fact]
    public async Task Types_ReturnsBlockedIndividual_WhenCustomerHasDraftFamilyGroup()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        db.FamilyGroups.Add(new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "Family Test",
            Status = FamilyGroupStatus.Draft
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(customerId);

        var result = await controller.Types(CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsAssignableFrom<IReadOnlyList<SubscriptionTypeOptionDto>>(ok.Value);
        var ind = types.Single(x => x.Code == "INDIVIDUAL");
        Assert.False(ind.IsAvailable);
    }

    [Fact]
    public async Task Programs_ReturnsDescriptionsAndKeywordsWithoutNutritionRanges()
    {
        await using var db = CreateDb();
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
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var unlinkedProgram = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_UNLINKED",
            NameAr = "برنامج بدون مطعم",
            NameEn = "Unlinked Program",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var unlinkedBundle = new Bundle
        {
            ProgramId = unlinkedProgram.Id,
            Code = "UNLINKED_BUNDLE",
            NameAr = "باقة غير مرتبطة",
            NameEn = "Unlinked Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.AddRange(program, bundle, restaurant, unlinkedProgram, unlinkedBundle);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None));
        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerProgramCardDto>>(result.Value));

        Assert.Equal("وجبات محسوبة السعرات.", item.DescriptionAr);
        Assert.Equal(["سعرات محسوبة", "نتائج مستدامة"], item.KeywordsAr);
        Assert.Equal(program.Id, item.ProgramId);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Fadmin%2Fprograms%2Fwl.webp", item.ImageUrl);
        Assert.Null(typeof(CustomerProgramCardDto).GetProperty("NutritionStandards"));
        Assert.DoesNotContain(typeof(CustomerProgramCardDto).GetProperties(), x => x.Name.Contains("Min") || x.Name.Contains("Max"));
    }

    [Fact]
    public async Task Bundles_ReturnsBilingualSlotsAndOnlyRestaurantPhotosFromActiveOffers()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_WEIGHT_LOSS",
            NameAr = "خسارة الوزن",
            NameEn = "Weight Loss",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "PKG-BL1S",
            NameAr = "باقة 3 وجبات",
            NameEn = "3 Meals Bundle",
            DescriptionAr = "فطور وغداء وسناك",
            DescriptionEn = "Breakfast, lunch and snack",
            ImageStorageKey = "uploads/admin/bundles/bl1s.webp",
            MealsPerDay = 3,
            BreakfastCount = 1,
            LunchCount = 1,
            SnackCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var restaurantWithPhoto = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم بصورة",
            TradeNameEn = "Restaurant With Photo",
            LegalNameAr = "مطعم بصورة",
            LegalNameEn = "Restaurant With Photo",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var restaurantWithoutPhoto = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم بدون صورة",
            TradeNameEn = "Restaurant Without Photo",
            LegalNameAr = "مطعم بدون صورة",
            LegalNameEn = "Restaurant Without Photo",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var stoppedRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم متوقف",
            TradeNameEn = "Stopped Restaurant",
            LegalNameAr = "مطعم متوقف",
            LegalNameEn = "Stopped Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = false,
        };
        db.AddRange(program, bundle, restaurantWithPhoto, restaurantWithoutPhoto, stoppedRestaurant);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurantWithPhoto.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurantWithoutPhoto.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = stoppedRestaurant.Id, IsActive = true });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurantWithPhoto.Id,
            BranchCode = "MAIN",
            NameAr = "الرئيسي",
            NameEn = "Main",
            AddressAr = "الكويت",
            AddressEn = "Kuwait",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = "50000000",
            IsActive = true,
            IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/main.webp",
        });
        db.Subscriptions.AddRange(
            new Subscription
            {
                CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                SelectedTier = RestaurantTier.Basic, DurationDays = 6,
                StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionStatus.Active, PaidAmount = new Money(40, "KWD"),
            },
            new Subscription
            {
                CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                SelectedTier = RestaurantTier.Basic, DurationDays = 6,
                StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionStatus.PendingPayment, PaidAmount = Money.Zero("KWD"),
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, restaurantWithPhoto.Id, restaurantWithoutPhoto.Id);
        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var payload = Assert.IsType<CustomerProgramBundlesDto>(result.Value);
        Assert.True(payload.HasDeliveryAddress);
        Assert.True(payload.HasAllergies);
        Assert.True(payload.HasDietaryPreferences);
        Assert.True(payload.IsProfileComplete);
        var item = Assert.Single(payload.Bundles);

        Assert.Equal("فطور وغداء وسناك", item.DescriptionAr);
        Assert.Equal("Breakfast, lunch and snack", item.DescriptionEn);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Fadmin%2Fbundles%2Fbl1s.webp", item.ImageUrl);
        Assert.Collection(item.Slots,
            x => { Assert.Equal("breakfast", x.Code); Assert.Equal("الإفطار", x.NameAr); Assert.Equal(1, x.Quantity); },
            x => { Assert.Equal("lunch", x.Code); Assert.Equal("الغداء", x.NameAr); Assert.Equal(1, x.Quantity); },
            x => { Assert.Equal("snack", x.Code); Assert.Equal("سناك", x.NameAr); Assert.Equal(1, x.Quantity); });
        Assert.Equal(2, item.RestaurantCount);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fmain.webp",
            item.Restaurants.Single(x => x.RestaurantId == restaurantWithPhoto.Id).LogoUrl);
        Assert.Null(item.Restaurants.Single(x => x.RestaurantId == restaurantWithoutPhoto.Id).LogoUrl);
        Assert.DoesNotContain(item.Restaurants, x => x.RestaurantId == stoppedRestaurant.Id);
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("ImageUrl"));
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("NameEn"));
        Assert.Equal(1, item.SelectionCount);
        Assert.True(item.IsMostSelected);
        Assert.Equal("MOST_SELECTED", item.Badge?.Code);
        Assert.Equal("الأكثر اختيارًا", item.Badge?.NameAr);
    }

    [Fact]
    public async Task Bundles_WithoutAddress_ReturnsPayloadWithFlagsFalse()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_ADDR", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "BND_ADDR", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var payload = Assert.IsType<CustomerProgramBundlesDto>(result.Value);
        Assert.False(payload.HasDeliveryAddress);
        Assert.False(payload.HasAllergies);
        Assert.False(payload.HasDietaryPreferences);
        Assert.False(payload.IsProfileComplete);
    }

    [Fact]
    public async Task Prerequisites_WithoutAddress_ReturnsAddressRequiredConflict()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out _);
        var result = await controller.Prerequisites(CancellationToken.None);
        AssertConflict(result, "checkout.address_required", "ADDRESS", "POST /api/v1/customers/me/addresses");
    }

    [Fact]
    public async Task Prerequisites_WithoutAllergies_ReturnsAllergiesRequiredConflict()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == currentUser.UserId!.Value);
        profile.AllergiesCompletedAtUtc = null;
        await db.SaveChangesAsync();

        var result = await controller.Prerequisites(CancellationToken.None);
        AssertConflict(result, "checkout.allergies_required", "ALLERGIES", "GET /api/v1/customers/me/food-preferences/allergies");
    }

    [Fact]
    public async Task Prerequisites_WithoutDietary_ReturnsDietaryRequiredConflict()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == currentUser.UserId!.Value);
        profile.DietaryPreferencesCompletedAtUtc = null;
        await db.SaveChangesAsync();

        var result = await controller.Prerequisites(CancellationToken.None);
        AssertConflict(result, "checkout.dietary_preferences_required", "DIETARY_PREFERENCES", "GET /api/v1/customers/me/food-preferences/dietary");
    }

    [Fact]
    public async Task Prerequisites_WithIncompleteFamily_ReturnsFamilyIncompleteNotMemberRequired()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        var managerAddressId = await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        await SeedFamilyGroupWithSpouseAsync(db, currentUser.UserId!.Value, managerAddressId);

        var result = await controller.Prerequisites(CancellationToken.None);
        AssertConflict(
            result,
            "family.incomplete",
            "FAMILY",
            "GET /api/v1/customer/subscriptions/uncompleted");
        Assert.False(IsConflictWithError(result, "family.member_required"));
    }

    [Fact]
    public async Task Prerequisites_WithCheckoutPendingFamilyWithoutLiveHold_AllowsIndividualFlow()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        var managerAddressId = await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        await SeedFamilyGroupWithSpouseAsync(db, currentUser.UserId!.Value, managerAddressId);
        var group = await db.FamilyGroups.SingleAsync(x => x.ManagerCustomerUserId == currentUser.UserId!.Value);
        group.Status = FamilyGroupStatus.CheckoutPending;
        await db.SaveChangesAsync();

        var result = await controller.Prerequisites(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Tiers_ButtonToClassification_WithoutPrerequisites_ReturnsConflict()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_TIERS_GATE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "BND_TIERS_GATE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        // 1. Without Address -> Conflict checkout.address_required
        var resultWithoutAddress = await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None);
        AssertConflict(resultWithoutAddress, "checkout.address_required", "ADDRESS", "POST /api/v1/customers/me/addresses");

        // 2. With Address but without Allergies -> Conflict checkout.allergies_required
        await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == currentUser.UserId!.Value);
        profile.AllergiesCompletedAtUtc = null;
        await db.SaveChangesAsync();

        var resultWithoutAllergies = await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None);
        AssertConflict(resultWithoutAllergies, "checkout.allergies_required", "ALLERGIES", "GET /api/v1/customers/me/food-preferences/allergies");

        // 3. With Allergies but without Dietary -> Conflict checkout.dietary_preferences_required
        profile.AllergiesCompletedAtUtc = DateTime.UtcNow;
        profile.DietaryPreferencesCompletedAtUtc = null;
        await db.SaveChangesAsync();

        var resultWithoutDietary = await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None);
        AssertConflict(resultWithoutDietary, "checkout.dietary_preferences_required", "DIETARY_PREFERENCES", "GET /api/v1/customers/me/food-preferences/dietary");
    }

    [Fact]
    public async Task Tiers_FamilyMemberWithoutOwnAddress_DoesNotUseManagerAddress()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FAMILY_TIERS_GATE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "BND_FAMILY_TIERS_GATE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>(
                Array.Empty<CustomerSubscriptionCatalogItemDto>())));
        var controller = CreateController(db, out _, out var currentUser, sender);
        var managerAddressId = await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var (managerMemberId, spouseMemberId) = await SeedFamilyGroupWithSpouseAsync(
            db, currentUser.UserId!.Value, managerAddressId);

        var omittedMemberId = await controller.Tiers(
            program.Id, bundle.Id, null, false, CancellationToken.None);
        Assert.False(IsConflictWithError(omittedMemberId, "family.member_required"));
        Assert.False(IsConflictWithError(omittedMemberId, "family.address_required"));
        Assert.False(IsConflictWithError(omittedMemberId, "checkout.address_required"));

        var spouseWithoutAddress = await controller.Tiers(
            program.Id, bundle.Id, null, false, CancellationToken.None, spouseMemberId);
        AssertConflict(
            spouseWithoutAddress,
            "family.address_required",
            "ADDRESS",
            $"POST /api/v1/customer/family-subscription-flow/members/{spouseMemberId}/addresses");

        var managerWithAddress = await controller.Tiers(
            program.Id, bundle.Id, null, false, CancellationToken.None, managerMemberId);
        Assert.False(
            IsConflictWithError(managerWithAddress, "family.address_required"),
            "Manager with a delivery address must pass the family classification address gate.");
        Assert.False(
            IsConflictWithError(managerWithAddress, "checkout.address_required"),
            "Family classification must not fall back to the individual address gate for a member who already has an address.");
        Assert.False(
            IsConflictWithError(managerWithAddress, "checkout.dietary_preferences_required"),
            "Family classification must not require dietary preferences.");
    }

    [Fact]
    public async Task TierRestaurants_FamilyMemberWithoutOwnAddress_DoesNotUseManagerAddress()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FAMILY_REST_GATE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "BND_FAMILY_REST_GATE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        var managerAddressId = await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var (_, spouseMemberId) = await SeedFamilyGroupWithSpouseAsync(
            db, currentUser.UserId!.Value, managerAddressId);

        var spouseWithoutAddress = await controller.TierRestaurants(
            program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None, spouseMemberId);
        AssertConflict(
            spouseWithoutAddress,
            "family.address_required",
            "ADDRESS",
            $"POST /api/v1/customer/family-subscription-flow/members/{spouseMemberId}/addresses");
    }

    [Fact]
    public async Task Bundles_FiltersRestaurantsByDeliveryRegion()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_REGION", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "BND_REGION", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var coveredRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم مغطى", TradeNameEn = "Covered Restaurant",
            LegalNameAr = "مطعم مغطى", LegalNameEn = "Covered Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var uncoveredRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم غير مغطى", TradeNameEn = "Uncovered Restaurant",
            LegalNameAr = "مطعم غير مغطى", LegalNameEn = "Uncovered Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, coveredRestaurant, uncoveredRestaurant);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = coveredRestaurant.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = uncoveredRestaurant.Id, IsActive = true });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, coveredRestaurant.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var payload = Assert.IsType<CustomerProgramBundlesDto>(result.Value);
        Assert.True(payload.HasDeliveryAddress);
        Assert.True(payload.HasAllergies);
        Assert.True(payload.HasDietaryPreferences);
        Assert.True(payload.IsProfileComplete);

        var item = Assert.Single(payload.Bundles);
        Assert.Equal(1, item.RestaurantCount);
        Assert.Single(item.Restaurants, x => x.RestaurantId == coveredRestaurant.Id);
        Assert.DoesNotContain(item.Restaurants, x => x.RestaurantId == uncoveredRestaurant.Id);
    }

    [Fact]
    public async Task Types_ReturnsIndividualAndFamilyForCustomer()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out _);

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();

        Assert.Equal(["INDIVIDUAL", "FAMILY"], items.Select(x => x.Code));
        Assert.True(items.Single(x => x.Code == "INDIVIDUAL").IsAvailable);
        Assert.False(items.Single(x => x.Code == "FAMILY").RequiresConversion);
    }

    [Fact]
    public async Task Types_FamilyIsConfiguredBeforePayment()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out _);

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();

        Assert.False(items.Single(x => x.Code == "FAMILY").MembersConfiguredAfterPayment);
    }

    [Fact]
    public async Task Types_WithActiveIndividual_DisablesIndividual_AndMarksFamilyRequiresConversion()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        var customerId = currentUser.UserId!.Value;
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
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
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();
        var individual = items.Single(x => x.Code == "INDIVIDUAL");
        var family = items.Single(x => x.Code == "FAMILY");

        Assert.False(individual.IsAvailable);
        Assert.Equal(CustomerIndividualSubscriptionGate.ExistingSubscriptionError, individual.DisabledReason);
        Assert.True(family.IsAvailable);
        Assert.True(family.RequiresConversion);
        Assert.Equal("/api/v1/customer/subscriptions/current/conversion-options", family.ConversionEndpoint);
    }

    [Fact]
    public async Task Types_And_Prerequisites_WithArchivedFamilySubscription_AllowsIndividualFlow()
    {
        await using var db = CreateDb();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>([])));
        var controller = CreateController(db, out _, out var currentUser, sender);
        var managerAddressId = await SeedCustomerDeliveryCoverageAsync(db, currentUser);
        var customerId = currentUser.UserId!.Value;

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_ARCH", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_ARCH", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = customerId,
            Name = "عائلة سابقة",
            Status = FamilyGroupStatus.Active,
        };
        var managerMember = new FamilyMember
        {
            Role = FamilyMemberRole.Manager,
            DisplayName = "الأب",
            RelationshipCode = "self",
            NutritionalGoalCode = string.Empty,
            Gender = FamilyMemberGender.Male,
            AgeYears = 40,
            DeliveryAddressId = managerAddressId,
        };
        var childMember = new FamilyMember
        {
            Role = FamilyMemberRole.Member,
            DisplayName = "الابن",
            RelationshipCode = "child",
            NutritionalGoalCode = string.Empty,
            Gender = FamilyMemberGender.Male,
            AgeYears = 12,
            DeliveryAddressId = managerAddressId,
        };
        group.Members.Add(managerMember);
        group.Members.Add(childMember);
        db.AddRange(program, bundle, group);
        await db.SaveChangesAsync();

        db.Subscriptions.AddRange(
            new Subscription
            {
                CustomerUserId = customerId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                FamilyGroupId = group.Id,
                FamilyMemberId = managerMember.Id,
                SelectedTier = RestaurantTier.Basic,
                DurationDays = 26,
                Status = SubscriptionStatus.Completed,
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 26),
                PaidAmount = new Money(100m, "KWD"),
            },
            new Subscription
            {
                CustomerUserId = customerId,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                FamilyGroupId = group.Id,
                FamilyMemberId = childMember.Id,
                SelectedTier = RestaurantTier.Basic,
                DurationDays = 26,
                Status = SubscriptionStatus.Completed,
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 26),
                PaidAmount = new Money(100m, "KWD"),
            }
        );
        await db.SaveChangesAsync();

        var typesResult = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var types = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(typesResult.Value).ToList();
        var individual = types.Single(x => x.Code == "INDIVIDUAL");
        Assert.True(individual.IsAvailable);
        Assert.Null(individual.DisabledReason);

        var prereqResult = await controller.Prerequisites(CancellationToken.None);
        Assert.IsType<OkObjectResult>(prereqResult);

        var tiersResult = await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None);
        Assert.False(IsConflictWithError(tiersResult, "family.member_required"));
        Assert.False(IsConflictWithError(tiersResult, "family.incomplete"));
    }

    [Fact]
    public async Task PaymentMethods_ReturnsOnlyConfiguredHostedMethodsAndWalletState()
    {
        await using var db = CreateDb();
        db.PayzahGatewayConfigs.Add(new PayzahGatewayConfig(true, "https://development.payzah.net/ws/paymentgateway", "token", maskedKey: "tok***"));
        await db.SaveChangesAsync();
        var controller = CreateController(db, out var payment, out _);
        payment.GetPaymentMethodsAsync(41.6m, Arg.Any<CancellationToken>()).Returns([
            new PaymentGatewayMethod(3, "PAYZAH", "الدفع عبر Payzah", "Payzah hosted checkout", null, null, "KWD", false)
        ]);

        var result = Assert.IsType<OkObjectResult>(await controller.PaymentMethods(41.6m, CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<CustomerPaymentMethodDto>>(result.Value).ToList();

        Assert.Contains(items, x => x.PaymentMethodId == 3 && x.Code == "PAYZAH" && x.IsAvailable && x.ServiceCharge == null && x.TotalAmount == null);
        Assert.DoesNotContain(items, x => x.PaymentMethodId == 2);
        Assert.DoesNotContain(items, x => x.PaymentMethodId == 8);
        Assert.Contains(items, x => x.Code == "MEALMATE_WALLET" && !x.IsAvailable && x.UnavailableReason == "wallet_not_implemented");
    }

    [Fact]
    public async Task DurationsAndTiers_ReturnSeparateMobileContracts()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FLOW", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_FLOW", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var run = new ClassificationRun
        {
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow,
        };
        var platinumRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم ٢", TradeNameEn = "Restaurant 2", LegalNameAr = "مطعم ٢", LegalNameEn = "Restaurant 2",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var eliteRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم ٣", TradeNameEn = "Restaurant 3", LegalNameAr = "مطعم ٣", LegalNameEn = "Restaurant 3",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant, platinumRestaurant, eliteRestaurant, run);
        // Daily reference prices 2 / 3 / 4 KWD classify the three restaurants as Basic / Platinum / Elite.
        foreach (var (owner, reference26) in new[] { (restaurant, 52m), (platinumRestaurant, 78m), (eliteRestaurant, 104m) })
        {
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = owner.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = owner.Id,
                ReferencePrice26 = new Money(reference26, "KWD"),
                ContractedSupplyPrice26 = new Money(reference26 * 0.8m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        }
        db.RestaurantOperatingSchedules.Add(new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
        {
            RestaurantId = restaurant.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = true,
        });
        db.RestaurantClassifications.Add(new RestaurantClassification
        {
            ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id,
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            Tier = RestaurantTier.Basic,
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/tier.webp",
        });
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 9, 20), PaidAmount = new Money(260, "KWD"),
        });
        var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
        pricingSettings.TierLabels.BasicLogoStorageKey = "uploads/admin/tiers/basic.webp";
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select",
                [new(6, 66, 11), new(12, 120, 10), new(26, 234, 9)]),
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Platinum, "بلس", "Plus",
                [new(6, 78, 13), new(12, 144, 12), new(26, 286, 11)]),
        ];
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(catalog));
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                var dailyPrice = query.Tier == RestaurantTier.Basic ? 9m : 11m;
                return Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    dailyPrice, query.DurationDays, DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays, FridayRatePct: 0m)));
            });
        var clock = Substitute.For<IDateTimeProvider>();
        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(
            db, currentUser, restaurant.Id, platinumRestaurant.Id, eliteRestaurant.Id);

        var durationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, false, CancellationToken.None));
        var durations = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsResult.Value).ToList();
        Assert.Equal(new int?[] { 6, 12, 26, null }, durations.Select(x => x.DurationDays));
        Assert.True(durations.Single(x => x.DurationDays == 26).IsPopular);
        var presetDurations = durations.Where(x => !x.IsCustom).ToList();
        Assert.All(presetDurations, x =>
        {
            Assert.True(x.SupportsFriday);
            Assert.False(x.IncludeFriday);
            Assert.Equal(0, x.FridayDays);
            Assert.Equal(x.DurationDays, x.TotalServiceDays);
            Assert.Equal("INCLUDE_FRIDAY", x.FridayOption.Code);
        });
        var customDuration = Assert.Single(durations, x => x.IsCustom);
        Assert.Null(customDuration.DurationDays);
        Assert.Equal(7, customDuration.MinDays);
        Assert.Equal(25, customDuration.MaxDays);
        Assert.True(customDuration.SupportsFriday);
        Assert.Null(customDuration.FridayDays);
        Assert.Null(customDuration.TotalServiceDays);

        var tiersResult = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(tiersResult.Value).ToList();
        Assert.Equal(2, tiers.Count);
        Assert.True(tiers.Single(x => x.Tier == RestaurantTier.Basic).IsRecommended);
        Assert.Equal(52, tiers.Single(x => x.Tier == RestaurantTier.Platinum).PriceDifference);
        Assert.Contains("basic.webp", tiers.Single(x => x.Tier == RestaurantTier.Basic).LogoUrl);
        Assert.Null(tiers.Single(x => x.Tier == RestaurantTier.Platinum).LogoUrl);
        var preview = Assert.Single(tiers.Single(x => x.Tier == RestaurantTier.Basic).Restaurants);
        Assert.Equal(restaurant.Id, preview.RestaurantId);
        Assert.Contains("tier.webp", preview.LogoUrl);

        var cardResult = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseCard(program.Id, bundle.Id, restaurant.Id, CancellationToken.None));
        var card = Assert.IsType<CustomerRestaurantBrowseCardDto>(cardResult.Value);
        Assert.Equal(restaurant.Id, card.RestaurantId);
        Assert.Equal("مطعم", card.NameAr);
        Assert.Equal("Restaurant", card.NameEn);
        Assert.Contains("tier.webp", card.LogoUrl);
        Assert.Null(card.Rating);
        Assert.Equal(0, card.MealCount);
        Assert.Equal(0, card.BundleMealCount);
        Assert.Equal(RestaurantTier.Basic, card.Tier);
        Assert.Equal("أساسي", card.TierNameAr);
        Assert.Contains("basic.webp", card.TierLogoUrl);
        var classification = Assert.Single(card.Classifications);
        Assert.Contains("basic.webp", classification.LogoUrl);
        Assert.Equal(RestaurantTier.Basic, classification.Tier);
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameEn"));

        var allBasicResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None));
        var allBasic = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(allBasicResult.Value).ToList();
        var listedBasic = Assert.Single(allBasic);
        Assert.Equal(card.RestaurantId, listedBasic.RestaurantId);
        Assert.Equal(card.NameAr, listedBasic.NameAr);
        Assert.Equal(card.LogoUrl, listedBasic.LogoUrl);
        Assert.Equal(card.MealCount, listedBasic.MealCount);

        var filteredResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic,
                [platinumRestaurant.Id, restaurant.Id, Guid.NewGuid()], CancellationToken.None));
        var filtered = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(filteredResult.Value).ToList();
        Assert.Equal(restaurant.Id, Assert.Single(filtered).RestaurantId);

        var platinumListResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Platinum, null, CancellationToken.None));
        var platinumList = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(platinumListResult.Value).ToList();
        Assert.Equal([restaurant.Id, platinumRestaurant.Id], platinumList.Select(x => x.RestaurantId));
        Assert.Equal("مطعم ٢", platinumList[1].NameAr);

        var unpricedResult = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None));
        var unpricedTiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(unpricedResult.Value).ToList();
        Assert.Equal(2, unpricedTiers.Count);
        Assert.All(unpricedTiers, x =>
        {
            Assert.Null(x.DurationDays);
            Assert.Null(x.TotalPrice);
            Assert.Null(x.DailyPrice);
            Assert.Null(x.PriceDifference);
            Assert.Null(x.CurrencyCode);
            Assert.Null(x.FridayDays);
            Assert.Null(x.TotalServiceDays);
        });

        var fridayDurationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, true,
            CancellationToken.None));
        var fridayDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 6);
        Assert.True(fridayDuration.IncludeFriday);
        Assert.Equal(1, fridayDuration.FridayDays);
        Assert.Equal(7, fridayDuration.TotalServiceDays);
        var fridayMonthlyDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 26);
        Assert.Equal(4, fridayMonthlyDuration.FridayDays);
        Assert.Equal(30, fridayMonthlyDuration.TotalServiceDays);

        var fridayTiersResult = Assert.IsType<OkObjectResult>(await controller.Tiers(program.Id, bundle.Id, 6, true,
            CancellationToken.None));
        var fridayTiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(fridayTiersResult.Value).ToList();
        Assert.Equal(2, fridayTiers.Count);
        Assert.All(fridayTiers, x =>
        {
            Assert.True(x.IncludeFriday);
            Assert.Equal(1, x.FridayDays);
            Assert.Equal(7, x.TotalServiceDays);
            Assert.True(x.FridayAvailability.IsAvailable);
        });
        var basicFridayTier = fridayTiers.Single(x => x.Tier == RestaurantTier.Basic);
        Assert.Equal(0m, basicFridayTier.PriceDifference);
        Assert.True(basicFridayTier.IsRecommended);

        var durationsWithoutBundleResult = Assert.IsType<OkObjectResult>(
            await controller.Durations(program.Id, null, false, CancellationToken.None));
        var durationsWithoutBundle = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsWithoutBundleResult.Value).ToList();
        Assert.NotEmpty(durationsWithoutBundle);

        var tiersWithoutBundleResult = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, null, 6, false, CancellationToken.None));
        var tiersWithoutBundle = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(tiersWithoutBundleResult.Value).ToList();
        Assert.Equal(2, tiersWithoutBundle.Count);
        Assert.All(tiersWithoutBundle, x => Assert.NotNull(x.TotalPrice));
    }

    [Fact]
    public async Task Summary_WithoutRestaurantId_ReturnsPackageSummaryForSelectedTier()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_SUMMARY", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
            ImageStorageKey = "uploads/admin/programs/keto.webp",
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_SUMMARY", NameAr = "باقة 4 وجبات", NameEn = "4-meal bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(104m, "KWD"),
            ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                9m, 26, DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m))));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule,
            new BundleSlotRequirementService(db));
        var regionId = Guid.NewGuid();
        db.Add(new CustomerProfile
        {
            UserId = currentUser.UserId!.Value,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
        });
        var address = new CustomerAddress
        {
            UserId = currentUser.UserId!.Value, Label = "المنزل", AddressLine = "قطعة 4 شارع 12",
            Governorate = "العاصمة", Area = "الشرق", RegionId = regionId, IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true,
        });
        await db.SaveChangesAsync();
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == currentUser.UserId!.Value);
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryRegionId = regionId;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        await db.SaveChangesAsync();
        // Summary validates StartDate against Kuwait calendar day, not UTC date.
        var request = new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, schedule.GetKuwaitToday());
        var chosenStart = request.StartDate!.Value;
        var expectedFirstDelivery = schedule.ResolveFirstDeliveryDate(chosenStart, false);

        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);
        Assert.Equal(1, summary.RestaurantCount);
        Assert.Equal("المنزل", summary.Address!.Label);
        Assert.Equal(regionId, summary.Address.RegionId);
        var delivering = Assert.Single(summary.Restaurants!);
        Assert.Equal(restaurant.Id, delivering.RestaurantId);
        Assert.Equal("مطعم", delivering.NameAr);
        Assert.True(summary.CanCheckout);
        Assert.Equal(RestaurantTier.Basic, summary.Tier);
        Assert.False(string.IsNullOrWhiteSpace(summary.TierNameAr));
        Assert.False(string.IsNullOrWhiteSpace(summary.TierNameEn));
        Assert.Equal(26, summary.DurationDays);
        Assert.Equal(bundle.Id, summary.BundleId);
        Assert.Contains("keto.webp", summary.ProgramImageUrl);
        Assert.Equal(chosenStart, summary.ChosenStartDate);
        Assert.Equal(expectedFirstDelivery, summary.StartDate);
        Assert.NotNull(summary.RestaurantRepetition);
        Assert.Equal(1, summary.RestaurantRepetition!.RestaurantCount);
        Assert.Equal(26, summary.RestaurantRepetition.DurationDays);
        Assert.Equal(26, summary.RestaurantRepetition.Limit); // single restaurant → full duration
        Assert.True(summary.FinalPrice > 0m);
        var lunch = Assert.Single(summary.SlotRequirements);
        Assert.Equal("lunch", lunch.Slot);
        Assert.Equal("الغداء", lunch.NameAr);
        Assert.Equal("Lunch", lunch.NameEn);
        Assert.Equal(1, lunch.Count);
        Assert.Equal("يشمل اشتراكك", summary.Includes.TitleAr);
        Assert.Equal("Your subscription includes", summary.Includes.TitleEn);
        Assert.Equal(3, summary.Includes.Items.Count);
        Assert.Equal("DAILY_MEALS", summary.Includes.Items[0].Code);
        Assert.Contains("وجبة واحدة يوميًا", summary.Includes.Items[0].TextAr);
        Assert.Contains("الغداء", summary.Includes.Items[0].TextAr);
        Assert.Equal("RESTAURANTS", summary.Includes.Items[1].Code);
        Assert.Contains("مطعم واحد", summary.Includes.Items[1].TextAr);
        Assert.Equal("SERVICE_DAYS", summary.Includes.Items[2].Code);
    }

    [Fact]
    public async Task Readiness_WhenIncomplete_ReturnsAddressNextAction()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_READY", NameAr = "كيتو", NameEn = "Keto",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_READY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        db.Add(new CustomerProfile { UserId = currentUser.UserId!.Value });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.False(readiness.IsComplete);
        Assert.Equal(SubscriptionCheckoutGateCodes.Address, readiness.NextAction);
        Assert.Equal("كيتو", readiness.Program.NameAr);
    }

    [Fact]
    public async Task Readiness_WhenAddressOnly_ReturnsDietaryPreferencesNextAction()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.Equal(SubscriptionCheckoutGateCodes.DietaryPreferences, readiness.NextAction);
        Assert.False(readiness.CanOpenSummary);
    }

    [Fact]
    public async Task Readiness_WhenAddressAndDietary_ReturnsAllergiesNextAction()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db, dietary: true);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.Equal(SubscriptionCheckoutGateCodes.Allergies, readiness.NextAction);
        Assert.False(readiness.CanOpenSummary);
    }

    [Fact]
    public async Task Readiness_WhenComplete_CanOpenSummary()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db, allergies: true, dietary: true);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.True(readiness.IsComplete);
        Assert.True(readiness.CanOpenSummary);
        Assert.Equal(SubscriptionCheckoutGateCodes.Summary, readiness.NextAction);
    }

    private static async Task<(MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle, Guid UserId)> SeedReadinessContextAsync(
        AppDbContext db, bool allergies = false, bool dietary = false)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_READY", NameAr = "كيتو", NameEn = "Keto",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_READY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var userId = Guid.NewGuid();
        db.AddRange(program, bundle, new CustomerProfile
        {
            UserId = userId,
            AllergiesCompletedAtUtc = allergies ? DateTime.UtcNow : null,
            DietaryPreferencesCompletedAtUtc = dietary ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return (program, bundle, userId);
    }

    [Fact]
    public async Task Summary_WhenIncomplete_ReturnsConflictWithReadiness()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_INCOMPLETE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_INCOMPLETE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        db.Add(new CustomerProfile { UserId = currentUser.UserId!.Value });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ConflictObjectResult>(await controller.Summary(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None));
        var payload = result.Value!;
        var error = payload.GetType().GetProperty("error")?.GetValue(payload)?.ToString();
        Assert.Equal(SubscriptionCheckoutGateCodes.ProfileIncomplete, error);
    }

    [Fact]
    public async Task Summary_OmitsRestaurantsOutsideAddressCoverage()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_COVER", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
            ImageStorageKey = "uploads/admin/programs/cover.webp",
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_COVER", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var covered = new RestaurantProfile
        {
            TradeNameAr = "يوصل", TradeNameEn = "Covered", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var uncovered = new RestaurantProfile
        {
            TradeNameAr = "لا يوصل", TradeNameEn = "Uncovered", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, covered, uncovered);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = covered.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = uncovered.Id, IsActive = true });
        db.RestaurantBundlePricings.AddRange(
            new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = covered.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            },
            new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = uncovered.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                9m, 26, DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m))));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule,
            new BundleSlotRequirementService(db));
        var regionId = Guid.NewGuid();
        db.Add(new CustomerProfile
        {
            UserId = currentUser.UserId!.Value,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
        });
        var address = new CustomerAddress
        {
            UserId = currentUser.UserId!.Value, Label = "العمل", AddressLine = "شارع الخليج",
            RegionId = regionId, IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = covered.Id, RegionId = regionId, IsActive = true,
        });
        await db.SaveChangesAsync();
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == currentUser.UserId!.Value);
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryRegionId = regionId;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        await db.SaveChangesAsync();

        // Summary validates StartDate against Kuwait calendar day, not UTC date.
        var result = Assert.IsType<OkObjectResult>(await controller.Summary(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, schedule.GetKuwaitToday()), CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);
        Assert.Equal(1, summary.RestaurantCount);
        Assert.Equal("العمل", summary.Address!.Label);
        Assert.Equal("يوصل", Assert.Single(summary.Restaurants!).NameAr);
        Assert.DoesNotContain(summary.Restaurants!, x => x.RestaurantId == uncovered.Id);
        Assert.Contains("مطعم واحد", summary.Includes.Items.Single(x => x.Code == "RESTAURANTS").TextAr);
    }

    [Fact]
    public async Task TierRestaurants_ReturnsLogoNameRatingMealsAndClassifications()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_BROWSE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_BROWSE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var first = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "دايت هاوس", TradeNameEn = "Diet House", LegalNameAr = "دايت هاوس", LegalNameEn = "Diet House",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var second = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen", LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, first, second);
        foreach (var restaurant in new[] { first, second })
        {
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(52m, "KWD"),
                ContractedSupplyPrice26 = new Money(40m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        }
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = first.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/diet-house.webp",
        });
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = first.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        db.Meals.AddRange(
            new MealMate.Domain.RestaurantOperations.Meal
            {
                RestaurantId = first.Id, MenuId = menu.Id, Menu = menu, Code = "M1",
                NameAr = "وجبة الباقة", NameEn = "Bundle meal",
                ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
                ProgramId = program.Id, BundleId = bundle.Id,
            },
            new MealMate.Domain.RestaurantOperations.Meal
            {
                RestaurantId = first.Id, MenuId = menu.Id, Menu = menu, Code = "M2",
                NameAr = "وجبة أخرى", NameEn = "Other meal",
                ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            });
        db.RestaurantRatings.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantRating
            {
                RestaurantId = first.Id, CustomerUserId = Guid.NewGuid(), Score = 5,
            },
            new MealMate.Domain.RestaurantOperations.RestaurantRating
            {
                RestaurantId = first.Id, CustomerUserId = Guid.NewGuid(), Score = 4,
            });
        var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
        pricingSettings.TierLabels.BasicAr = "سيلكت";
        pricingSettings.TierLabels.BasicEn = "Select";
        pricingSettings.TierLabels.BasicLogoStorageKey = "uploads/admin/tiers/basic.webp";
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, first.Id, second.Id);
        var result = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic,
                [second.Id, first.Id], CancellationToken.None));
        var cards = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(result.Value).ToList();
        Assert.Equal([second.Id, first.Id], cards.Select(x => x.RestaurantId));

        var dietHouse = cards.Single(x => x.RestaurantId == first.Id);
        Assert.Equal("دايت هاوس", dietHouse.NameAr);
        Assert.Equal("Diet House", dietHouse.NameEn);
        Assert.Contains("diet-house.webp", dietHouse.LogoUrl);
        Assert.Equal(4.5m, dietHouse.Rating);
        Assert.Equal(2, dietHouse.MealCount);
        Assert.Equal(1, dietHouse.BundleMealCount);
        Assert.Equal(RestaurantTier.Basic, dietHouse.Tier);
        Assert.Equal("سيلكت", dietHouse.TierNameAr);
        Assert.Equal("Select", dietHouse.TierNameEn);
        var classification = Assert.Single(dietHouse.Classifications);
        Assert.Contains("basic.webp", classification.LogoUrl);
        Assert.Equal(RestaurantTier.Basic, classification.Tier);
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameEn"));

        var protein = cards.Single(x => x.RestaurantId == second.Id);
        Assert.Equal("مطبخ البروتين", protein.NameAr);
        Assert.Null(protein.LogoUrl);
        Assert.Null(protein.Rating);
        Assert.Equal(0, protein.MealCount);
        Assert.Equal(0, protein.BundleMealCount);
    }

    [Fact]
    public async Task RestaurantBrowseDetails_ReturnsLogoOriginsAndMealsGroupedBySlot()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_DETAILS", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_DETAILS", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen",
            LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(40m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/protein.webp",
        });
        db.Countries.AddRange(
            new Country { Code = "ES", NameAr = "اسبانيا", NameEn = "Spain", DialCode = "+34", FlagEmoji = "🇪🇸" },
            new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" },
            new Country { Code = "NO", NameAr = "النرويج", NameEn = "Norway", DialCode = "+47", FlagEmoji = "🇳🇴" });
        var oil = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "OIL", NameAr = "زيت زيتون", NameEn = "Olive oil",
            Category = "oils", OriginCountryCode = "ES", IsLocal = false, IsOriginVisibleToCustomers = true,
            IsCore = true, RestaurantIconUrl = "uploads/restaurant/ingredients/oils.webp", IsActive = true,
        };
        var poultry = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "CHK", NameAr = "دجاج مشوي", NameEn = "Grilled chicken",
            Category = "poultry", OriginCountryCode = "KW", IsLocal = true, IsOriginVisibleToCustomers = true,
            IsCore = true, MealIconUrl = "uploads/restaurant/ingredients/chicken.webp", IsActive = true,
        };
        var hidden = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "HID", NameAr = "مخفي", NameEn = "Hidden",
            Category = "seafood", OriginCountryCode = "NO", IsLocal = false, IsOriginVisibleToCustomers = false,
            IsCore = true, IsActive = true,
        };
        var garnish = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "LEM", NameAr = "ليمون", NameEn = "Lemon",
            Category = "produce", OriginCountryCode = "KW", IsLocal = true, IsOriginVisibleToCustomers = true,
            IsCore = false, MealIconUrl = "uploads/restaurant/ingredients/lemon.webp", IsActive = true,
        };
        db.Ingredients.AddRange(oil, poultry, hidden, garnish);
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = restaurant.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        var breakfast = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "B1",
            NameAr = "فطور بروتين", NameEn = "Protein breakfast", Slot = "breakfast",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 550, ProteinG = 35, CarbsG = 55, FatG = 18,
        };
        var lunch = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "L1",
            NameAr = "غداء دجاج", NameEn = "Chicken lunch", Slot = "lunch",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 620, ProteinG = 40, CarbsG = 48, FatG = 16,
        };
        var snack = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "S1",
            NameAr = "سناك", NameEn = "Snack", Slot = "snack",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
        };
        db.Meals.AddRange(breakfast, lunch, snack);
        db.MealImages.Add(new MealMate.Domain.RestaurantOperations.MealImage
        {
            MealId = breakfast.Id, ImageUrl = "uploads/restaurant/meals/breakfast.webp", IsPrimary = true, DisplayOrder = 1,
        });
        db.MealIngredients.AddRange(
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = poultry.Id, PortionGrams = 120, SortOrder = 1,
            },
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = oil.Id, PortionGrams = 10, SortOrder = 2,
            },
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = garnish.Id, PortionGrams = 5, SortOrder = 3,
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseDetails(program.Id, bundle.Id, restaurant.Id, CancellationToken.None));
        var details = Assert.IsType<CustomerRestaurantBrowseDetailsDto>(result.Value);
        Assert.Equal(restaurant.Id, details.RestaurantId);
        Assert.Equal("مطبخ البروتين", details.NameAr);
        Assert.Contains("protein.webp", details.LogoUrl);
        Assert.Contains("breakfast.webp", details.CoverImageUrl);
        Assert.Equal(3, details.CoreIngredients.Count);
        var oliveOil = details.CoreIngredients.Single(x => x.NameAr == "زيت زيتون");
        Assert.Contains("oils.webp", oliveOil.IconUrl);
        Assert.False(oliveOil.IsLocal);
        Assert.Equal("ES", oliveOil.OriginCountryCode);
        Assert.Equal("اسبانيا", oliveOil.OriginCountryNameAr);
        Assert.Equal("Spain", oliveOil.OriginCountryNameEn);
        Assert.Equal("🇪🇸", oliveOil.FlagEmoji);
        var chicken = details.CoreIngredients.Single(x => x.NameAr == "دجاج مشوي");
        Assert.Contains("chicken.webp", chicken.IconUrl);
        Assert.True(chicken.IsLocal);
        Assert.Equal("KW", chicken.OriginCountryCode);
        Assert.Equal("الكويت", chicken.OriginCountryNameAr);
        Assert.Equal("🇰🇼", chicken.FlagEmoji);
        var hiddenCore = details.CoreIngredients.Single(x => x.NameAr == "مخفي");
        Assert.Null(hiddenCore.OriginCountryCode);
        Assert.Null(hiddenCore.OriginCountryNameAr);
        Assert.Null(hiddenCore.FlagEmoji);
        Assert.DoesNotContain(details.CoreIngredients, x => x.NameAr == "ليمون");
        Assert.Equal(2, details.IngredientOrigins.Count);
        var oils = details.IngredientOrigins.Single(x => x.Category == "oils");
        Assert.Equal("الزيوت", oils.NameAr);
        Assert.False(oils.IsLocal);
        Assert.Equal("مستوردة", oils.SourceAr);
        Assert.Equal("ES", oils.OriginCountryCode);
        Assert.Equal("اسبانيا", oils.OriginCountryNameAr);
        Assert.Equal("🇪🇸", oils.FlagEmoji);
        Assert.Contains("oils.webp", oils.IconUrl);
        var poultryOrigin = details.IngredientOrigins.Single(x => x.Category == "poultry");
        Assert.Equal("الدواجن", poultryOrigin.NameAr);
        Assert.True(poultryOrigin.IsLocal);
        Assert.Equal("طازجة محلية", poultryOrigin.SourceAr);
        Assert.Equal("KW", poultryOrigin.OriginCountryCode);
        Assert.DoesNotContain(details.IngredientOrigins, x => x.Category == "seafood");
        Assert.DoesNotContain(details.IngredientOrigins, x => x.Category == "produce");
        Assert.Equal(["breakfast", "lunch", "snack"], details.MealGroups.Select(x => x.Slot));
        Assert.Equal("الإفطار", details.MealGroups[0].NameAr);
        var breakfastCard = Assert.Single(details.MealGroups[0].Meals);
        Assert.Equal("فطور بروتين", breakfastCard.NameAr);
        Assert.Equal(550, breakfastCard.Calories);
        Assert.Equal(35, breakfastCard.ProteinG);
        Assert.Equal(55, breakfastCard.CarbsG);
        Assert.Equal(18, breakfastCard.FatG);
        // Core ingredients live on restaurant details — meals expose non-core chips only.
        Assert.Equal(["ليمون"], breakfastCard.Ingredients.Select(x => x.NameAr));
        Assert.Equal("produce", breakfastCard.Ingredients[0].Category);
        Assert.False(breakfastCard.Ingredients[0].IsCore);
        Assert.Contains("lemon.webp", breakfastCard.Ingredients[0].IconUrl);
        Assert.Null(typeof(CustomerRestaurantBrowseMealDto).GetProperty("DescriptionAr"));
        Assert.Null(typeof(CustomerRestaurantBrowseMealDto).GetProperty("FiberG"));
        Assert.Equal("غداء دجاج", Assert.Single(details.MealGroups[1].Meals).NameAr);
        Assert.Equal("سناك", Assert.Single(details.MealGroups[2].Meals).NameAr);
    }

    [Theory]
    [InlineData(350, true)]
    [InlineData(null, false)]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    public async Task RestaurantBrowseMealDetails_ReturnsDisplayPageWithShortAllergens(int? servingWeightG, bool hasHighlights)
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_MEAL", NameAr = "خسارة الوزن", NameEn = "Weight Loss",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_MEAL", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen",
            LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(40m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/protein.webp",
        });
        db.AllergenDefinitions.AddRange(
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "GLUTEN", NameAr = "جلوتين", NameEn = "Gluten", IsActive = true, SortOrder = 1 },
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "MILK", NameAr = "لاكتوز", NameEn = "Lactose", IsActive = true, SortOrder = 2 },
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "TREE_NUTS", NameAr = "مكسرات", NameEn = "Nuts", IsActive = true, SortOrder = 3 });
        var chicken = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "CHK", NameAr = "دجاج مشوي", NameEn = "Grilled chicken",
            Category = "poultry", IsActive = true,
        };
        var rice = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "RICE", NameAr = "أرز بني", NameEn = "Brown rice",
            Category = "grains", IsActive = true,
        };
        db.Ingredients.AddRange(chicken, rice);
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = restaurant.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        var meal = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "L1",
            NameAr = "دجاج مشوي مع الأرز البني", NameEn = "Grilled chicken with brown rice",
            DescriptionAr = "وجبة متكاملة عالية البروتين.", DescriptionEn = "A high-protein complete meal.",
            Slot = "lunch", ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 550, ProteinG = 65, CarbsG = 60, FatG = 15, FiberG = 7,
            ServingWeightG = servingWeightG,
            NutritionTagCodes = hasHighlights ? "HIGH_PROTEIN,DIET_FRIENDLY,NATURAL,HIGH_PROTEIN,UNKNOWN" : null,
            PresentAllergenCodes = "GLUTEN,MILK,TREE_NUTS",
        };
        db.Meals.Add(meal);
        db.MealImages.Add(new MealMate.Domain.RestaurantOperations.MealImage
        {
            MealId = meal.Id, ImageUrl = "uploads/restaurant/meals/chicken.webp", IsPrimary = true, DisplayOrder = 1,
        });
        db.MealIngredients.AddRange(
            new MealMate.Domain.RestaurantOperations.MealIngredient { MealId = meal.Id, IngredientId = chicken.Id, PortionGrams = 180, SortOrder = 1 },
            new MealMate.Domain.RestaurantOperations.MealIngredient { MealId = meal.Id, IngredientId = rice.Id, PortionGrams = 120, SortOrder = 2 });
        db.RestaurantRatings.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 5 },
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 5 },
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 4 });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseMealDetails(program.Id, bundle.Id, restaurant.Id, meal.Id, CancellationToken.None));
        var page = Assert.IsType<CustomerRestaurantBrowseMealDetailsDto>(result.Value);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(page,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.True(json.GetProperty("ingredients")[0].TryGetProperty("weightG", out var weight));
        Assert.Equal(180m, weight.GetDecimal());
        var nutrition = json.GetProperty("nutrition");
        Assert.True(nutrition.TryGetProperty("perMeal", out var perMeal));
        Assert.Equal(550m, perMeal.GetProperty("calories").GetDecimal());
        Assert.True(nutrition.TryGetProperty("per100g", out var per100g));
        if (servingWeightG is > 0)
        {
            Assert.Equal(157.14m, per100g.GetProperty("calories").GetDecimal());
            Assert.Equal(18.57m, per100g.GetProperty("proteinG").GetDecimal());
            Assert.Equal(17.14m, per100g.GetProperty("carbsG").GetDecimal());
            Assert.Equal(4.29m, per100g.GetProperty("fatG").GetDecimal());
            Assert.Equal(2m, per100g.GetProperty("fiberG").GetDecimal());
            Assert.Equal(perMeal.GetProperty("proteinPercent").GetInt32(), per100g.GetProperty("proteinPercent").GetInt32());
        }
        else
            Assert.Equal(System.Text.Json.JsonValueKind.Null, per100g.ValueKind);
        Assert.True(json.TryGetProperty("tags", out var highlights));
        Assert.Equal(hasHighlights ? new[] { "diet_friendly", "high_protein", "natural" } : Array.Empty<string>(),
            highlights.EnumerateArray().Select(x => x.GetProperty("id").GetString()));
        if (hasHighlights)
        {
            var proteinHighlight = Assert.Single(highlights.EnumerateArray(), x => x.GetProperty("id").GetString() == "high_protein");
            Assert.Equal("عالي البروتين", proteinHighlight.GetProperty("nameAr").GetString());
            Assert.Equal("protein", proteinHighlight.GetProperty("iconType").GetString());
        }
        var compactResult = Assert.IsType<OkObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, meal.Id, CancellationToken.None));
        var compactPage = Assert.IsType<CustomerRestaurantBrowseMealDetailsDto>(compactResult.Value);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(page), System.Text.Json.JsonSerializer.Serialize(compactPage));

        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(Guid.NewGuid(), meal.Id, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, Guid.NewGuid(), CancellationToken.None));

        bundle.IsActive = false;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, meal.Id, CancellationToken.None));
        bundle.IsActive = true;

        meal.ProgramId = Guid.NewGuid();
        meal.BundleId = null;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, meal.Id, CancellationToken.None));
        meal.ProgramId = program.Id;
        meal.BundleId = bundle.Id;

        restaurant.IsOperationallyActive = false;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, meal.Id, CancellationToken.None));
        restaurant.IsOperationallyActive = true;

        meal.IsAvailableForNewOrders = false;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(
            await controller.BundleMealBrowseDetails(bundle.Id, meal.Id, CancellationToken.None));
        Assert.Equal("دجاج مشوي مع الأرز البني", page.NameAr);
        Assert.Equal("وجبة متكاملة عالية البروتين.", page.DescriptionAr);
        Assert.Contains("chicken.webp", page.ImageUrl);
        Assert.Equal("lunch", page.Slot);
        Assert.Equal("مطبخ البروتين", page.RestaurantNameAr);
        Assert.Contains("protein.webp", page.RestaurantLogoUrl);
        Assert.Equal(4.7m, page.RestaurantRating);
        Assert.Equal(3, page.RestaurantRatingCount);
        Assert.Null(typeof(CustomerRestaurantBrowseMealDetailsDto).GetProperty("ProgramNameAr"));
        Assert.Null(page.DeliveryWindowStart);
        Assert.Equal(["دجاج مشوي", "أرز بني"], page.Ingredients.Select(x => x.NameAr));
        Assert.Equal(550, page.Nutrition.Calories);
        Assert.Equal(65, page.Nutrition.ProteinG);
        Assert.Equal(60, page.Nutrition.CarbsG);
        Assert.Equal(15, page.Nutrition.FatG);
        Assert.Equal(7, page.Nutrition.FiberG);
        Assert.Equal(100, page.Nutrition.CarbsPercent + page.Nutrition.ProteinPercent + page.Nutrition.FatPercent);
        Assert.Equal(["GLUTEN", "MILK", "TREE_NUTS"], page.Allergens.Select(x => x.Code));
        Assert.Equal(["جلوتين", "لاكتوز", "مكسرات"], page.Allergens.Select(x => x.NameAr));
        Assert.Equal("Gluten", page.Allergens[0].NameEn);
        Assert.DoesNotContain(page.Allergens, x => x.NameAr.Contains("حبوب") || x.NameAr.Contains("مشتقات"));
    }

    [Fact]
    public void BundleMealBrowseDetails_ExposesCompactRoute()
    {
        var action = typeof(CustomerSubscriptionFlowController).GetMethod(nameof(CustomerSubscriptionFlowController.BundleMealBrowseDetails));
        Assert.NotNull(action);
        var route = Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>());
        Assert.Equal("bundles/{bundleId:guid}/meals/{mealId:guid}", route.Template);
    }

    [Fact]
    public async Task FridayToggle_IgnoresUnclassifiedRestaurants_AndConflictsWithUnavailablePayload()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FRIDAY", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_FRIDAY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var classified = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم أساسي", TradeNameEn = "Classified", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var unclassifiedFriday = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم غير مصنف", TradeNameEn = "Unclassified Friday", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var run = new ClassificationRun
        {
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow,
        };
        db.AddRange(program, bundle, classified, unclassifiedFriday, run);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = classified.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = unclassifiedFriday.Id, IsActive = true });
        db.RestaurantOperatingSchedules.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
            {
                RestaurantId = classified.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = false,
            },
            new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
            {
                RestaurantId = unclassifiedFriday.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = true,
            });
        db.RestaurantClassifications.Add(new RestaurantClassification
        {
            ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = classified.Id,
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            Tier = RestaurantTier.Basic,
        });
        await db.SaveChangesAsync();

        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select",
                [new(6, 66, 11), new(12, 120, 10), new(26, 234, 9)]),
        ];
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(catalog));
        var controller = CreateController(db, out _, out var currentUser, sender);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, classified.Id, unclassifiedFriday.Id);

        var durationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, false, CancellationToken.None));
        var durations = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsResult.Value).ToList();
        Assert.All(durations, x =>
        {
            Assert.False(x.SupportsFriday);
            Assert.False(x.FridayOption.IsEnabled);
            Assert.False(x.FridayAvailability.IsAvailable);
            Assert.Equal("DISABLED", x.FridayAvailability.DisplayType);
        });

        var fridayTiers = Assert.IsType<ConflictObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, null, true, CancellationToken.None));
        var payload = fridayTiers.Value!;
        var error = payload.GetType().GetProperty("error")?.GetValue(payload)?.ToString()
            ?? payload.GetType().GetProperty("Error")?.GetValue(payload)?.ToString();
        Assert.Equal("subscription_flow.friday_unavailable", error);
        var availability = payload.GetType().GetProperty("fridayAvailability")?.GetValue(payload)
            ?? payload.GetType().GetProperty("FridayAvailability")?.GetValue(payload);
        Assert.NotNull(availability);
        var isAvailable = Assert.IsType<bool>(availability!.GetType().GetProperty("IsAvailable")!.GetValue(availability));
        Assert.False(isAvailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Programs_existing_route_exposes_address_availability_without_wrapping_cards(bool paged)
    {
        await using var db = CreateDb();
        var covered = NewProgram("LEGACY_COVERED");
        var unavailable = NewProgram("LEGACY_UNAVAILABLE");
        var coveredBundle = NewBundle(covered, "COVERED_BUNDLE");
        var unavailableBundle = NewBundle(unavailable, "UNAVAILABLE_BUNDLE");
        var restaurant = NewApprovedRestaurant("Covered restaurant");
        db.AddRange(covered, unavailable, coveredBundle, unavailableBundle, restaurant,
            new RestaurantProgramBundleOffer { ProgramId = covered.Id, BundleId = coveredBundle.Id, RestaurantId = restaurant.Id, IsActive = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var user,
            catalogAvailability: CreateCatalogAvailability(db, Substitute.For<ISender>(), Substitute.For<ISubscriptionRestaurantAvailabilityService>()));
        await SeedCustomerDeliveryCoverageAsync(db, user, restaurant.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Programs(
            CancellationToken.None, page: paged ? 1 : null, pageSize: paged ? 20 : null));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var items = paged ? json.GetProperty("items") : json;
        Assert.Equal(2, items.GetArrayLength());
        var disabled = items.EnumerateArray().Single(x => x.GetProperty("programId").GetGuid() == unavailable.Id);
        Assert.False(disabled.TryGetProperty("option", out _));
        Assert.False(disabled.GetProperty("availability").GetProperty("canSelect").GetBoolean());
        Assert.Equal("no_restaurants_for_address", disabled.GetProperty("availability").GetProperty("status").GetString());
        var enabled = items.EnumerateArray().Single(x => x.GetProperty("programId").GetGuid() == covered.Id);
        Assert.True(enabled.GetProperty("availability").GetProperty("canSelect").GetBoolean());
        if (paged) Assert.Equal(2, json.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bundles_existing_route_exposes_disabled_state_and_preserves_profile_flags(bool paged)
    {
        await using var db = CreateDb();
        var program = NewProgram("LEGACY_BUNDLES");
        var covered = NewBundle(program, "B_COVERED");
        var unavailable = NewBundle(program, "B_UNAVAILABLE");
        var restaurant = NewApprovedRestaurant("Covered restaurant");
        db.AddRange(program, covered, unavailable, restaurant,
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = covered.Id, RestaurantId = restaurant.Id, IsActive = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var user,
            catalogAvailability: CreateCatalogAvailability(db, Substitute.For<ISender>(), Substitute.For<ISubscriptionRestaurantAvailabilityService>()));
        await SeedCustomerDeliveryCoverageAsync(db, user, restaurant.Id);

        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id,
            CancellationToken.None, page: paged ? 1 : null, pageSize: paged ? 20 : null));
        var dto = Assert.IsType<CustomerProgramBundlesDto>(result.Value);
        Assert.True(dto.HasDeliveryAddress);
        Assert.True(dto.HasAllergies);
        Assert.True(dto.HasDietaryPreferences);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(dto,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var items = json.GetProperty("bundles");
        var disabled = items.EnumerateArray().Single(x => x.GetProperty("bundleId").GetGuid() == unavailable.Id);
        Assert.False(disabled.TryGetProperty("option", out _));
        Assert.False(disabled.GetProperty("availability").GetProperty("canSelect").GetBoolean());
        Assert.Equal(0, disabled.GetProperty("restaurants").GetArrayLength());
        var enabled = items.EnumerateArray().Single(x => x.GetProperty("bundleId").GetGuid() == covered.Id);
        Assert.True(enabled.GetProperty("availability").GetProperty("canSelect").GetBoolean());
        if (paged) Assert.Equal(2, dto.Pagination!.TotalCount);
    }

    [Fact]
    public async Task Existing_routes_refresh_coverage_when_the_active_address_changes()
    {
        await using var db = CreateDb();
        var program = NewProgram("ADDRESS_SWITCH");
        var bundle = NewBundle(program, "ADDRESS_BUNDLE");
        var restaurant = NewApprovedRestaurant("Address restaurant");
        db.AddRange(program, bundle, restaurant,
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var user,
            catalogAvailability: CreateCatalogAvailability(db, Substitute.For<ISender>(), Substitute.For<ISubscriptionRestaurantAvailabilityService>()));
        var coveredAddress = await SeedCustomerDeliveryCoverageAsync(db, user, restaurant.Id);

        async Task AssertCoverageAsync(bool expected)
        {
            var programsResult = Assert.IsType<OkObjectResult>(await controller.Programs());
            var programCard = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CustomerProgramCardDto>>(programsResult.Value));
            Assert.Equal(expected, programCard.Availability!.CanSelect);
            var bundlesResult = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
            var bundleCard = Assert.Single(Assert.IsType<CustomerProgramBundlesDto>(bundlesResult.Value).Bundles);
            Assert.Equal(expected, bundleCard.Availability!.CanSelect);
            Assert.Equal(expected ? 1 : 0, bundleCard.RestaurantCount);
            Assert.Equal(expected ? 1 : 0, bundleCard.Restaurants.Count);
        }

        await AssertCoverageAsync(true);
        await SeedCustomerDeliveryCoverageAsync(db, user);
        await AssertCoverageAsync(false);
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == user.UserId);
        profile.ActiveDeliveryAddressId = coveredAddress;
        await db.SaveChangesAsync();
        await AssertCoverageAsync(true);
    }

    // ---- Additive catalog routes keep their wrapped envelope contracts. ----

    [Fact]
    public async Task CatalogPrograms_ReturnsEnvelopeWithAvailabilityAndPagination()
    {
        await using var db = CreateDb();
        var program = NewProgram("CAT_P1");
        var bundle = NewBundle(program, "CAT_B1");
        var restaurant = NewApprovedRestaurant("Catalog Restaurant");
        db.AddRange(program, bundle, restaurant,
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var programsUser, sender: Substitute.For<ISender>(),
            catalogAvailability: CreateCatalogAvailability(db, Substitute.For<ISender>(), Substitute.For<ISubscriptionRestaurantAvailabilityService>()));
        // The active address must belong to the controller's own authenticated user.
        await SeedCustomerDeliveryCoverageAsync(db, programsUser, restaurant.Id);

        var result = await controller.CatalogPrograms(CancellationToken.None, page: 1, pageSize: 1);
        var ok = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<SubscriptionCatalogProgramsEnvelopeDto>(ok.Value);
        Assert.True(envelope.HasDeliveryAddress);
        Assert.Equal(SubscriptionCatalogAvailabilityStatus.Available, envelope.AddressCoverage.Status);
        var option = Assert.Single(envelope.Items);
        Assert.Equal(program.Id, option.Option.ProgramId);
        Assert.Equal("coverage", option.Availability.Scope);
        Assert.Equal(SubscriptionCatalogAvailabilityStatus.SelectionRequired, option.Availability.Status);
        Assert.True(option.Availability.CanSelect);
        Assert.Equal(1, option.Availability.CoveredRestaurantCount);
        Assert.Null(option.Availability.EligibleRestaurantCount);
        Assert.NotNull(envelope.Pagination);
        Assert.Equal(1, envelope.Pagination.TotalCount);

        // A zero-coverage active program stays visible, disabled with its reason.
        var ghost = NewProgram("CAT_GHOST");
        var ghostBundle = NewBundle(ghost, "CAT_GHOST_B");
        db.AddRange(ghost, ghostBundle);
        await db.SaveChangesAsync();
        var enriched = await controller.CatalogPrograms(CancellationToken.None, page: null, pageSize: null);
        var ghostEnvelope = Assert.IsType<SubscriptionCatalogProgramsEnvelopeDto>(Assert.IsType<OkObjectResult>(enriched).Value);
        var ghostOption = ghostEnvelope.Items.Single(x => x.Option.ProgramId == ghost.Id);
        Assert.Equal(SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress, ghostOption.Availability.Status);
        Assert.False(ghostOption.Availability.CanSelect);
    }

    [Fact]
    public async Task CatalogRoutes_AreForbiddenForNonCustomers()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserType.Returns(UserType.Restaurant);

        Assert.IsType<ForbidResult>(await controller.CatalogPrograms(CancellationToken.None, null, null));
        Assert.IsType<ForbidResult>(await controller.CatalogBundles(Guid.NewGuid(), CancellationToken.None, null, null));
        Assert.IsType<ForbidResult>(await controller.CatalogTiers(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CatalogTiers_AttachPeriodVerdicts_WhenContextComplete()
    {
        await using var db = CreateDb();
        var program = NewProgram("CAT_T");
        var bundle = NewBundle(program, "CAT_TB");
        var restaurant = NewApprovedRestaurant("Tier Restaurant");
        var run = new ClassificationRun { ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle, RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow };
        db.AddRange(program, bundle, restaurant, run,
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true },
            new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            },
            new RestaurantClassification
            {
                ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id,
                ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
                Tier = RestaurantTier.Basic,
            });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select", [new(6, 24m, 4m)], "KWD"),
        ];
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>.Success(catalog));

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        var availability = Substitute.For<ISubscriptionRestaurantAvailabilityService>();
        availability.EvaluateDetailedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), RestaurantTier.Basic,
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new IndividualRestaurantAvailabilityReport(
                new IndividualRestaurantAvailabilityDto(
                    IndividualRestaurantAvailabilityStatus.Available, 1, 1, DateTime.UtcNow, startDate, null),
                [], new Dictionary<Guid, RestaurantTier>(), Guid.NewGuid()));

        var controller = CreateController(db, out _, out var tiersUser, sender: sender,
            catalogAvailability: CreateCatalogAvailability(db, sender, availability));
        // The active address must belong to the controller's own authenticated user.
        await SeedCustomerDeliveryCoverageAsync(db, tiersUser, restaurant.Id);

        var result = await controller.CatalogTiers(program.Id, bundle.Id, CancellationToken.None,
            durationDays: 6, includeFriday: false, startDate: startDate, page: null, pageSize: null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var envelope = Assert.IsType<SubscriptionCatalogTiersEnvelopeDto>(ok.Value);
        Assert.True(envelope.PeriodContextComplete);
        var option = Assert.Single(envelope.Items);
        Assert.Equal(RestaurantTier.Basic, option.Option.Tier);
        Assert.Equal("period", option.Availability.Scope);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.Available, option.Availability.Status);
        Assert.True(option.Availability.CanSelect);
        Assert.Equal(1, option.Availability.EligibleRestaurantCount);
        Assert.NotNull(option.Option.TotalPrice);
        await availability.Received(1).EvaluateDetailedAsync(
            tiersUser.UserId!.Value, program.Id, bundle.Id, RestaurantTier.Basic, 6, false, startDate, Arg.Any<CancellationToken>());
    }

    private static SubscriptionCatalogAvailabilityService CreateCatalogAvailability(
        AppDbContext db, ISender sender, ISubscriptionRestaurantAvailabilityService availability)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));
        return new SubscriptionCatalogAvailabilityService(
            db, clock,
            new CustomerDeliveryAddressService(db, clock),
            new RestaurantTierClassificationService(db, clock),
            new AllowedSubscriptionDurationService(db),
            availability,
            sender);
    }

    private static MealMate.Domain.ProgramsSubscriptions.Program NewProgram(string code)
        => new() { Code = code, NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };

    private static Bundle NewBundle(MealMate.Domain.ProgramsSubscriptions.Program program, string code)
        => new() { ProgramId = program.Id, Program = program, Code = code, NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };

    private static RestaurantProfile NewApprovedRestaurant(string nameEn)
        => new()
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = nameEn, LegalNameEn = nameEn,
            TradeNameAr = nameEn, TradeNameEn = nameEn,
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };

    private static CustomerSubscriptionFlowController CreateController(AppDbContext db, out IPaymentGatewayService payment,
        out ICurrentUser currentUser, ISender? sender = null, ISubscriptionScheduleService? schedule = null,
        IBundleSlotRequirementService? slots = null, IRestaurantEligibilityService? eligibility = null,
        ISubscriptionCatalogAvailabilityService? catalogAvailability = null)
    {
        payment = Substitute.For<IPaymentGatewayService>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        var httpContext = new DefaultHttpContext();
        if (sender is not null)
            httpContext.RequestServices = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        slots ??= Substitute.For<IBundleSlotRequirementService>();
        if (slots is not BundleSlotRequirementService)
        {
            slots.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SlotRequirementDto>>([]));
        }
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        eligibility ??= Substitute.For<IRestaurantEligibilityService>();
        var resolvedSchedule = schedule ?? new SubscriptionScheduleService(clock);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var readiness = new SubscriptionCheckoutReadinessService(
            db, new AllowedSubscriptionDurationService(db), tiers,
            new CustomerDeliveryAddressService(db, clock),
            new CustomerIndividualSubscriptionGate(db));
        return new CustomerSubscriptionFlowController(db, currentUser,
            eligibility, Substitute.For<IMenuPlanResolutionService>(),
            resolvedSchedule, slots, payment,
            new AllowedSubscriptionDurationService(db), tiers, readiness,
            new CustomerIndividualSubscriptionGate(db),
            new RestaurantDiversificationCalculator(),
            new ConfigurationBuilder().Build(),
            CreateTestWebHostEnvironment(),
            queryService: null,
            catalogAvailability: catalogAvailability)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<Guid> SeedCustomerDeliveryCoverageAsync(
        AppDbContext db,
        ICurrentUser currentUser,
        params Guid[] restaurantIds)
    {
        var userId = currentUser.UserId!.Value;
        var regionId = Guid.NewGuid();
        if (!await db.CustomerProfiles.AnyAsync(x => x.UserId == userId))
        {
            db.CustomerProfiles.Add(new CustomerProfile
            {
                UserId = userId,
                AllergiesCompletedAtUtc = DateTime.UtcNow,
                DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
            });
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "المنزل",
            AddressLine = "السالمية",
            RegionId = regionId,
            IsDefault = true,
            Latitude = 29.34m,
            Longitude = 48.08m,
        };
        db.CustomerAddresses.Add(address);
        foreach (var restaurantId in restaurantIds.Distinct())
        {
            db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurantId,
                RegionId = regionId,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == userId);
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryRegionId = regionId;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        profile.AllergiesCompletedAtUtc ??= DateTime.UtcNow;
        profile.DietaryPreferencesCompletedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return address.Id;
    }

    private static async Task<(Guid ManagerMemberId, Guid SpouseMemberId)> SeedFamilyGroupWithSpouseAsync(
        AppDbContext db,
        Guid managerUserId,
        Guid managerAddressId)
    {
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerUserId,
            Name = "عائلة",
            Status = FamilyGroupStatus.Draft,
        };
        var manager = new FamilyMember
        {
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            NutritionalGoalCode = string.Empty,
            Gender = FamilyMemberGender.Male,
            AgeYears = 36,
            DeliveryAddressId = managerAddressId,
        };
        var spouse = new FamilyMember
        {
            Role = FamilyMemberRole.Member,
            DisplayName = "سارة",
            RelationshipCode = "spouse",
            NutritionalGoalCode = string.Empty,
            Gender = FamilyMemberGender.Female,
            AgeYears = 30,
        };
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();
        return (manager.Id, spouse.Id);
    }

    private static bool IsConflictWithError(IActionResult result, string expectedError)
    {
        if (result is not ConflictObjectResult conflict || conflict.Value is null)
            return false;
        var error = conflict.Value.GetType().GetProperty("error")?.GetValue(conflict.Value)?.ToString();
        return error == expectedError;
    }

    private static void AssertConflict(IActionResult result, string expectedError, string? expectedStep = null, string? expectedEndpoint = null)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var value = conflict.Value!;
        var error = value.GetType().GetProperty("error")?.GetValue(value)?.ToString();
        Assert.Equal(expectedError, error);

        if (expectedStep is not null)
        {
            var step = value.GetType().GetProperty("missingStep")?.GetValue(value)?.ToString();
            Assert.Equal(expectedStep, step);
        }

        if (expectedEndpoint is not null)
        {
            var endpoint = value.GetType().GetProperty("actionEndpoint")?.GetValue(value)?.ToString();
            Assert.Equal(expectedEndpoint, endpoint);
        }
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static IWebHostEnvironment CreateTestWebHostEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return environment;
    }
}
