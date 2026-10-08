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
        services.AddScoped<Features.CustomerNotifications.ICustomerNotificationWriter, Features.CustomerNotifications.CustomerNotificationWriter>();
        services.AddScoped<Features.VoiceCalls.Abstractions.IVoiceCallAuthorizationService, Features.VoiceCalls.Services.VoiceCallAuthorizationService>();
        services.AddScoped<Features.VoiceCalls.Abstractions.IDeliveryContactEligibilityService, Features.VoiceCalls.Services.DeliveryContactEligibilityService>();
        services.AddScoped<Features.VoiceCalls.Abstractions.IVoiceCallDeviceSessionService, Features.VoiceCalls.Services.VoiceCallDeviceSessionService>();
        services.AddScoped<Features.VoiceCalls.V2.Commands.DeliveryContactCaseService>();
        services.AddScoped<Features.VoiceCalls.V2.Commands.DeliveryContactCaseService>();
        services.AddScoped<Features.AdminDashboard.OperationalPolicies.IOperationalPolicyService, Features.AdminDashboard.OperationalPolicies.OperationalPolicyService>();
        services.AddScoped<Features.RestaurantOperations.RestaurantCapacityService>();
        services.AddScoped<Features.RestaurantOperations.RestaurantServiceRegionService>();
        services.AddScoped<Features.RestaurantOperations.Readiness.IRestaurantReadinessService, Features.RestaurantOperations.Readiness.RestaurantReadinessService>();
        services.AddScoped<Features.FoodDefinitions.IAdminFoodDefinitionsService, Features.FoodDefinitions.AdminFoodDefinitionsService>();
        services.AddScoped<Features.RestaurantOrders.RestaurantOrderService>();
        services.AddScoped<Features.IdentityAccess.RestaurantRegistration.RestaurantChangeReviewService>();
        services.AddScoped<Features.Catalog.KuwaitTenantResolver>();
        services.AddScoped<Features.AdminCoverage.AdminCoverageAccess>();
        services.AddScoped<Features.Catalog.LocationCatalogService>();
        services.AddScoped<Features.Maps.IMapsService, Features.Maps.MapsService>();
        services.AddScoped<Features.CustomerCheckout.IRestaurantEligibilityService, Features.CustomerCheckout.RestaurantEligibilityService>();
        services.AddScoped<Features.RestaurantOperations.Availability.IRestaurantCapacityReader, Features.RestaurantOperations.Availability.RestaurantCapacityReader>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionCheckoutReadinessService, Features.CustomerCheckout.SubscriptionCheckoutReadinessService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionRestaurantAvailabilityService, Features.CustomerCheckout.SubscriptionRestaurantAvailabilityService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionMealSnapshotService, Features.CustomerCheckout.SubscriptionMealSnapshotService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionScheduleService, Features.CustomerCheckout.SubscriptionScheduleService>();
        services.AddScoped<Features.ProgramsSubscriptions.IAllowedSubscriptionDurationService, Features.ProgramsSubscriptions.AllowedSubscriptionDurationService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyPricingSettingsReader, Features.FamilyCheckout.FamilyPricingSettingsReader>();
        services.AddScoped<Features.FamilyCheckout.IFamilyQuoteService, Features.FamilyCheckout.FamilyQuoteService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyPlanEligibilityService, Features.FamilyCheckout.FamilyPlanEligibilityService>();
        services.AddScoped<Features.FamilyCheckout.IFamilyCheckoutService, Features.FamilyCheckout.FamilyCheckoutService>();
        services.AddScoped<Features.FamilyCheckout.IFamilySubscriptionFlowService, Features.FamilyCheckout.FamilySubscriptionFlowService>();
        services.AddScoped<Features.CustomerCheckout.IBundleSlotRequirementService, Features.CustomerCheckout.BundleSlotRequirementService>();
        services.AddScoped<Features.CustomerCheckout.IMenuPlanResolutionService, Features.CustomerCheckout.MenuPlanResolutionService>();
        services.AddScoped<Features.CustomerCheckout.IRestaurantPoolSnapshotService, Features.CustomerCheckout.RestaurantPoolSnapshotService>();
        services.AddScoped<Features.CustomerCheckout.ICustomerIndividualSubscriptionGate, Features.CustomerCheckout.CustomerIndividualSubscriptionGate>();
        services.AddScoped<Features.CustomerCheckout.ICustomerCheckoutService, Features.CustomerCheckout.CustomerCheckoutService>();
        services.AddScoped<Features.CustomerCheckout.IAdminCheckoutOperationsService, Features.CustomerCheckout.AdminCheckoutOperationsService>();
        services.AddScoped<Features.CustomerCheckout.IPayzahWebhookService, Features.CustomerCheckout.PayzahWebhookService>();
        services.AddScoped<Features.CustomerCheckout.IPayzahPaymentReturnService, Features.CustomerCheckout.PayzahPaymentReturnService>();
        services.AddOptions<Features.CustomerCheckout.FulfillmentOptions>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionFulfillmentService, Features.CustomerCheckout.SubscriptionFulfillmentService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionPaymentProcessor, Features.CustomerCheckout.SubscriptionPaymentProcessor>();
        services.AddScoped<Features.CustomerCheckout.ISandboxCheckoutService, Features.CustomerCheckout.SandboxCheckoutService>();
        services.AddScoped<Features.CustomerCheckout.ICapacityHoldExpiryService, Features.CustomerCheckout.CapacityHoldExpiryService>();
        services.AddScoped<Features.CustomerCheckout.ISubscriptionRestaurantPoolBackfillService, Features.CustomerCheckout.SubscriptionRestaurantPoolBackfillService>();
        services.AddScoped<Features.CustomerCheckout.IOrderReplacementOrchestrator, Features.CustomerCheckout.OrderReplacementOrchestrator>();
        services.AddScoped<Features.ProgramsSubscriptions.Pricing.IRestaurantTierClassificationService, Features.ProgramsSubscriptions.Pricing.RestaurantTierClassificationService>();
        services.AddScoped<Features.ProgramsSubscriptions.Pricing.IRestaurantPricingWorkspaceService, Features.ProgramsSubscriptions.Pricing.RestaurantPricingWorkspaceService>();
        services.AddScoped<Features.ProgramsSubscriptions.ISubscriptionTierUpgradeQuoteBuilder, Features.ProgramsSubscriptions.SubscriptionTierUpgradeQuoteBuilder>();
        services.AddScoped<Features.ProgramsSubscriptions.IRestaurantDiversificationCalculator, Features.ProgramsSubscriptions.RestaurantDiversificationCalculator>();
        services.AddScoped<Features.PrepDocuments.IPrepDocumentsGenerator, Features.PrepDocuments.PrepDocumentsGenerator>();
        services.AddScoped<Features.CustomerCalendar.ISubscriptionDayDispatchService, Features.CustomerCalendar.SubscriptionDayDispatchService>();
        services.AddScoped<Features.IdentityAccess.Customers.ICustomerDeliveryAddressService, Features.IdentityAccess.Customers.CustomerDeliveryAddressService>();
        services.AddScoped<Features.IdentityAccess.Customers.ICustomerAddressBook, Features.IdentityAccess.Customers.CustomerAddressHandlers>();
        services.AddScoped<Features.FamilyManager.IFamilyCalendarAccessGate, Features.FamilyManager.FamilyCalendarAccessGate>();
        services.AddScoped<Features.CalendarMeals.IMealRecommendationEngine, Features.CalendarMeals.MealRecommendationEngine>();
        services.AddScoped<Features.CustomerCalendar.ICapacityTransferService, Features.CustomerCalendar.CapacityTransferService>();
        services.AddScoped<Features.CustomerCalendar.CustomerCalendarEngine>();
        services.AddScoped<Features.IdentityAccess.Influencer.IInfluencerWalletService, Features.IdentityAccess.Influencer.InfluencerWalletService>();
        services.AddScoped<Features.IdentityAccess.Influencer.IInfluencerAnalyticsService, Features.IdentityAccess.Influencer.InfluencerAnalyticsService>();
        services.AddScoped<Features.IdentityAccess.Influencer.IInfluencerProfileService, Features.IdentityAccess.Influencer.InfluencerProfileService>();
        services.AddScoped<Features.IdentityAccess.Influencer.IInfluencerAdminService, Features.IdentityAccess.Influencer.InfluencerAdminService>();
        services.AddScoped<Features.ProgramsSubscriptions.Cancellation.ISubscriptionCancellationService, Features.ProgramsSubscriptions.Cancellation.SubscriptionCancellationService>();
        services.AddScoped<Features.ProgramsSubscriptions.Freeze.ISubscriptionFreezeService, Features.ProgramsSubscriptions.Freeze.SubscriptionFreezeService>();
        services.AddScoped<Features.ProgramsSubscriptions.Freeze.ISubscriptionFreezePolicyService, Features.ProgramsSubscriptions.Freeze.SubscriptionFreezePolicyService>();
        services.AddScoped<Features.ProgramsSubscriptions.ISubscriptionPricingSettingsService, Features.ProgramsSubscriptions.SubscriptionPricingSettingsService>();
        services.AddScoped<Features.ProgramsSubscriptions.Upgrade.ISubscriptionUpgradeService, Features.ProgramsSubscriptions.Upgrade.SubscriptionUpgradeService>();
        services.AddScoped<Features.ProgramsSubscriptions.Renewal.ISubscriptionRenewalService, Features.ProgramsSubscriptions.Renewal.SubscriptionRenewalService>();
        services.AddScoped<Features.ProgramsSubscriptions.CustomerSubscriptions.ICustomerSubscriptionQueryService, Features.ProgramsSubscriptions.CustomerSubscriptions.CustomerSubscriptionQueryService>();
        services.AddScoped<Features.ProgramsSubscriptions.Flow.ISubscriptionFlowQueryService, Features.ProgramsSubscriptions.Flow.SubscriptionFlowQueryService>();
        services.AddScoped<Features.ProgramsSubscriptions.Flow.ISubscriptionFlowCouponService, Features.ProgramsSubscriptions.Flow.SubscriptionFlowCouponService>();

        // Collaborative Campaigns Services
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignTierService, Features.CollaborativeCampaigns.Services.CampaignTierService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignProfitabilityEvaluator, Features.CollaborativeCampaigns.Services.CampaignProfitabilityEvaluator>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ISnapshotFreshnessValidator, Features.CollaborativeCampaigns.Services.SnapshotFreshnessValidator>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignLaunchGuardEvaluator, Features.CollaborativeCampaigns.Services.CampaignLaunchGuardEvaluator>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignCapacityCalendarInitializer, Features.CollaborativeCampaigns.Services.CampaignCapacityCalendarInitializer>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignFulfillmentAllocator, Features.CollaborativeCampaigns.Services.CampaignFulfillmentAllocator>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ISubscriberCapManager, Features.CollaborativeCampaigns.Services.SubscriberCapManager>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignReservationCoordinator, Features.CollaborativeCampaigns.Services.CampaignReservationCoordinator>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignReservationExpirationService, Features.CollaborativeCampaigns.Services.CampaignReservationExpirationService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ILatePaymentResolutionService, Features.CollaborativeCampaigns.Services.LatePaymentResolutionService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignLedgerPostingService, Features.CollaborativeCampaigns.Services.CampaignLedgerPostingService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignCheckoutReconciliationService, Features.CollaborativeCampaigns.Services.CampaignCheckoutReconciliationService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.IFulfillmentCutoffPolicy, Features.CollaborativeCampaigns.Services.FulfillmentCutoffPolicy>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignLifecycleSchedulerService, Features.CollaborativeCampaigns.Services.CampaignLifecycleSchedulerService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.IOutboxDispatcherService, Features.CollaborativeCampaigns.Services.OutboxDispatcherService>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ICampaignEligibilityEngine, Features.CollaborativeCampaigns.Services.CampaignEligibilityEngine>();

        // Offers & Promotions Services
        services.AddScoped<Features.Offers.Services.IOfferEligibilityEngine, Features.Offers.Services.OfferEligibilityEngine>();
        services.AddScoped<Features.Offers.Services.IOfferLifecycleSchedulerService, Features.Offers.Services.OfferLifecycleSchedulerService>();
        services.AddScoped<Features.Offers.Services.ICampaignOfferSyncService, Features.Offers.Services.CampaignOfferSyncService>();

        // Dispatcher Smart Recommendation
        services.AddScoped<Features.DriverDelivery.Dispatcher.IDispatchRecommendationEngine, Features.DriverDelivery.Dispatcher.DispatchRecommendationEngine>();

        return services;
    }

    public static IServiceCollection AddSubscriptionBackgroundJobs(this IServiceCollection services)
    {
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.CapacityHoldExpiryJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionFulfillmentRetryJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.SubscriptionRestaurantPoolBackfillJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.RestaurantOrderEscalationJob>();
        services.AddScoped<Features.CollaborativeCampaigns.Services.ProviderRefundReconciliationService>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.PrepDocumentsGenerationJob>();
        services.AddHostedService<Features.CustomerCheckout.BackgroundJobs.CustomerDeliveryPrepPushJob>();
        services.AddScoped<Features.DriverDelivery.SupervisorNotifications.DeliveryDelayEscalationService>();
        services.AddHostedService<Features.DriverDelivery.SupervisorNotifications.DeliveryDelayEscalationJob>();
        return services;
    }

    public static IServiceCollection AddVoiceCallBackgroundJobs(this IServiceCollection services)
    {
        services.AddHostedService<Features.VoiceCalls.BackgroundJobs.VoiceCallRecoveryJob>();
        return services;
    }
}
