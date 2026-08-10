using MealMate.Application.Common.Abstractions;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using MealMate.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Persistence;

public static class DependencyInjection
{
    public const string ConnectionStringName = "MealMateDb";

    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found.");

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                });

            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.SignIn.RequireConfirmedAccount = false;
                options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddDataProtection();
        services.AddScoped<IRestaurantAccountRegistrationTokenService, RestaurantAccountRegistrationTokenService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IAuthLoginChallengeService, AuthLoginChallengeService>();
        services.AddScoped<ITotpAuthenticatorService, TotpAuthenticatorService>();
        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(
                options => string.IsNullOrWhiteSpace(options.FixedCode)
                    || options.FixedCode.Length == 6 && options.FixedCode.All(char.IsDigit),
                "Otp:FixedCode must contain exactly six digits.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.WhatsAppFixedCode)
                    || options.WhatsAppFixedCode.Length == 6 && options.WhatsAppFixedCode.All(char.IsDigit),
                "Otp:WhatsAppFixedCode must contain exactly six digits when configured.")
            .ValidateOnStart();
        services.AddOptions<TotpOptions>()
            .Bind(configuration.GetSection(TotpOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Issuer),
                "Totp:Issuer must be configured and non-empty.")
            .ValidateOnStart();

        return services;
    }
}
