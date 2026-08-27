using System.Reflection;
using FluentValidation;
using MealMate.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Application;

public static class DependencyInjection
{
    /// <summary>Marker for assembly scanning (MediatR handlers, validators).</summary>
    public static readonly Assembly AssemblyReference = typeof(DependencyInjection).Assembly;

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(AssemblyReference);

            // Pipeline order is outermost-first: log -> validate -> persist -> handler.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });

        services.AddValidatorsFromAssembly(AssemblyReference, includeInternalTypes: true);
        services.AddScoped<Features.RestaurantOperations.RestaurantCapacityService>();
        services.AddScoped<Features.RestaurantOperations.RestaurantServiceRegionService>();
        services.AddScoped<Features.RestaurantOrders.RestaurantOrderService>();
        services.AddScoped<Features.Catalog.KuwaitTenantResolver>();
        services.AddScoped<Features.Catalog.LocationCatalogService>();
        services.AddScoped<Features.CustomerCheckout.IRestaurantEligibilityService, Features.CustomerCheckout.RestaurantEligibilityService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionCheckoutReadinessService, Features.CustomerCheckout.SubscriptionCheckoutReadinessService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionMealSnapshotService, Features.CustomerCheckout.SubscriptionMealSnapshotService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionScheduleService, Features.CustomerCheckout.SubscriptionScheduleService>();
        services.AddScoped<Features.ProgramsSubscriptions.IAllowedSubscriptionDurationService, Features.ProgramsSubscriptions.AllowedSubscriptionDurationService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyPricingSettingsReader, Features.FamilyCheckout.FamilyPricingSettingsReader>();
        services.AddScoped<Features.FamilyCheckout.IFamilyQuoteService, Features.FamilyCheckout.FamilyQuoteService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyPlanEligibilityService, Features.FamilyCheckout.FamilyPlanEligibilityService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyCheckoutService, Features.FamilyCheckout.FamilyCheckoutService>();
        services.AddScoped<Features.CustomerCheckout.IBundleSlotRequirementService, Features.CustomerCheckout.BundleSlotRequirementService>();
        services.AddScoped<Features.CustomerCheckout.IMenuPlanResolutionService, Features.CustomerCheckout.MenuPlanResolutionService>();
        services.AddScoped<Features.CustomerCheckout.IRestaurantPoolSnapshotService, Features.CustomerCheckout.RestaurantPoolSnapshotService>();
        services.AddScoped<Features.CustomerCheckout.ICustomerIndividualSubscriptionGate, Features.CustomerCheckout.CustomerIndividualSubscriptionGate>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionFulfillmentService, Features.CustomerCheckout.SubscriptionFulfillmentService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionPaymentProcessor, Features.CustomerCheckout.SubscriptionPaymentProcessor>();
        services.AddScoped<Features.CustomerCheckout.ICapacityHoldExpiryService, Features.CustomerCheckout.CapacityHoldExpiryService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionRestaurantPoolBackfillService, Features.CustomerCheckout.SubscriptionRestaurantPoolBackfillService>();
        services.AddScoped<Features.ProgramsSubscriptions.Pricing.IRestaurantTierClassificationService, Features.ProgramsSubscriptions.Pricing.RestaurantTierClassificationService>();
        services.AddScoped<Features.ProgramsSubscriptions.IRestaurantDiversificationCalculator, Features.ProgramsSubscriptions.RestaurantDiversificationCalculator>();
        services.AddScoped<Features.PrepDocuments.IPrepDocumentsGenerator, Features.PrepDocuments.PrepDocumentsGenerator>();
        services.AddScoped<Features.CustomerCalendar.ISubscriptionDayDispatchService, Features.CustomerCalendar.SubscriptionDayDispatchService>();
        services.AddScoped<Features.IdentityAccess.Customers.ICustomerDeliveryAddressService, Features.IdentityAccess.Customers.CustomerDeliveryAddressService>();
        services.AddScoped<Features.FamilyManager.IFamilyCalendarAccessGate, Features.FamilyManager.FamilyCalendarAccessGate>();
        services.AddScoped<Features.CustomerCalendar.CustomerCalendarEngine>();

        return services;
    }

    public static IServiceCollection AddSubscriptionBackgroundJobs(this IServiceCollection services)
    {
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.CapacityHoldExpiryJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionFulfillmentRetryJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionRestaurantPoolBackfillJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.RestaurantOrderEscalationJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.PrepDocumentsGenerationJob>();
        return services;
    }
}
