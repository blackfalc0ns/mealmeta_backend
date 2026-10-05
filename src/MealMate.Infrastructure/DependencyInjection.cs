using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Infrastructure.Auth;
using MealMate.Infrastructure.Email;
using MealMate.Infrastructure.Push;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MealMate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<ITokenService, TokenService>();

        // Driver delivery-map navigation builder (Screen 03.01): fresh-location
        // rules, real route lookup, and the external navigation link.
        services
            .AddOptions<DriverNavigationOptions>()
            .Bind(configuration.GetSection(DriverNavigationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IDriverDeliveryNavigationBuilder, DriverDeliveryNavigationBuilder>();

        // Email secret protector (requires Email:SecretsEncryptionKey in configuration)
        services.AddSingleton<IEmailSecretProtector, EmailSecretProtector>();

        // Resend HTTP client
        services.AddHttpClient<ITransactionalEmailSender, ResendTransactionalEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MealMate/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register concrete OTP delivery services
        services.AddScoped<ResendOtpDeliveryService>();
        services.AddScoped<LoggingOtpDeliveryService>();
        services.AddScoped<SilentOtpDeliveryService>();

        // Route OTP delivery based on channel and environment
        services.AddScoped<IOtpDeliveryService, RoutingOtpDeliveryService>();

        services
            .AddOptions<ImageKitOptions>()
            .Bind(configuration.GetSection(ImageKitOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.PrivateKey))
                {
                    options.PrivateKey = Environment.GetEnvironmentVariable("IMAGEKIT_PRIVATE_KEY");
                }

                if (string.IsNullOrWhiteSpace(options.UrlEndpoint))
                {
                    options.UrlEndpoint = Environment.GetEnvironmentVariable("IMAGEKIT_URL_ENDPOINT");
                }
            });
        services.AddHttpClient("ImageKit", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.ExpectContinue = false;
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MealMate/1.0");
        });
        services.AddScoped<LocalFileStorage>();
        services.AddScoped<ImageKitFileStorage>();
        services.AddHttpClient("RegistrationUploadsSource", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddScoped<IFileStorage, RoutingFileStorage>();

        services
            .AddOptions<GoogleMapsOptions>()
            .Bind(configuration.GetSection(GoogleMapsOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = Environment.GetEnvironmentVariable("GOOGLE_MAPS_SERVER_API_KEY");
                }
            });
        services.AddHttpClient<IGoogleGeocodingService, GoogleGeocodingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MealMate/1.0");
        });
        services.AddHttpClient<IGoogleRoutingService, GoogleRoutingService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MealMate/1.0");
        });

        // MyFatoorah Payment Integration Service
        services.AddHttpClient<IMyFatoorahPaymentService, MyFatoorahPaymentService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services
            .AddOptions<FirebasePushOptions>()
            .Bind(configuration.GetSection(FirebasePushOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.CredentialPath))
                {
                    options.CredentialPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
                }

                if (string.IsNullOrWhiteSpace(options.ProjectId))
                {
                    options.ProjectId = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");
                }
            });

        services.AddSingleton<IFcmMessagingClient, FirebaseFcmMessagingClient>();
        services.AddScoped<ICustomerPushNotifier, FirebaseCustomerPushNotifier>();
        services.AddScoped<IDriverPushNotifier, FirebaseDriverPushNotifier>();
        services.AddScoped<ICustomerPushTestService, CustomerPushTestService>();
        services.AddScoped<IAdminPushTestService, AdminPushTestService>();
        services.AddScoped<IRestaurantPushTestService, RestaurantPushTestService>();
        services.AddScoped<IRegistrationPushNotificationService, RegistrationPushNotificationService>();
        services.AddScoped<IMealPushNotifier, MealPushNotifier>();
        services.AddScoped<IDeliverySupervisorNotifier, DeliverySupervisorNotifier>();
        services.AddScoped<IDriverRegistrationNotificationService, DriverRegistrationNotificationService>();
        services.AddScoped<IBatchDispatchNotifier, BatchDispatchNotifier>();
        services.AddScoped<MealMate.Application.Features.CollaborativeCampaigns.Services.ICampaignNotificationSender, Services.CampaignNotificationSender>();
        services.AddScoped<MealMate.Application.Features.CollaborativeCampaigns.Services.IPaymentRefundGateway, Services.CampaignPaymentRefundGateway>();
        services.AddSingleton<MealMate.Application.Features.CollaborativeCampaigns.Services.ICampaignTelemetry, MealMate.Application.Features.CollaborativeCampaigns.Services.CampaignTelemetry>();

        // Voice Calls (Coturn STUN/TURN Credential Provider & FCM Push Notifier)
        services
            .AddOptions<VoiceCalls.CoturnOptions>()
            .Bind(configuration.GetSection(VoiceCalls.CoturnOptions.SectionName))
            .Validate(options =>
            {
                if (!options.Enabled) return true;
                if (string.IsNullOrWhiteSpace(options.ServerHost)) return false;
                if (string.IsNullOrWhiteSpace(options.SharedSecret)) return false;
                if (options.TurnPort <= 0 || options.TurnPort > 65535) return false;
                if (options.StunPort <= 0 || options.StunPort > 65535) return false;
                if (options.TtlSeconds <= 0) return false;
                if (options.EnableTls && (options.TurnsPort <= 0 || options.TurnsPort > 65535)) return false;
                return true;
            }, "Coturn configuration is invalid. ServerHost, SharedSecret, and valid ports are required when Coturn is enabled.");
        services.AddScoped<MealMate.Application.Features.VoiceCalls.Abstractions.ITurnCredentialService, VoiceCalls.TurnCredentialService>();
        services.AddScoped<MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallPushNotifier, VoiceCalls.VoiceCallPushNotifier>();

        return services;
    }
}
