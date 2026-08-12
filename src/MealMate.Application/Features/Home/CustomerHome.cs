using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Home;

/// <summary>
/// The data needed to render the customer application's home screen.
/// Subscription-only content is deliberately nullable so clients never need to
/// infer subscription eligibility from an empty collection or a client flag.
/// </summary>
public sealed record CustomerHomeDto(
    DateOnly Today,
    bool IsSubscriber,
    CustomerHomeSubscriptionDto? Subscription,
    CustomerHomeNutritionDto? Nutrition,
    IReadOnlyList<TodayMealDto> TodayMeals,
    IReadOnlyList<CustomerHomeRestaurantDto> Restaurants);

public sealed record CustomerHomeSubscriptionDto(
    Guid Id,
    string ProgramNameAr,
    string ProgramNameEn,
    string BundleNameAr,
    string BundleNameEn,
    DateOnly StartDate,
    DateOnly EndDate,
    int RemainingDays);

public sealed record TodayMealDto(
    Guid RestaurantId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    Guid MealId,
    string MealNameAr,
    string MealNameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Slot,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG,
    string? ImageUrl);

/// <summary>
/// Nutritional totals for the meals selected for the customer's current
/// subscription day. This is null for non-subscribers so nutrition data is
/// never exposed as a public home-screen feature.
/// </summary>
public sealed record CustomerHomeNutritionDto(
    decimal Calories,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG);

/// <summary>
/// The public restaurant carousel on the home screen. It is deliberately
/// independent of a customer's subscription and daily meal selection.
/// </summary>
public sealed record CustomerHomeRestaurantDto(
    Guid Id,
    string NameAr,
    string NameEn,
    Guid? RegionId,
    string? PhotoUrl);

file sealed record HomeMealCandidate(
    Guid RestaurantId,
    string RestaurantNameAr,
    string RestaurantNameEn,
    Guid MealId,
    string MealNameAr,
    string MealNameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? Slot,
    decimal? Calories,
    decimal? ProteinG,
    decimal? CarbsG,
    decimal? FatG);

public sealed record GetCustomerHomeQuery() : IQuery<CustomerHomeDto>;

public sealed class GetCustomerHomeQueryHandler : IQueryHandler<GetCustomerHomeQuery, CustomerHomeDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetCustomerHomeQueryHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<CustomerHomeDto>> Handle(GetCustomerHomeQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } customerUserId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        if (_currentUser.UserType is not UserType.Customer)
        {
            return Error.Forbidden(MessageCodes.Auth.InvalidCredentials, "This endpoint is only available to customer accounts.");
        }

        var today = _clock.TodayUtc;
        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .Where(item => item.CustomerUserId == customerUserId
                && item.Status == SubscriptionStatus.Active
                && item.StartDate <= today
                && item.EndDate >= today)
            .OrderByDescending(item => item.EndDate)
            .Select(item => new CustomerHomeSubscriptionDto(
                item.Id,
                item.Program.NameAr,
                item.Program.NameEn,
                item.Bundle.NameAr,
                item.Bundle.NameEn,
                item.StartDate,
                item.EndDate,
                item.EndDate.DayNumber - today.DayNumber + 1))
            .FirstOrDefaultAsync(cancellationToken);

        // Restaurants are a public discovery section. Do not constrain it by
        // a subscription, selected tier, or the customer's region.
        var restaurants = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(restaurant => restaurant.IsOperationallyActive
                && restaurant.VerificationStatus == VerificationStatus.Approved)
            .OrderBy(restaurant => restaurant.TradeNameAr)
            .Select(restaurant => new CustomerHomeRestaurantDto(
                restaurant.Id,
                restaurant.TradeNameAr,
                restaurant.TradeNameEn,
                restaurant.PrimaryRegionId,
                _db.RestaurantBranches
                    .Where(branch => branch.RestaurantRegistrationId == restaurant.Id && branch.IsActive)
                    .OrderByDescending(branch => branch.IsPrimary)
                    .ThenBy(branch => branch.NameAr)
                    .Select(branch => branch.PhotoStorageKey)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        // Both the selected meals and their macro totals are subscription-only.
        // In particular, do not fall back to platform meals when no daily
        // selection exists: an empty list is the accurate subscriber state.
        var mealCandidates = subscription is null
            ? new List<HomeMealCandidate>()
            : await (
                from selection in _db.DailyMealSelections.AsNoTracking()
                join subscriptionDay in _db.SubscriptionDays.AsNoTracking()
                    on selection.SubscriptionDayId equals subscriptionDay.Id
                join meal in _db.Meals.AsNoTracking() on selection.MealId equals meal.Id
                join restaurant in _db.RestaurantProfiles.AsNoTracking() on meal.RestaurantId equals restaurant.Id
                where subscriptionDay.SubscriptionId == subscription.Id
                    && subscriptionDay.ServiceDate == today
                    && restaurant.IsOperationallyActive
                    && restaurant.VerificationStatus == VerificationStatus.Approved
                orderby selection.SortOrder
                select new HomeMealCandidate(
                    meal.RestaurantId,
                    restaurant.TradeNameAr,
                    restaurant.TradeNameEn,
                    meal.Id,
                    meal.NameAr,
                    meal.NameEn,
                    meal.DescriptionAr,
                    meal.DescriptionEn,
                    meal.Slot,
                    meal.Calories,
                    meal.ProteinG,
                    meal.CarbsG,
                    meal.FatG))
                .ToListAsync(cancellationToken);

        var mealIds = mealCandidates.Select(item => item.MealId).ToList();
        var primaryImageUrls = mealIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.MealImages.AsNoTracking()
                .Where(image => mealIds.Contains(image.MealId))
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.DisplayOrder)
                .GroupBy(image => image.MealId)
                .Select(group => new { MealId = group.Key, ImageUrl = group.Select(image => image.ImageUrl).First() })
                .ToDictionaryAsync(item => item.MealId, item => (string?)item.ImageUrl, cancellationToken);

        var todayMeals = mealCandidates
            .Select(item => new TodayMealDto(
                item.RestaurantId,
                item.RestaurantNameAr,
                item.RestaurantNameEn,
                item.MealId,
                item.MealNameAr,
                item.MealNameEn,
                item.DescriptionAr,
                item.DescriptionEn,
                item.Slot,
                item.Calories,
                item.ProteinG,
                item.CarbsG,
                item.FatG,
                primaryImageUrls.GetValueOrDefault(item.MealId)))
            .ToList();

        var nutrition = subscription is null
            ? null
            : new CustomerHomeNutritionDto(
                mealCandidates.Sum(item => item.Calories ?? 0),
                mealCandidates.Sum(item => item.ProteinG ?? 0),
                mealCandidates.Sum(item => item.CarbsG ?? 0),
                mealCandidates.Sum(item => item.FatG ?? 0));

        return new CustomerHomeDto(today, subscription is not null, subscription, nutrition, todayMeals, restaurants);
    }
}
