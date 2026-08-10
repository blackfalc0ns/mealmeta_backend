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
        services.AddScoped<Features.Catalog.KuwaitTenantResolver>();

        return services;
    }
}
