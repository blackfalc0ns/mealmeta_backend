using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public sealed record CustomerProfilePrerequisitesEvaluation(
    bool IsMet,
    bool HasAddress,
    bool HasAllergies,
    bool HasDietaryPreferences,
    CustomerActiveDeliveryAddress? ActiveAddress,
    string? Status,
    string? MessageCode,
    string? MissingStep,
    string? ActionEndpoint,
    Error? GateError);

public interface ICustomerProfilePrerequisitesGate
{
    Task<CustomerProfilePrerequisitesEvaluation> EvaluateAsync(Guid customerUserId, CancellationToken ct = default);
}

public sealed class CustomerProfilePrerequisitesGate : ICustomerProfilePrerequisitesGate
{
    private readonly IAppDbContext _db;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;

    public CustomerProfilePrerequisitesGate(
        IAppDbContext db,
        ICustomerDeliveryAddressService deliveryAddresses)
    {
        _db = db;
        _deliveryAddresses = deliveryAddresses;
    }

    internal CustomerProfilePrerequisitesGate(
        IAppDbContext db,
        IDateTimeProvider clock)
        : this(db, new CustomerDeliveryAddressService(db, clock))
    {
    }

    internal CustomerProfilePrerequisitesGate(IAppDbContext db)
        : this(db, new SystemDateTimeProvider())
    {
    }

    internal sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public async Task<CustomerProfilePrerequisitesEvaluation> EvaluateAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty)
        {
            return new CustomerProfilePrerequisitesEvaluation(
                IsMet: false,
                HasAddress: false,
                HasAllergies: false,
                HasDietaryPreferences: false,
                ActiveAddress: null,
                Status: SubscriptionCatalogAvailabilityStatus.AddressRequired,
                MessageCode: "checkout.address_required",
                MissingStep: SubscriptionCheckoutGateCodes.Address,
                ActionEndpoint: "POST /api/v1/customers/me/addresses",
                GateError: Error.Unauthorized("auth.unauthorized"));
        }

        var activeAddress = await _deliveryAddresses.ResolveActiveDeliveryAddressAsync(customerUserId, ct);
        var profile = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => new { x.AllergiesCompletedAtUtc, x.DietaryPreferencesCompletedAtUtc })
            .FirstOrDefaultAsync(ct);
        var hasAllergies = profile?.AllergiesCompletedAtUtc.HasValue == true;
        var hasDietary = profile?.DietaryPreferencesCompletedAtUtc.HasValue == true;

        if (activeAddress is null)
        {
            return new CustomerProfilePrerequisitesEvaluation(
                IsMet: false,
                HasAddress: false,
                HasAllergies: hasAllergies,
                HasDietaryPreferences: hasDietary,
                ActiveAddress: null,
                Status: SubscriptionCatalogAvailabilityStatus.AddressRequired,
                MessageCode: "checkout.address_required",
                MissingStep: SubscriptionCheckoutGateCodes.Address,
                ActionEndpoint: "POST /api/v1/customers/me/addresses",
                GateError: new Error("checkout.address_required", null, ErrorType.Conflict,
                [
                    new
                    {
                        error = "checkout.address_required",
                        missingStep = SubscriptionCheckoutGateCodes.Address,
                        actionEndpoint = "POST /api/v1/customers/me/addresses"
                    }
                ]));
        }

        if (!hasAllergies)
        {
            return new CustomerProfilePrerequisitesEvaluation(
                IsMet: false,
                HasAddress: true,
                HasAllergies: false,
                HasDietaryPreferences: hasDietary,
                ActiveAddress: activeAddress,
                Status: SubscriptionCatalogAvailabilityStatus.AllergiesRequired,
                MessageCode: "checkout.allergies_required",
                MissingStep: SubscriptionCheckoutGateCodes.Allergies,
                ActionEndpoint: "GET /api/v1/customers/me/food-preferences/allergies",
                GateError: new Error("checkout.allergies_required", null, ErrorType.Conflict,
                [
                    new
                    {
                        error = "checkout.allergies_required",
                        missingStep = SubscriptionCheckoutGateCodes.Allergies,
                        actionEndpoint = "GET /api/v1/customers/me/food-preferences/allergies"
                    }
                ]));
        }

        if (!hasDietary)
        {
            return new CustomerProfilePrerequisitesEvaluation(
                IsMet: false,
                HasAddress: true,
                HasAllergies: true,
                HasDietaryPreferences: false,
                ActiveAddress: activeAddress,
                Status: SubscriptionCatalogAvailabilityStatus.DietaryPreferencesRequired,
                MessageCode: "checkout.dietary_preferences_required",
                MissingStep: SubscriptionCheckoutGateCodes.DietaryPreferences,
                ActionEndpoint: "GET /api/v1/customers/me/food-preferences/dietary",
                GateError: new Error("checkout.dietary_preferences_required", null, ErrorType.Conflict,
                [
                    new
                    {
                        error = "checkout.dietary_preferences_required",
                        missingStep = SubscriptionCheckoutGateCodes.DietaryPreferences,
                        actionEndpoint = "GET /api/v1/customers/me/food-preferences/dietary"
                    }
                ]));
        }

        return new CustomerProfilePrerequisitesEvaluation(
            IsMet: true,
            HasAddress: true,
            HasAllergies: true,
            HasDietaryPreferences: true,
            ActiveAddress: activeAddress,
            Status: null,
            MessageCode: null,
            MissingStep: null,
            ActionEndpoint: null,
            GateError: null);
    }
}
