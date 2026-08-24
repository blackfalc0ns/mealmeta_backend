using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record ConvertIndividualToFamilyCommand(
    string? Name = null,
    Guid? FamilyPackageId = null) : ICommand<FamilyGroupScreenDto>;

public sealed record GetSubscriptionConversionOptionsQuery : IQuery<SubscriptionConversionOptionsDto>;

public sealed record SubscriptionConversionOptionsDto(
    Guid SubscriptionId,
    string Status,
    IReadOnlyList<SubscriptionConversionOptionDto> Options);

public sealed record SubscriptionConversionOptionDto(
    string Code,
    bool Available,
    string TitleAr,
    string TitleEn,
    string? Action,
    string? DisabledReason);

public sealed record ConvertFromIndividualRequest(string? Name = null, Guid? FamilyPackageId = null);
