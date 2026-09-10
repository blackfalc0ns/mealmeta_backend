namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public sealed record SaveMyRestaurantPricingRequest(
    Guid ProgramId,
    Guid BundleId,
    decimal ReferencePrice26,
    decimal? ContractedSupplyPrice26 = null,
    string? ChangeRequestReason = null);

public sealed record AddRestaurantPricingOfferRequest(Guid ProgramId, Guid BundleId);
