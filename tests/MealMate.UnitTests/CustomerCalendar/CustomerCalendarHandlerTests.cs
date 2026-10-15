using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCalendar;

public sealed class CustomerCalendarHandlerTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateOnly Day2 = new(2026, 9, 2);
    private static readonly DateOnly Day3 = new(2026, 9, 3);
    private static readonly DateOnly Day4 = new(2026, 9, 4);
    private static readonly DateTime Now = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Deadline = new(2026, 8, 29, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Family_manual_auto_selection_cannot_choose_restaurant_without_grant()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: true, selectRestaurant: false);
        var result = await fx.Handlers.Handle(new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.permission_required");
        (await fx.Db.DailyRestaurantSelections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Family_calendar_read_includes_plan_prerequisite_without_granting_restaurant_writes()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: false, selectRestaurant: false);
        var visible = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        visible.IsSuccess.Should().BeTrue();
        ((object?)visible.Value.ProgramId).Should().NotBeNull();
        ((object?)visible.Value.BundleId).Should().NotBeNull();
        visible.Value.SubscriptionId.Should().Be(fx.SubscriptionId);
        visible.Value.Days.Should().NotBeEmpty();
        var denied = await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(
            fx.SubscriptionId, Day1, Guid.NewGuid()), CancellationToken.None);
        denied.IsFailure.Should().BeTrue();
        denied.Error.Code.Should().Be("family.permission_required");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled, false)]
    [InlineData(SubscriptionStatus.Cancelled, true)]
    [InlineData(SubscriptionStatus.Completed, false)]
    [InlineData(SubscriptionStatus.Expired, false)]
    [InlineData(SubscriptionStatus.Upgraded, false)]
    public async Task Family_terminal_subscription_cannot_edit_legacy_open_day(SubscriptionStatus status, bool manager)
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: true, selectRestaurant: true);
        var sub = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.SubscriptionId);
        sub.Status = status;
        if (manager) (await fx.Db.FamilyGroups.SingleAsync()).ManagerCustomerUserId = fx.CustomerId;
        await fx.Db.SaveChangesAsync();
        var read = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        read.IsSuccess.Should().BeTrue();
        read.Value.Days.Should().OnlyContain(x => !x.CanEdit && x.EditBlockReason == "subscription.service_unavailable");
        var write = await fx.Handlers.Handle(new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA), CancellationToken.None);
        write.IsFailure.Should().BeTrue();
        write.Error.Code.Should().Be("subscription.service_unavailable");
        (await fx.Db.DailyRestaurantSelections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Family_restaurant_selection_uses_its_independent_grant()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: false, selectRestaurant: true);
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Family_calendar_read_does_not_advertise_meal_edit_without_grant()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: false, selectRestaurant: false);
        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.Days.Should().OnlyContain(x => !x.CanEdit && x.EditBlockReason == "family.permission_required");
    }

    [Fact]
    public async Task Family_meal_edit_does_not_grant_confirmation()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: true, selectRestaurant: false);
        var result = await fx.Handlers.Handle(
            new ConfirmCustomerDayBoxCommand(fx.SubscriptionId, Day1), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.permission_required");
    }

    [Fact]
    public async Task EditMealsAndDays_allows_filling_and_completing_a_day_without_allowing_exchange()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: false, selectRestaurant: true, editMealsAndDays: true);
        await FillBox(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var exchange = await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(
            fx.SubscriptionId, Day1, fx.BreakfastAllergen, "breakfast"), CancellationToken.None);
        exchange.IsFailure.Should().BeTrue();
        exchange.Error.Code.Should().Be("family.permission_required");

        var complete = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1), CancellationToken.None);
        complete.IsSuccess.Should().BeTrue();
        complete.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
    }

    [Fact]
    public async Task UnitExchange_allows_replacing_meal_in_open_day_but_not_filling_empty_slot()
    {
        var fx = await SeedAsync();
        await AttachFamilyMemberAsync(fx, editMeals: true, selectRestaurant: true);
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");
        var permissions = await fx.Db.FamilyMemberPermissions.SingleAsync();
        permissions.EditDailyMeals = false;
        permissions.UnitExchange = true;
        await fx.Db.SaveChangesAsync();

        var exchange = await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(
            fx.SubscriptionId, Day1, fx.BreakfastAllergen, "breakfast"), CancellationToken.None);
        exchange.IsSuccess.Should().BeTrue();
        exchange.Value.SelectedMeals.Should().ContainSingle(x => x.MealId == fx.BreakfastAllergen);

        var add = await fx.Handlers.Handle(new SelectCustomerDaySlotMealCommand(
            fx.SubscriptionId, Day1, fx.LunchA, "lunch"), CancellationToken.None);
        add.IsFailure.Should().BeTrue();
        add.Error.Code.Should().Be("family.permission_required");
    }

    private static async Task AttachFamilyMemberAsync(Fixture fx, bool editMeals, bool selectRestaurant,
        bool editMealsAndDays = false, bool unitExchange = false)
    {
        var group = new FamilyGroup { ManagerCustomerUserId = Guid.NewGuid(), Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", Role = FamilyMemberRole.Member,
            RelationshipCode = "child", NutritionalGoalCode = "maintain", MemberSubscriptionId = fx.SubscriptionId };
        group.Members.Add(member);
        var subscription = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.SubscriptionId);
        subscription.FamilyGroupId = group.Id;
        subscription.FamilyMemberId = member.Id;
        var persistedRegionId = await fx.Db.SubscriptionRestaurantSnapshots
            .Where(x => x.SubscriptionId == subscription.Id)
            .Select(x => x.RegionId)
            .FirstAsync();
        var restaurantIds = await fx.Db.SubscriptionRestaurantSnapshots
            .Where(x => x.SubscriptionId == subscription.Id)
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync();
        // Family calendar reads resolve the immutable purchase address. Keep this
        // fixture on the persisted batch-evidence path; a profile/default
        // address fallback would hide malformed-history review conflicts.
        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id,
            PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = $"calendar-{Guid.NewGuid():N}",
            Status = FamilyCheckoutBatchStatus.Paid,
            FinalPrice = subscription.PaidAmount.Amount,
            CurrencyCode = subscription.PaidAmount.CurrencyCode,
            GatewayPaymentId = $"calendar-payment-{Guid.NewGuid():N}",
            GatewayInvoiceId = $"calendar-invoice-{Guid.NewGuid():N}",
            PaidAtUtc = Now,
            CreatedAtUtc = Now
        };
        batch.Lines.Add(new FamilyCheckoutBatchLine
        {
            BatchId = batch.Id,
            FamilyMemberId = member.Id,
            SubscriptionId = subscription.Id,
            CheckoutSelected = true,
            DeliveryAddressId = Guid.NewGuid(),
            AddressLabel = "Fixture",
            AddressLine = "Fixture family delivery address",
            RegionId = persistedRegionId,
            PriceBeforeFamilyDiscount = subscription.PaidAmount.Amount,
            NetPaidAmount = subscription.PaidAmount.Amount,
            CurrencyCode = subscription.PaidAmount.CurrencyCode,
            CreatedAtUtc = Now
        });
        fx.Db.AddRange(group, batch);
        fx.Db.RestaurantServiceRegions.AddRange(restaurantIds.Select(restaurantId => new RestaurantServiceRegion
        {
            RestaurantId = restaurantId,
            RegionId = persistedRegionId,
            IsActive = true
        }));
        fx.Db.AddRange(
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = fx.CustomerId, HasSeparateAccount = true },
            new FamilyMemberPermissions { FamilyMemberId = member.Id, ViewCalendar = true, EditDailyMeals = editMeals,
                EditMealsAndDays = editMealsAndDays, UnitExchange = unitExchange, SelectRestaurant = selectRestaurant });
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetCalendar_returns_filled_and_required_counts()
    {
        var fx = await SeedAsync();
        var breakfast = fx.BreakfastA;
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, breakfast, "breakfast");

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RequiredCount.Should().Be(3);
        var day = result.Value.Days.Single(x => x.ServiceDate == Day1);
        day.FilledCount.Should().Be(1);
        day.RequiredCount.Should().Be(3);
        day.UiStatus.Should().Be(CustomerCalendarUiStatus.Drafting);
        day.CanEdit.Should().BeTrue();
        day.SelectedMeals.Should().ContainSingle(x => x.MealId == breakfast);
    }

    [Fact]
    public async Task GetCalendar_selected_meals_are_slim_without_nutrition_or_ingredients()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var calendar = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        calendar.IsSuccess.Should().BeTrue();
        var summaryMeal = calendar.Value.Days.Single(x => x.ServiceDate == Day1).SelectedMeals.Single();
        summaryMeal.Should().BeOfType<CustomerCalendarMealSummaryDto>();
        summaryMeal.MealId.Should().Be(fx.BreakfastA);
        summaryMeal.NameAr.Should().NotBeNullOrWhiteSpace();
        summaryMeal.Slot.Should().Be("breakfast");
        summaryMeal.IsSelected.Should().BeTrue();

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        var fullMeal = day.Value.SelectedMeals.Single();
        fullMeal.Should().BeOfType<CustomerCalendarMealDto>();
        fullMeal.Calories.Should().Be(400);
        fullMeal.DescriptionAr.Should().NotBeNullOrWhiteSpace();
        fullMeal.PresentAllergenCodes.Should().NotBeNull();
        fullMeal.Ingredients.Should().NotBeNull();
        day.Value.NutritionTotals.Calories.Should().Be(400);
        day.Value.NutritionTotals.ProteinG.Should().Be(30);
        day.Value.NutritionTotals.CarbsG.Should().Be(20);
        day.Value.NutritionTotals.FatG.Should().Be(10);
    }

    [Fact]
    public async Task GetDay_nutrition_totals_sum_all_selected_meals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");
        await SelectSlot(fx, Day1, fx.LunchA, "lunch");

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);

        day.IsSuccess.Should().BeTrue();
        day.Value.FilledCount.Should().Be(2);
        day.Value.NutritionTotals.Calories.Should().Be(800);
        day.Value.NutritionTotals.ProteinG.Should().Be(60);
        day.Value.NutritionTotals.CarbsG.Should().Be(40);
        day.Value.NutritionTotals.FatG.Should().Be(20);
    }

    [Fact]
    public async Task GetDay_hides_fulfillment_preselected_restaurant_until_customer_picks()
    {
        var fx = await SeedAsync();
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1);
        day.RestaurantId = fx.RestaurantA;
        await fx.Db.SaveChangesAsync();

        var beforePick = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        beforePick.IsSuccess.Should().BeTrue();
        beforePick.Value.RestaurantId.Should().BeNull();
        beforePick.Value.RestaurantNameAr.Should().BeNull();
        beforePick.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Empty);

        var afterPick = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);
        afterPick.IsSuccess.Should().BeTrue();
        afterPick.Value.RestaurantId.Should().Be(fx.RestaurantA);
        afterPick.Value.RestaurantNameAr.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDay_unknown_date_fails()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new GetCustomerCalendarDayQuery(fx.SubscriptionId, new DateOnly(2099, 1, 1)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.DayUnavailable);
    }

    [Fact]
    public async Task GetDay_returns_restaurant_rating_and_delivery_window_when_selected()
    {
        var fx = await SeedAsync();
        fx.Db.RestaurantRatings.AddRange(
            new RestaurantRating { RestaurantId = fx.RestaurantA, CustomerUserId = Guid.NewGuid(), Score = 5 },
            new RestaurantRating { RestaurantId = fx.RestaurantA, CustomerUserId = Guid.NewGuid(), Score = 4 }
        );
        fx.Db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = fx.RestaurantA,
            RegionId = Guid.NewGuid(),
            IsActive = true,
            DeliveryWindowStart = new TimeOnly(18, 0),
            DeliveryWindowEnd = new TimeOnly(19, 0)
        });
        await fx.Db.SaveChangesAsync();

        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        day.Value.RestaurantId.Should().Be(fx.RestaurantA);
        day.Value.RestaurantRating.Should().Be(4.5m);
        day.Value.RestaurantRatingCount.Should().Be(2);
        day.Value.DeliveryWindowStart.Should().Be(new TimeOnly(18, 0));
        day.Value.DeliveryWindowEnd.Should().Be(new TimeOnly(19, 0));
        day.Value.DeliveryTimeText.Should().Contain("6:00").And.Contain("7:00");
        day.Value.Tier.Should().Be(RestaurantTier.Platinum);
        day.Value.TierNameAr.Should().Be("بلاتينيوم");
        day.Value.ProgramNameAr.Should().Be("وزن");
        day.Value.ProgramName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetDayRestaurants_returns_rating_and_delivery_window_for_all_restaurants()
    {
        var fx = await SeedAsync();
        fx.Db.RestaurantRatings.Add(new RestaurantRating { RestaurantId = fx.RestaurantA, CustomerUserId = Guid.NewGuid(), Score = 4 });
        fx.Db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = fx.RestaurantA,
            RegionId = Guid.NewGuid(),
            IsActive = true,
            DeliveryWindowStart = new TimeOnly(8, 0),
            DeliveryWindowEnd = new TimeOnly(14, 0)
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var restA = result.Value.Restaurants.Single(r => r.RestaurantId == fx.RestaurantA);
        restA.Rating.Should().Be(4.0m);
        restA.RatingCount.Should().Be(1);
        restA.DeliveryWindowStart.Should().Be(new TimeOnly(8, 0));
        restA.DeliveryWindowEnd.Should().Be(new TimeOnly(14, 0));
        restA.DeliveryTimeText.Should().NotBeNullOrWhiteSpace();
        restA.Tier.Should().Be(RestaurantTier.Platinum);
        restA.TierNameAr.Should().Be("بلاتينيوم");
        restA.TierName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetRestaurantMeals_puts_core_ingredients_on_restaurant_not_each_meal()
    {
        var fx = await SeedAsync();
        var ingredient = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = fx.RestaurantA,
            Code = "CHICKEN",
            NameAr = "دجاج",
            NameEn = "Chicken",
            OriginCountryCode = "KW",
            IsActive = true,
            IsCore = true,
            RestaurantIconUrl = "uploads/restaurant/ingredients/chicken-core.png",
            MealIconUrl = "uploads/restaurant/ingredients/chicken.png"
        };
        var sideIngredient = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = fx.RestaurantA,
            Code = "RICE",
            NameAr = "أرز",
            NameEn = "Rice",
            OriginCountryCode = "KW",
            IsActive = true,
            IsCore = false,
            MealIconUrl = "uploads/restaurant/ingredients/rice.png"
        };
        fx.Db.Ingredients.Add(ingredient);
        fx.Db.Ingredients.Add(sideIngredient);
        await fx.Db.SaveChangesAsync();
        var ingredientId = ingredient.Id;
        var sideId = sideIngredient.Id;
        var daySnap = await fx.Db.SubscriptionRestaurantDaySnapshots
            .FirstAsync(x => x.SubscriptionId == fx.SubscriptionId && x.RestaurantId == fx.RestaurantA && x.ServiceDate == Day1);
        daySnap.MealsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                mealId = fx.BreakfastA,
                nameAr = "فطور",
                nameEn = "Breakfast",
                descriptionAr = "فطور",
                descriptionEn = "Breakfast",
                slot = "breakfast",
                sortOrder = 1,
                imageUrl = (string?)null,
                calories = 400,
                proteinG = 30,
                carbsG = 20,
                fatG = 10,
                presentAllergenCodes = Array.Empty<string>(),
                mayContainAllergenCodes = Array.Empty<string>(),
                dietaryPreferenceCodes = Array.Empty<string>(),
                ingredients = new object[]
                {
                    new
                    {
                        id = ingredientId,
                        nameAr = "دجاج",
                        nameEn = "Chicken",
                        originCountryCode = "KW",
                        supplierName = (string?)null,
                        isLocal = true,
                        isHalalCertified = true,
                        allergenCodes = Array.Empty<string>(),
                        isOriginVisibleToCustomers = true,
                        isCore = true,
                        mealIconUrl = "uploads/restaurant/ingredients/chicken.png"
                    },
                    new
                    {
                        id = sideId,
                        nameAr = "أرز",
                        nameEn = "Rice",
                        originCountryCode = "KW",
                        supplierName = (string?)null,
                        isLocal = true,
                        isHalalCertified = true,
                        allergenCodes = Array.Empty<string>(),
                        isOriginVisibleToCustomers = true,
                        isCore = false,
                        mealIconUrl = "uploads/restaurant/ingredients/rice.png"
                    }
                }
            }
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CoreIngredients.Should().ContainSingle(x => x.Id == ingredientId);
        var core = result.Value.CoreIngredients.Single();
        core.IconUrl.Should().Contain("chicken-core.png");
        core.OriginCountryCode.Should().Be("KW");
        var meal = result.Value.Slots.SelectMany(x => x.Meals).Single(x => x.MealId == fx.BreakfastA);
        meal.Ingredients.Should().ContainSingle(x => x.Id == sideId);
        meal.Ingredients.Should().NotContain(x => x.Id == ingredientId);
    }

    [Fact]
    public async Task SelectRestaurant_same_restaurant_again_preserves_selected_meals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var again = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);

        again.IsSuccess.Should().BeTrue();
        again.Value.RestaurantId.Should().Be(fx.RestaurantA);
        again.Value.SelectedMeals.Should().ContainSingle(x => x.MealId == fx.BreakfastA);
        again.Value.FilledCount.Should().Be(1);

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        day.Value.SelectedMeals.Should().ContainSingle(x => x.MealId == fx.BreakfastA);
        day.Value.SelectedMeals.Single().NameAr.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetRestaurantMeals_filters_to_subscription_bundle_slots_only()
    {
        var fx = await SeedAsync();
        var snackId = Guid.NewGuid();
        foreach (var daySnap in fx.Db.SubscriptionRestaurantDaySnapshots.Where(x => x.SubscriptionId == fx.SubscriptionId && x.RestaurantId == fx.RestaurantA))
        {
            daySnap.SlotRequirementsJson = "[{\"slot\":\"lunch\",\"count\":1},{\"slot\":\"snack\",\"count\":1}]";
            daySnap.MealsJson = JsonSerializer.Serialize(new object[]
            {
                new
                {
                    mealId = fx.BreakfastA,
                    nameAr = "فطور",
                    nameEn = "Breakfast",
                    descriptionAr = "فطور",
                    descriptionEn = "Breakfast",
                    slot = "breakfast",
                    sortOrder = 1,
                    imageUrl = (string?)null,
                    calories = 400,
                    proteinG = 30,
                    carbsG = 20,
                    fatG = 10,
                    presentAllergenCodes = Array.Empty<string>(),
                    mayContainAllergenCodes = Array.Empty<string>(),
                    dietaryPreferenceCodes = Array.Empty<string>(),
                    ingredients = Array.Empty<object>()
                },
                new
                {
                    mealId = fx.LunchA,
                    nameAr = "غداء",
                    nameEn = "Lunch",
                    descriptionAr = "غداء",
                    descriptionEn = "Lunch",
                    slot = "lunch",
                    sortOrder = 2,
                    imageUrl = (string?)null,
                    calories = 400,
                    proteinG = 30,
                    carbsG = 20,
                    fatG = 10,
                    presentAllergenCodes = Array.Empty<string>(),
                    mayContainAllergenCodes = Array.Empty<string>(),
                    dietaryPreferenceCodes = Array.Empty<string>(),
                    ingredients = Array.Empty<object>()
                },
                new
                {
                    mealId = fx.DinnerA,
                    nameAr = "عشاء",
                    nameEn = "Dinner",
                    descriptionAr = "عشاء",
                    descriptionEn = "Dinner",
                    slot = "dinner",
                    sortOrder = 3,
                    imageUrl = (string?)null,
                    calories = 400,
                    proteinG = 30,
                    carbsG = 20,
                    fatG = 10,
                    presentAllergenCodes = Array.Empty<string>(),
                    mayContainAllergenCodes = Array.Empty<string>(),
                    dietaryPreferenceCodes = Array.Empty<string>(),
                    ingredients = Array.Empty<object>()
                },
                new
                {
                    mealId = snackId,
                    nameAr = "سناك",
                    nameEn = "Snack",
                    descriptionAr = "سناك",
                    descriptionEn = "Snack",
                    slot = "snack",
                    sortOrder = 4,
                    imageUrl = (string?)null,
                    calories = 200,
                    proteinG = 10,
                    carbsG = 20,
                    fatG = 5,
                    presentAllergenCodes = Array.Empty<string>(),
                    mayContainAllergenCodes = Array.Empty<string>(),
                    dietaryPreferenceCodes = Array.Empty<string>(),
                    ingredients = Array.Empty<object>()
                }
            });
        }

        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Slots.Select(x => x.Slot.ToLowerInvariant()).Should().BeEquivalentTo(["lunch", "snack"]);
        result.Value.RequiredCount.Should().Be(2);
        var allMeals = result.Value.Slots.SelectMany(x => x.Meals).ToList();
        allMeals.Select(x => x.Slot.ToLowerInvariant()).Should().BeEquivalentTo(["lunch", "snack"]);
        allMeals.Should().NotContain(x => x.Slot.Equals("breakfast", StringComparison.OrdinalIgnoreCase));
        allMeals.Should().NotContain(x => x.Slot.Equals("dinner", StringComparison.OrdinalIgnoreCase));
        allMeals.Should().Contain(x => x.MealId == fx.LunchA);
        allMeals.Should().Contain(x => x.MealId == snackId);
    }

    [Fact]
    public async Task GetRestaurantMeals_paginates_meals_per_slot()
    {
        var fx = await SeedAsync();
        var meal2 = Guid.NewGuid();
        var meal3 = Guid.NewGuid();
        foreach (var daySnap in fx.Db.SubscriptionRestaurantDaySnapshots.Where(x => x.SubscriptionId == fx.SubscriptionId && x.RestaurantId == fx.RestaurantA))
        {
            daySnap.SlotRequirementsJson = "[{\"slot\":\"breakfast\",\"count\":1}]";
            daySnap.MealsJson = JsonSerializer.Serialize(new object[]
            {
                new { mealId = fx.BreakfastA, nameAr = "فطور 1", nameEn = "Breakfast 1", descriptionAr = "", descriptionEn = "", slot = "breakfast", sortOrder = 1, calories = 300, proteinG = 20, carbsG = 20, fatG = 5, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() },
                new { mealId = meal2, nameAr = "فطور 2", nameEn = "Breakfast 2", descriptionAr = "", descriptionEn = "", slot = "breakfast", sortOrder = 2, calories = 350, proteinG = 25, carbsG = 25, fatG = 6, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() },
                new { mealId = meal3, nameAr = "فطور 3", nameEn = "Breakfast 3", descriptionAr = "", descriptionEn = "", slot = "breakfast", sortOrder = 3, calories = 400, proteinG = 30, carbsG = 30, fatG = 7, presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() }
            });
        }
        await fx.Db.SaveChangesAsync();

        var page1 = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA, Page: 1, PageSize: 2),
            CancellationToken.None);

        page1.IsSuccess.Should().BeTrue();
        var slotPage1 = page1.Value.Slots.Single();
        slotPage1.Slot.Should().Be("breakfast");
        slotPage1.Page.Should().Be(1);
        slotPage1.PageSize.Should().Be(2);
        slotPage1.TotalCount.Should().Be(3);
        slotPage1.TotalPages.Should().Be(2);
        slotPage1.HasNextPage.Should().BeTrue();
        slotPage1.HasPreviousPage.Should().BeFalse();
        slotPage1.Meals.Should().HaveCount(2);
        slotPage1.Meals[0].MealId.Should().Be(fx.BreakfastA);
        slotPage1.Meals[1].MealId.Should().Be(meal2);

        var page2 = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA, Page: 2, PageSize: 2),
            CancellationToken.None);

        page2.IsSuccess.Should().BeTrue();
        var slotPage2 = page2.Value.Slots.Single();
        slotPage2.Page.Should().Be(2);
        slotPage2.TotalCount.Should().Be(3);
        slotPage2.TotalPages.Should().Be(2);
        slotPage2.HasNextPage.Should().BeFalse();
        slotPage2.HasPreviousPage.Should().BeTrue();
        slotPage2.Meals.Should().HaveCount(1);
        slotPage2.Meals[0].MealId.Should().Be(meal3);
    }

    [Fact]
    public async Task GetRestaurantMeals_with_slot_filter_returns_only_requested_slot()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA, Slot: "breakfast"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Slots.Should().HaveCount(1);
        result.Value.Slots.Single().Slot.Should().Be("breakfast");
    }

    [Fact]
    public async Task GetDay_calculates_macronutrient_percentages_for_nutrition_totals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        day.Value.NutritionTotals.Calories.Should().Be(400);
        day.Value.NutritionTotals.ProteinG.Should().Be(30);
        day.Value.NutritionTotals.CarbsG.Should().Be(20);
        day.Value.NutritionTotals.FatG.Should().Be(10);
        day.Value.NutritionTotals.ProteinPercentage.Should().Be(41.4m);
        day.Value.NutritionTotals.CarbsPercentage.Should().Be(27.6m);
        day.Value.NutritionTotals.FatPercentage.Should().Be(31.0m);
        day.Value.NutritionTotals.CaloriesPercentage.Should().Be(0m, "no nutrition target is configured in this fixture");
        day.Value.ProteinPercentage.Should().Be(41.4m);
        day.Value.CarbsPercentage.Should().Be(27.6m);
        day.Value.FatPercentage.Should().Be(31.0m);
        day.Value.CaloriesPercentage.Should().Be(0m);
    }

    [Fact]
    public async Task GetDay_populates_allergens_in_selected_meals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastAllergen, "breakfast");

        var day = await fx.Handlers.Handle(new GetCustomerCalendarDayQuery(fx.SubscriptionId, Day1), CancellationToken.None);
        day.IsSuccess.Should().BeTrue();
        var meal = day.Value.SelectedMeals.Single();
        meal.Allergens.Should().Contain("لاكتوز");
        meal.AllergensAr.Should().Contain("لاكتوز");
        meal.AllergensEn.Should().Contain("Lactose");
        meal.AllergenDetails.Should().ContainSingle(x => x.Code == "MILK" && x.NameAr == "لاكتوز" && x.NameEn == "Lactose");
    }

    [Fact]
    public async Task GetDayRestaurants_marks_exhausted_when_used_at_limit()
    {
        var fx = await SeedAsync();
        await MarkCompleted(fx, Day1, fx.RestaurantA);
        await MarkCompleted(fx, Day2, fx.RestaurantA);
        await MarkCompleted(fx, Day3, fx.RestaurantA);

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day4), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Limit.Should().Be(3);
        var restaurantA = result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantA);
        restaurantA.UsedCount.Should().Be(3);
        restaurantA.IsQuotaExhausted.Should().BeTrue();
        restaurantA.IsEnabled.Should().BeFalse();
        restaurantA.DisabledReason.Should().Be(MessageCodes.Calendar.QuotaExhausted);
        result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantB).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SelectRestaurant_from_pool_clears_meals()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantB),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        await fx.Db.SaveChangesAsync();

        result.Value.RestaurantId.Should().Be(fx.RestaurantB);
        result.Value.FilledCount.Should().Be(0);
        result.Value.Status.Should().Be(SubscriptionDayStatus.Open);
        (await fx.Db.DailyMealSelections.CountAsync(x => x.SubscriptionDayId == result.Value.Id && !x.IsDeleted)).Should().Be(0);
    }

    [Fact]
    public async Task SelectRestaurant_outside_pool_fails()
    {
        var fx = await SeedAsync();

        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.RestaurantNotInPool);
    }

    [Fact]
    public async Task SelectRestaurant_when_locked_fails()
    {
        var fx = await SeedAsync(now: Deadline.AddSeconds(1));

        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, Day1, fx.RestaurantA),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.SelectionLocked);
    }

    [Fact]
    public async Task SelectSlotMeal_keeps_day_in_draft()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(1);
        result.Value.Status.Should().Be(SubscriptionDayStatus.Open);
        result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Drafting);
    }

    [Fact]
    public async Task SelectSlotMeal_revives_soft_deleted_selection_for_same_meal()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var existing = await fx.Db.DailyMealSelections.SingleAsync(x => x.MealId == fx.BreakfastA);
        existing.IsDeleted = true;
        existing.DeletedAtUtc = Now;
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();

        var rows = await fx.Db.DailyMealSelections.IgnoreQueryFilters()
            .Where(x => x.SubscriptionDayId == result.Value.Id && x.MealId == fx.BreakfastA)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].IsDeleted.Should().BeFalse();
        result.Value.FilledCount.Should().Be(1);
        result.Value.SelectedMeals.Should().ContainSingle(x => x.MealId == fx.BreakfastA);
    }

    [Fact]
    public async Task SelectSlotMeal_rejects_allergy_conflict()
    {
        var fx = await SeedAsync(seedMilkAllergy: true);
        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastAllergen, "breakfast"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.AllergyConflict);
    }

    [Fact]
    public async Task CompleteBox_fails_when_incomplete()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var result = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.BoxIncomplete);
        (await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == fx.SubscriptionId && x.ServiceDate == Day1))
            .Status.Should().Be(SubscriptionDayStatus.Open);
    }

    [Fact]
    public async Task CompleteBox_succeeds_when_all_slots_filled()
    {
        var fx = await SeedAsync();
        await FillBox(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);

        var result = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.AwaitingRestaurant);
        result.Value.FilledCount.Should().Be(3);
        result.Value.IsAutoSelected.Should().BeFalse();
        await fx.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task CompleteBox_is_idempotent_when_already_complete()
    {
        var fx = await SeedAsync();
        await FillBox(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var first = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1, "box-1"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Code : "");
        await fx.Db.SaveChangesAsync();

        var again = await fx.Handlers.Handle(
            new CompleteCustomerDayBoxCommand(fx.SubscriptionId, Day1, "box-1"),
            CancellationToken.None);

        again.IsSuccess.Should().BeTrue();
        again.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        (await fx.Db.DailyMealSelections.CountAsync(x => x.SubscriptionDayId == again.Value.Id)).Should().Be(3);
    }

    [Fact]
    public async Task AutoSelect_fills_full_box_skipping_allergy_meals()
    {
        var fx = await SeedAsync(seedMilkAllergy: true);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.RequiredCount.Should().Be(3);
        result.Value.IsAutoSelected.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionDayStatus.AutoSelected);
        result.Value.SelectedMeals.Should().NotContain(x => x.MealId == fx.BreakfastAllergen);
        result.Value.SelectedMeals.Select(x => x.Slot.ToLowerInvariant())
            .Should().BeEquivalentTo(["breakfast", "lunch", "dinner"]);
    }

    [Fact]
    public async Task AutoSelect_skips_restaurant_at_repetition_limit_when_alternative_exists()
    {
        var fx = await SeedAsync();
        await MarkCompleted(fx, Day2, fx.RestaurantA);
        await MarkCompleted(fx, Day3, fx.RestaurantA);
        await MarkCompleted(fx, Day4, fx.RestaurantA);

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestaurantId.Should().Be(fx.RestaurantB);
        result.Value.FilledCount.Should().Be(3);
    }

    [Fact]
    public async Task AutoSelect_never_leaves_a_partial_box()
    {
        var fx = await SeedAsync();
        await SelectRestaurant(fx, Day1, fx.RestaurantA);
        await SelectSlot(fx, Day1, fx.BreakfastA, "breakfast");

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(result.Value.RequiredCount);
        result.Value.FilledCount.Should().Be(3);
    }

    [Fact]
    public async Task GetDayRestaurants_filters_by_query()
    {
        var fx = await SeedAsync();

        var resultA = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, "مطعم أ"),
            CancellationToken.None);

        resultA.IsSuccess.Should().BeTrue();
        resultA.Value.Restaurants.Should().HaveCount(1);
        resultA.Value.Restaurants[0].RestaurantId.Should().Be(fx.RestaurantA);

        var resultB = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, "Restaurant B"),
            CancellationToken.None);

        resultB.IsSuccess.Should().BeTrue();
        resultB.Value.Restaurants.Should().HaveCount(1);
        resultB.Value.Restaurants[0].RestaurantId.Should().Be(fx.RestaurantB);

        var resultNone = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1, "NonExistingRestaurant"),
            CancellationToken.None);

        resultNone.IsSuccess.Should().BeTrue();
        resultNone.Value.Restaurants.Should().BeEmpty();
    }

    [Fact]
    public async Task Family_subscription_without_active_group_is_rejected()
    {
        var fx = await SeedAsync(family: true);

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.GroupNotActive);
    }

    [Fact]
    public async Task Lock_after_deadline_auto_selects_then_locks()
    {
        var fx = await SeedAsync(now: Deadline.AddMinutes(1));

        var result = await fx.Handlers.Handle(
            new AutoSelectCustomerDayBoxCommand(fx.SubscriptionId, Day1, LockAfter: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FilledCount.Should().Be(3);
        result.Value.Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        result.Value.CanEdit.Should().BeFalse();
        result.Value.UiStatus.Should().Be(CustomerCalendarUiStatus.Locked);
        result.Value.IsAutoSelected.Should().BeTrue();
    }

    private static async Task FillBox(Fixture fx, DateOnly date, Guid restaurantId, Guid breakfast, Guid lunch, Guid dinner)
    {
        await SelectRestaurant(fx, date, restaurantId);
        await SelectSlot(fx, date, breakfast, "breakfast");
        await SelectSlot(fx, date, lunch, "lunch");
        await SelectSlot(fx, date, dinner, "dinner");
    }

    private static async Task SelectRestaurant(Fixture fx, DateOnly date, Guid restaurantId)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDayRestaurantCommand(fx.SubscriptionId, date, restaurantId),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task SelectSlot(Fixture fx, DateOnly date, Guid mealId, string slot)
    {
        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, date, mealId, slot),
            CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task MarkCompleted(Fixture fx, DateOnly date, Guid restaurantId)
    {
        var meals = restaurantId == fx.RestaurantA
            ? (fx.BreakfastA, fx.LunchA, fx.DinnerA)
            : (fx.BreakfastB, fx.LunchB, fx.DinnerB);
        await FillBox(fx, date, restaurantId, meals.Item1, meals.Item2, meals.Item3);
        var result = await fx.Handlers.Handle(new CompleteCustomerDayBoxCommand(fx.SubscriptionId, date), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
        await fx.Db.SaveChangesAsync();
    }

    private static async Task<Fixture> SeedAsync(
        bool seedMilkAllergy = false,
        bool family = false,
        DateTime? now = null)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var program = new Program
        {
            Code = "WL",
            NameAr = "وزن",
            NameEn = "Weight",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "3M",
            NameAr = "3 وجبات",
            NameEn = "3 meals",
            MealsPerDay = 3,
            BreakfastCount = 1,
            LunchCount = 1,
            DinnerCount = 1,
            SnackCount = 0,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var subscription = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 6,
            IncludeFriday = false,
            StartDate = Day1,
            EndDate = Day4,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(25m, "KWD"),
            FulfillmentCompletedAtUtc = Now,
            FamilyGroupId = family ? Guid.NewGuid() : null
        };

        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();
        var breakfastA = Guid.NewGuid();
        var lunchA = Guid.NewGuid();
        var dinnerA = Guid.NewGuid();
        var breakfastAllergen = Guid.NewGuid();
        var breakfastB = Guid.NewGuid();
        var lunchB = Guid.NewGuid();
        var dinnerB = Guid.NewGuid();
        var versionA = Guid.NewGuid();
        var versionB = Guid.NewGuid();

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscription.Id,
            RestaurantCount = 2,
            EligibilityCheckedAtUtc = Now
        };
        var snapA = FrozenRestaurant(pool.Id, subscription, restaurantA, "مطعم أ", "Restaurant A", true);
        var snapB = FrozenRestaurant(pool.Id, subscription, restaurantB, "مطعم ب", "Restaurant B", false);

        db.AddRange(program, bundle, subscription, pool, snapA, snapB);
        if (seedMilkAllergy)
        {
            db.Add(new CustomerAllergyPreference
            {
                CustomerUserId = customerId,
                AllergenCode = "MILK",
                AllergenNameAr = "حليب",
                AllergenNameEn = "Milk",
                IsDislike = false
            });
        }

        foreach (var date in new[] { Day1, Day2, Day3, Day4 })
        {
            db.Add(new SubscriptionDay
            {
                SubscriptionId = subscription.Id,
                ServiceDate = date,
                Status = SubscriptionDayStatus.Open,
                SelectionDeadlineUtc = Deadline
            });
            db.Add(RestaurantDay(snapA.Id, subscription.Id, restaurantA, date, versionA,
                MealsJson(breakfastA, lunchA, dinnerA, breakfastAllergen, "A")));
            db.Add(RestaurantDay(snapB.Id, subscription.Id, restaurantB, date, versionB,
                MealsJson(breakfastB, lunchB, dinnerB, allergenBreakfast: null, suffix: "B")));
        }

        await db.SaveChangesAsync();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now ?? Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(now ?? Now));

        // Auto-selection can switch restaurants; the write path now requires the capacity
        // transfer service (fail-closed without it). These tests assert repetition/allergy
        // behavior, not capacity accounting, so a succeeding stub models the configured service.
        var transfer = Substitute.For<ICapacityTransferService>();
        transfer.TransferCapacityAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid?>(),
                Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MealMate.Application.Common.Models.Result.Success());
        var engine = new CustomerCalendarEngine(db, user, clock, new RestaurantDiversificationCalculator(), capacityTransferService: transfer);
        var handlers = new CustomerCalendarHandlers(engine);
        return new Fixture(db, handlers, subscription.Id, customerId, restaurantA, restaurantB, breakfastA, lunchA, dinnerA,
            breakfastAllergen, breakfastB, lunchB, dinnerB);
    }

    private static SubscriptionRestaurantSnapshot FrozenRestaurant(
        Guid poolId, Subscription subscription, Guid restaurantId, string nameAr, string nameEn, bool initial)
        => new()
        {
            PoolSnapshotId = poolId,
            SubscriptionId = subscription.Id,
            RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId,
            BundleId = subscription.BundleId,
            RegionId = Guid.NewGuid(),
            Tier = RestaurantTier.Platinum,
            NameAr = nameAr,
            NameEn = nameEn,
            IsInitiallySelected = initial,
            OfferingSnapshotJson = "{}",
            CapturedAtUtc = Now
        };

    private static SubscriptionRestaurantDaySnapshot RestaurantDay(
        Guid restaurantSnapshotId, Guid subscriptionId, Guid restaurantId, DateOnly date, Guid versionId, string mealsJson)
        => new()
        {
            RestaurantSnapshotId = restaurantSnapshotId,
            SubscriptionId = subscriptionId,
            RestaurantId = restaurantId,
            ServiceDate = date,
            MenuPlanVersionId = versionId,
            SlotRequirementsJson = "[{\"slot\":\"breakfast\",\"count\":1},{\"slot\":\"lunch\",\"count\":1},{\"slot\":\"dinner\",\"count\":1}]",
            MealsJson = mealsJson,
            ContentHash = "hash",
            CapturedAtUtc = Now
        };

    private static string MealsJson(Guid breakfast, Guid lunch, Guid dinner, Guid? allergenBreakfast, string suffix)
    {
        var meals = new List<object>
        {
            Meal(breakfast, $"فطور {suffix}", $"Breakfast {suffix}", "breakfast", 1, []),
            Meal(lunch, $"غداء {suffix}", $"Lunch {suffix}", "lunch", 2, []),
            Meal(dinner, $"عشاء {suffix}", $"Dinner {suffix}", "dinner", 3, [])
        };
        if (allergenBreakfast is Guid allergen)
            meals.Insert(0, Meal(allergen, "فطور حليب", "Milk breakfast", "breakfast", 0, new[] { "MILK" }));
        return JsonSerializer.Serialize(meals);
    }

    private static object Meal(Guid id, string ar, string en, string slot, int sort, string[] allergens) => new
    {
        mealId = id,
        nameAr = ar,
        nameEn = en,
        descriptionAr = ar,
        descriptionEn = en,
        slot,
        sortOrder = sort,
        imageUrl = (string?)null,
        calories = 400,
        proteinG = 30,
        carbsG = 20,
        fatG = 10,
        presentAllergenCodes = allergens,
        mayContainAllergenCodes = Array.Empty<string>(),
        dietaryPreferenceCodes = Array.Empty<string>(),
        ingredients = Array.Empty<object>()
    };

    // ---------- Calendar unread notifications indicator ----------

    [Fact]
    public async Task GetCalendar_without_notifications_reports_no_unread()
    {
        var fx = await SeedAsync();

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasUnreadNotifications.Should().BeFalse();
        result.Value.UnreadNotificationsCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCalendar_with_seeded_unread_reports_true_and_one()
    {
        var fx = await SeedAsync();
        await SeedNotificationAsync(fx, fx.CustomerId, read: false);

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.HasUnreadNotifications.Should().BeTrue();
        result.Value.UnreadNotificationsCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCalendar_excludes_read_and_other_users_notifications()
    {
        var fx = await SeedAsync();
        await SeedNotificationAsync(fx, fx.CustomerId, read: true);
        await SeedNotificationAsync(fx, Guid.NewGuid(), read: false);

        var result = await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        result.Value.HasUnreadNotifications.Should().BeFalse();
        result.Value.UnreadNotificationsCount.Should().Be(0);
    }

    [Fact]
    public async Task GetCalendar_twice_never_marks_notifications_read()
    {
        var fx = await SeedAsync();
        var id = await SeedNotificationAsync(fx, fx.CustomerId, read: false);

        await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);
        await fx.Handlers.Handle(new GetCustomerCalendarQuery(fx.SubscriptionId), CancellationToken.None);

        (await fx.Db.CustomerNotifications.AsNoTracking().SingleAsync(x => x.Id == id)).ReadAtUtc.Should().BeNull();
    }

    // ---------- Ratings: null without reviews, real average with them ----------

    [Fact]
    public async Task GetDayRestaurants_reports_null_rating_without_reviews()
    {
        var fx = await SeedAsync();

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Restaurants.Should().OnlyContain(x => x.Rating == null && x.RatingCount == 0);
        result.Value.Restaurants.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDayRestaurants_reports_real_average_and_count_with_reviews()
    {
        var fx = await SeedAsync();
        fx.Db.RestaurantRatings.Add(new RestaurantRating
        { RestaurantId = fx.RestaurantA, CustomerUserId = fx.CustomerId, Score = 5 });
        fx.Db.RestaurantRatings.Add(new RestaurantRating
        { RestaurantId = fx.RestaurantA, CustomerUserId = Guid.NewGuid(), Score = 3 });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(new GetCustomerDayRestaurantsQuery(fx.SubscriptionId, Day1), CancellationToken.None);

        var rated = result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantA);
        rated.Rating.Should().Be(4.0m);
        rated.RatingCount.Should().Be(2);
        var unrated = result.Value.Restaurants.Single(x => x.RestaurantId == fx.RestaurantB);
        unrated.Rating.Should().BeNull();
        unrated.RatingCount.Should().Be(0);
    }

    // ---------- Allergens: meal + ingredient codes combine; filter rejects conflicts ----------

    [Fact]
    public async Task RestaurantMeals_allergens_combine_meal_and_ingredient_codes()
    {
        var fx = await SeedAsync();
        var snapshot = await fx.Db.SubscriptionRestaurantDaySnapshots
            .SingleAsync(x => x.RestaurantId == fx.RestaurantA && x.ServiceDate == Day1);
        snapshot.MealsJson = JsonSerializer.Serialize(new List<object>
        {
            new
            {
                mealId = fx.BreakfastA,
                nameAr = "فطور بيض",
                nameEn = "Egg breakfast",
                descriptionAr = "وجبة",
                descriptionEn = "Meal",
                slot = "breakfast",
                sortOrder = 1,
                imageUrl = (string?)null,
                calories = 300m,
                proteinG = 20m,
                carbsG = 20m,
                fatG = 5m,
                presentAllergenCodes = new[] { "EGGS" },
                mayContainAllergenCodes = Array.Empty<string>(),
                dietaryPreferenceCodes = Array.Empty<string>(),
                ingredients = new object[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        nameAr = "حليب",
                        nameEn = "Milk",
                        originCountryCode = (string?)null,
                        supplierName = (string?)null,
                        isLocal = false,
                        isHalalCertified = true,
                        allergenCodes = new[] { "MILK" },
                        isCore = false,
                        iconUrl = (string?)null
                    }
                }
            },
            Meal(fx.LunchA, "غداء", "Lunch", "lunch", 2),
            Meal(fx.DinnerA, "عشاء", "Dinner", "dinner", 3)
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(
            new GetCustomerDayRestaurantMealsQuery(fx.SubscriptionId, Day1, fx.RestaurantA, "breakfast", 1, 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var meal = result.Value.Slots.Single().Meals.Single(x => x.MealId == fx.BreakfastA);
        meal.PresentAllergenCodes.Should().Contain("EGGS");
        meal.Ingredients.Single().AllergenCodes.Should().Contain("MILK");
        meal.Allergens.Should().BeEquivalentTo(new[] { "بيض", "لاكتوز" });
        meal.AllergensEn.Should().BeEquivalentTo(new[] { "Eggs", "Lactose" });
        meal.AllergenDetails.Should().Contain(x => x.Code == "EGGS" && x.NameEn == "Eggs");
        meal.AllergenDetails.Should().Contain(x => x.Code == "MILK" && x.NameEn == "Lactose");
    }

    [Fact]
    public async Task SelectSlotMeal_rejects_allergy_conflict_from_ingredient_codes()
    {
        var fx = await SeedAsync(seedMilkAllergy: true);
        await SelectRestaurant(fx, Day1, fx.RestaurantA);

        // Swap in a meal with no meal-level allergen whose ingredient carries MILK only.
        var snapshot = await fx.Db.SubscriptionRestaurantDaySnapshots
            .SingleAsync(x => x.RestaurantId == fx.RestaurantA && x.ServiceDate == Day1);
        snapshot.MealsJson = JsonSerializer.Serialize(new List<object>
        {
            new
            {
                mealId = fx.BreakfastA,
                nameAr = "فطور آمن",
                nameEn = "Safe breakfast",
                descriptionAr = "وجبة",
                descriptionEn = "Meal",
                slot = "breakfast",
                sortOrder = 1,
                imageUrl = (string?)null,
                calories = 300m,
                proteinG = 20m,
                carbsG = 20m,
                fatG = 5m,
                presentAllergenCodes = Array.Empty<string>(),
                mayContainAllergenCodes = Array.Empty<string>(),
                dietaryPreferenceCodes = Array.Empty<string>(),
                ingredients = new object[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        nameAr = "زبادي",
                        nameEn = "Yogurt",
                        originCountryCode = (string?)null,
                        supplierName = (string?)null,
                        isLocal = false,
                        isHalalCertified = true,
                        allergenCodes = new[] { "MILK" },
                        isCore = false,
                        iconUrl = (string?)null
                    }
                }
            },
            Meal(fx.LunchA, "غداء", "Lunch", "lunch", 2),
            Meal(fx.DinnerA, "عشاء", "Dinner", "dinner", 3)
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(
            new SelectCustomerDaySlotMealCommand(fx.SubscriptionId, Day1, fx.BreakfastA, "breakfast"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Calendar.AllergyConflict);
    }

    private static object Meal(Guid id, string ar, string en, string slot, int sort) => new
    {
        mealId = id,
        nameAr = ar,
        nameEn = en,
        descriptionAr = ar,
        descriptionEn = en,
        slot,
        sortOrder = sort,
        imageUrl = (string?)null,
        calories = 400m,
        proteinG = 30m,
        carbsG = 20m,
        fatG = 10m,
        presentAllergenCodes = Array.Empty<string>(),
        mayContainAllergenCodes = Array.Empty<string>(),
        dietaryPreferenceCodes = Array.Empty<string>(),
        ingredients = Array.Empty<object>()
    };

    private static async Task<Guid> SeedNotificationAsync(CustomerCalendarHandlerTests.Fixture fx, Guid userId, bool read)
    {
        var row = new MealMate.Domain.IdentityAccess.CustomerNotification
        {
            UserId = userId,
            EventKey = MealMate.Application.Features.CustomerPush.CustomerPushEventKeys.OrderConfirmedByRestaurant,
            SubjectId = Guid.NewGuid(),
            SubscriptionId = fx.SubscriptionId,
            TitleAr = "تم تأكيد طلبك",
            TitleEn = "Order Confirmed",
            BodyAr = "المطعم أكد وجبتك.",
            BodyEn = "The restaurant confirmed your meal.",
            DeepLink = "mealmate://orders",
            CreatedAtUtc = Now,
            ReadAtUtc = read ? Now : null,
        };
        fx.Db.CustomerNotifications.Add(row);
        await fx.Db.SaveChangesAsync();
        return row.Id;
    }

    private sealed record Fixture(
        AppDbContext Db,
        CustomerCalendarHandlers Handlers,
        Guid SubscriptionId,
        Guid CustomerId,
        Guid RestaurantA,
        Guid RestaurantB,
        Guid BreakfastA,
        Guid LunchA,
        Guid DinnerA,
        Guid BreakfastAllergen,
        Guid BreakfastB,
        Guid LunchB,
        Guid DinnerB);
}
