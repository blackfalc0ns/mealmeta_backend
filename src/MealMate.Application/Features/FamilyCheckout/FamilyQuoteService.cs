using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public interface IFamilyPricingSettingsReader
{
    Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class FamilyPricingSettingsReader : IFamilyPricingSettingsReader
{
    private readonly IAppDbContext _db;

    public FamilyPricingSettingsReader(IAppDbContext db) => _db = db;

    public async Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        return SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson).Family;
    }
}

public interface IFamilyQuoteService
{
    Task<FamilyQuoteResult> QuoteAsync(FamilyGroup group, CancellationToken ct);
}

public sealed record FamilyQuoteLine(
    Guid MemberId,
    string DisplayName,
    decimal MemberPrice,
    decimal CampaignDiscount,
    decimal PromoDiscount,
    decimal GatewayFee);

public sealed record FamilyQuoteResult(
    IReadOnlyList<FamilyQuoteLine> Lines,
    decimal Subtotal,
    decimal FamilyDiscountPercent,
    decimal FamilyDiscountAmount,
    decimal FinalPrice,
    string CurrencyCode,
    bool DiscountApplied);

public sealed class FamilyQuoteService : IFamilyQuoteService
{
    private readonly ISender _sender;
    private readonly IFamilyPricingSettingsReader _settingsReader;
    private readonly ISubscriptionScheduleService _schedule;

    public FamilyQuoteService(
        ISender sender,
        IFamilyPricingSettingsReader settingsReader,
        ISubscriptionScheduleService schedule)
    {
        _sender = sender;
        _settingsReader = settingsReader;
        _schedule = schedule;
    }

    public async Task<FamilyQuoteResult> QuoteAsync(FamilyGroup group, CancellationToken ct)
    {
        var settings = await _settingsReader.GetAsync(ct);
        var lines = new List<FamilyQuoteLine>();
        var currency = "KWD";

        foreach (var member in group.Members.Where(m => m.SetupStatus == FamilyMemberSetupStatus.Ready))
        {
            if (member.ProgramId is null || member.BundleId is null || member.SelectedTier is null || member.DurationDays is null)
                continue;

            var days = member.DurationDays.Value;
            var fridayDays = member.IncludeFriday ? _schedule.GetFridayAddonDays(days) : 0;
            var pricing = await _sender.Send(
                new PreviewCustomerPricingQuery(
                    member.ProgramId.Value,
                    member.BundleId.Value,
                    member.SelectedTier.Value,
                    days,
                    IncludeFriday: member.IncludeFriday,
                    FridayDays: fridayDays),
                ct);

            if (pricing.IsFailure)
                throw new InvalidOperationException($"Failed to price family member {member.Id}: {pricing.Error.Code}");

            var result = pricing.Value;
            currency = result.CurrencyCode;
            lines.Add(new FamilyQuoteLine(
                member.Id,
                member.DisplayName,
                result.FinalCustomerPrice,
                result.CampaignDiscountApplied,
                result.PromoCodeDiscountApplied,
                result.GatewayCostRecoveryAmount));
        }

        var subtotal = lines.Sum(x => x.MemberPrice);
        var discountApplied = lines.Count >= settings.MinMembersForDiscount;
        var familyDiscountAmount = discountApplied
            ? Math.Round(subtotal * settings.DiscountPercent, 3, MidpointRounding.AwayFromZero)
            : 0m;
        var finalPrice = subtotal - familyDiscountAmount;

        return new FamilyQuoteResult(
            lines,
            subtotal,
            settings.DiscountPercent,
            familyDiscountAmount,
            finalPrice,
            currency,
            discountApplied && familyDiscountAmount > 0m);
    }
}
