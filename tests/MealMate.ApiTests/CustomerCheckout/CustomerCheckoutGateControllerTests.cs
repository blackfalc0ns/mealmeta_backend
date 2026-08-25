using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerCheckoutGateControllerTests
{
    [Fact]
    public async Task Checkout_WithoutAddress_ReturnsAddressRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, false, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, false, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, false, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.address_required");
    }

    [Fact]
    public async Task Checkout_WithoutAllergiesConfirmation_ReturnsAllergiesRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, false, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, true, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.allergies_required");
    }

    [Fact]
    public async Task Checkout_WithoutDietaryConfirmation_ReturnsDietaryRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, true, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, false, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.dietary_preferences_required");
    }

    [Fact]
    public async Task Checkout_WithExistingSubscriptionBlock_ReturnsConflict()
    {
        var result = await CheckoutWithExistingSubscriptionAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var value = conflict.Value!;
        var error = value.GetType().GetProperty("error")?.GetValue(value)?.ToString();
        var subscriptionId = value.GetType().GetProperty("subscriptionId")?.GetValue(value);
        Assert.Equal("checkout.existing_subscription", error);
        Assert.NotNull(subscriptionId);
    }

    private static async Task<IActionResult> CheckoutWithExistingSubscriptionAsync()
    {
        var userId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var blockingId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var items = new[]
        {
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, true, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, true, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e")
        };

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(userId, programId, bundleId, RestaurantTier.Basic, 26, false, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionCheckoutReadinessDto(
                true, false, false,
                SubscriptionCheckoutGateCodes.Summary,
                "w", "w",
                SubscriptionCheckoutGateCodes.ExistingSubscription,
                new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"),
                new SubscriptionSummaryAddressDto(Guid.NewGuid(), "المنزل", "شارع", null, null, null, Guid.NewGuid()),
                0, items, [],
                blockingId,
                nameof(SubscriptionStatus.Active)));

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
        users.FindByIdAsync(userId.ToString()).Returns(new ApplicationUser { Id = userId });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), Substitute.For<IDateTimeProvider>(),
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            readiness)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Checkout(
            new StartCheckoutRequest(programId, bundleId, RestaurantTier.Basic, 26, "mealmate://ok", "mealmate://err", DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);
    }

    private static async Task<IActionResult> CheckoutWithItemsAsync(params SubscriptionProfileGateItemDto[] items)
    {
        var userId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(userId, programId, bundleId, RestaurantTier.Basic, 26, false, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionCheckoutReadinessDto(
                false, false, false, items.First(x => !x.IsComplete).Code, "w", "w",
                SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(new SubscriptionCheckoutReadinessDto(
                    false, false, false, "ADDRESS", "w", "w", null,
                    new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"), null, 0, items, [])) ?? "checkout.address_required",
                new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"),
                items.First(x => x.Code == SubscriptionCheckoutGateCodes.Address).IsComplete
                    ? new SubscriptionSummaryAddressDto(Guid.NewGuid(), "المنزل", "شارع", null, null, null, Guid.NewGuid())
                    : null,
                0, items, []));

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
        users.FindByIdAsync(userId.ToString()).Returns(new ApplicationUser { Id = userId });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), Substitute.For<IDateTimeProvider>(),
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            readiness)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Checkout(
            new StartCheckoutRequest(programId, bundleId, RestaurantTier.Basic, 26, "mealmate://ok", "mealmate://err", DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);
    }

    private static void AssertConflict(IActionResult result, string expected)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = conflict.Value!.GetType().GetProperty("error")?.GetValue(conflict.Value)?.ToString();
        Assert.Equal(expected, error);
    }
}
