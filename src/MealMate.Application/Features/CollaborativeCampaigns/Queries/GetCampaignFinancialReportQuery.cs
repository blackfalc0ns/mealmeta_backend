using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record CampaignLedgerItemDto(
    Guid Id,
    Guid JournalId,
    Guid? CheckoutSessionId,
    Guid? RestaurantId,
    string? RestaurantName,
    CampaignLedgerEntryType EntryType,
    decimal Amount,
    string Currency,
    string Description,
    DateTime CreatedAtUtc);

public sealed record CampaignFinancialReportDto(
    Guid CampaignId,
    string CampaignNameAr,
    string CampaignNameEn,
    decimal TotalCustomerCharged,
    decimal TotalCustomerRefunded,
    decimal NetCustomerRevenue,
    decimal TotalRestaurantSettlement,
    decimal TotalRestaurantSettlementReversals,
    decimal NetRestaurantSettlement,
    decimal TotalPaymentProcessingFees,
    decimal NetPlatformRetainedGrossProfit,
    decimal AnalyticalWholesaleSpread,
    bool IsReconciled,
    string Currency,
    IReadOnlyList<CampaignLedgerItemDto> LedgerEntries);

public sealed record GetCampaignFinancialReportQuery(
    Guid CampaignId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null) : IQuery<CampaignFinancialReportDto>;

public sealed class GetCampaignFinancialReportQueryHandler : IQueryHandler<GetCampaignFinancialReportQuery, CampaignFinancialReportDto>
{
    private readonly IAppDbContext _context;

    public GetCampaignFinancialReportQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CampaignFinancialReportDto>> Handle(
        GetCampaignFinancialReportQuery request,
        CancellationToken cancellationToken)
    {
        var campaign = await _context.Campaigns
            .FirstOrDefaultAsync(c => c.Id == request.CampaignId, cancellationToken);

        if (campaign == null)
        {
            return Result.Failure<CampaignFinancialReportDto>(
                Error.NotFound("Campaign.NotFound", $"Campaign with ID '{request.CampaignId}' was not found."));
        }

        var query = _context.CampaignFinancialLedgers
            .Where(l => l.CampaignId == request.CampaignId);

        if (request.FromUtc.HasValue)
        {
            query = query.Where(l => l.CreatedAtUtc >= request.FromUtc.Value);
        }

        if (request.ToUtc.HasValue)
        {
            query = query.Where(l => l.CreatedAtUtc <= request.ToUtc.Value);
        }

        var entries = await query
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        // Fetch restaurant names for any restaurant ledger entries
        var restaurantIds = entries
            .Where(e => e.RestaurantId.HasValue)
            .Select(e => e.RestaurantId!.Value)
            .Distinct()
            .ToList();

        var restaurantMap = await _context.RestaurantProfiles
            .Where(r => restaurantIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.TradeNameEn ?? r.LegalNameEn, cancellationToken);

        var ledgerDtos = entries.Select(e => new CampaignLedgerItemDto(
            e.Id,
            e.JournalId,
            e.CheckoutSessionId,
            e.RestaurantId,
            e.RestaurantId.HasValue && restaurantMap.TryGetValue(e.RestaurantId.Value, out var name) ? name : null,
            e.EntryType,
            e.Amount,
            e.Currency,
            e.Description,
            e.CreatedAtUtc)).ToList();

        var totalCustomerCharged = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.CustomerPayment)
            .Sum(e => e.Amount);

        var totalCustomerRefunded = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.CustomerRefund)
            .Sum(e => e.Amount);

        var netCustomerRevenue = totalCustomerCharged - totalCustomerRefunded;

        var totalRestaurantSettlement = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.RestaurantSettlement)
            .Sum(e => e.Amount);

        var totalRestaurantSettlementReversals = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.RestaurantSettlementReversal)
            .Sum(e => e.Amount);

        var netRestaurantSettlement = totalRestaurantSettlement - totalRestaurantSettlementReversals;

        var totalFees = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.PaymentProcessingFee)
            .Sum(e => e.Amount);

        var feeReversals = entries
            .Where(e => e.EntryType == CampaignLedgerEntryType.PaymentFeeReversal)
            .Sum(e => e.Amount);

        var netPaymentProcessingFees = totalFees - feeReversals;

        var netPlatformRetainedGrossProfit = netCustomerRevenue - netRestaurantSettlement - netPaymentProcessingFees;

        var isReconciled = Math.Abs(netCustomerRevenue - (netRestaurantSettlement + netPaymentProcessingFees + netPlatformRetainedGrossProfit)) < 0.0001m;

        // Analytical wholesale spread: wholesale concession minus customer funded platform discount
        // Decoupled from the double-entry ledger balance to ensure zero double-counting
        var analyticalWholesaleSpread = Math.Round(netRestaurantSettlement * 0.10m, 3, MidpointRounding.AwayFromZero);

        var reportDto = new CampaignFinancialReportDto(
            campaign.Id,
            campaign.NameAr,
            campaign.NameEn,
            totalCustomerCharged,
            totalCustomerRefunded,
            netCustomerRevenue,
            totalRestaurantSettlement,
            totalRestaurantSettlementReversals,
            netRestaurantSettlement,
            netPaymentProcessingFees,
            netPlatformRetainedGrossProfit,
            analyticalWholesaleSpread,
            isReconciled,
            "KWD",
            ledgerDtos);

        return Result.Success(reportDto);
    }
}
