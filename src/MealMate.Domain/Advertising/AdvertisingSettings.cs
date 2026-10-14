using MealMate.Domain.Common;

namespace MealMate.Domain.Advertising;

/// <summary>
/// Global advertising switchboard. AuctionsEnabled=false stops NEW auctions and bids;
/// already-paid approved placements keep serving (separate stop action with settlement policy).
/// </summary>
public sealed class AdvertisingSettings : AuditableEntity
{
    public bool AuctionsEnabled { get; set; }

    /// <summary>Business timezone for display; times remain UTC in this record.</summary>
    public string BusinessTimezone { get; set; } = default!;

    /// <summary>Incremented on every settings change; optimistic concurrency for admin edits.</summary>
    public int ExpectedVersion { get; set; }

    public Guid? LastChangedByUserId { get; set; }

    public DateTime? LastChangedAtUtc { get; set; }

    public string? LastChangeReason { get; set; }
}

/// <summary>
/// Approved commercial policy for auctions and paid placements. No implicit business values:
/// auctions cannot open and winners cannot pay until a version is approved by an admin.
/// </summary>
public sealed class AdCommercialPolicy : AuditableEntity
{
    public int Version { get; set; }

    public string CurrencyCode { get; set; } = default!;

    public decimal MinimumBid { get; set; }

    public decimal BidStep { get; set; }

    /// <summary>Equal highest bids: first accepted bid wins (server acceptance order).</summary>
    public const string TiePolicyFirstAccepted = "FirstAccepted";

    public string TiePolicy { get; set; } = default!;

    public int WinnerPaymentDeadlineMinutes { get; set; }

    /// <summary>What happens when the winner fails to pay: "NoAutoCharge" (offered to next bidder with consent) — never an automatic debit.</summary>
    public const string NextBidderPolicyOfferWithConsent = "OfferNextWithConsent";

    public string NextBidderPolicy { get; set; } = default!;

    /// <summary>Settlement for stopping a running paid placement: "NoRefund", "ProrataUnservedDays", "FullRefundBeforeStart".</summary>
    public string StopSettlementRule { get; set; } = default!;

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public bool IsApproved => ApprovedByUserId.HasValue && ApprovedAtUtc.HasValue;

    public static Result<AdCommercialPolicy> Create(
        int version,
        string currencyCode,
        decimal minimumBid,
        decimal bidStep,
        string tiePolicy,
        int winnerPaymentDeadlineMinutes,
        string nextBidderPolicy,
        string stopSettlementRule)
    {
        if (version <= 0)
        {
            return Result.Failure<AdCommercialPolicy>("Policy version must be a positive integer.");
        }

        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
        {
            return Result.Failure<AdCommercialPolicy>("Policy currency code must be a 3-letter ISO code.");
        }

        if (minimumBid <= 0m || bidStep <= 0m)
        {
            return Result.Failure<AdCommercialPolicy>("Minimum bid and bid step must be greater than zero.");
        }

        if (tiePolicy != TiePolicyFirstAccepted)
        {
            return Result.Failure<AdCommercialPolicy>($"Unknown tie policy '{tiePolicy}'.");
        }

        if (winnerPaymentDeadlineMinutes <= 0)
        {
            return Result.Failure<AdCommercialPolicy>("Winner payment deadline must be greater than zero minutes.");
        }

        if (nextBidderPolicy != NextBidderPolicyOfferWithConsent)
        {
            return Result.Failure<AdCommercialPolicy>($"Unknown next-bidder policy '{nextBidderPolicy}'.");
        }

        if (stopSettlementRule is not ("NoRefund" or "ProrataUnservedDays" or "FullRefundBeforeStart"))
        {
            return Result.Failure<AdCommercialPolicy>($"Unknown stop settlement rule '{stopSettlementRule}'.");
        }

        return Result.Success(new AdCommercialPolicy
        {
            Version = version,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            MinimumBid = minimumBid,
            BidStep = bidStep,
            TiePolicy = tiePolicy,
            WinnerPaymentDeadlineMinutes = winnerPaymentDeadlineMinutes,
            NextBidderPolicy = nextBidderPolicy,
            StopSettlementRule = stopSettlementRule
        });
    }

    public Result Approve(Guid approverUserId, DateTime approvedAtUtc)
    {
        if (IsApproved)
        {
            return Result.Failure("Policy version has already been approved.");
        }

        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = approvedAtUtc;
        return Result.Success();
    }
}
