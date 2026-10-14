using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.DTOs;

public sealed record CampaignDraftDto(
    Guid Id,
    string NameAr,
    string NameEn,
    CampaignStatus Status,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    decimal TotalDiscountRate,
    decimal MealMateShareRate,
    decimal RestaurantShareRate,
    CampaignFundingMode FundingMode,
    int? MaxSubscribers,
    DateTime? EnrollmentOpensAtUtc,
    DateTime? EnrollmentClosesAtUtc,
    IReadOnlyList<Guid> ProgramIds,
    IReadOnlyList<Guid> BundleIds,
    DateTime? ReviewStartsAtUtc = null,
    DateTime? ReviewEndsAtUtc = null,
    int CurrentSnapshotVersion = 1,
    int? ApprovedSnapshotVersion = null,
    Guid? ApprovedByUserId = null,
    DateTime? ApprovedAtUtc = null,
    bool ReviewOverdue = false,
    string ApprovalStatus = CampaignApprovalState.NotInReview);

/// <summary>Stable approval-state identifiers derived server-side from data and server time (never stored translated text).</summary>
public static class CampaignApprovalState
{
    public const string NotInReview = "NotInReview";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string ApprovedStale = "ApprovedStale";
    public const string Overdue = "Overdue";

    public static (bool ReviewOverdue, string ApprovalStatus) Derive(
        CampaignStatus status,
        DateTime? reviewStartsAtUtc,
        DateTime? reviewEndsAtUtc,
        int currentSnapshotVersion,
        int? approvedSnapshotVersion,
        DateTime utcNow)
    {
        var hasFreshApproval = approvedSnapshotVersion.HasValue && approvedSnapshotVersion.Value == currentSnapshotVersion;
        var hasStaleApproval = approvedSnapshotVersion.HasValue && approvedSnapshotVersion.Value != currentSnapshotVersion;

        if (status == CampaignStatus.Scheduled)
        {
            return (false, hasFreshApproval ? Approved : ApprovedStale);
        }

        if (status is not (CampaignStatus.UnderReview or CampaignStatus.JoiningClosed or CampaignStatus.NeedsChanges))
        {
            return (false, NotInReview);
        }

        var windowEnded = reviewEndsAtUtc.HasValue && reviewEndsAtUtc.Value < utcNow;
        var reviewOverdue = !hasFreshApproval && windowEnded;

        var approvalStatus = hasFreshApproval
            ? Approved
            : hasStaleApproval
                ? ApprovedStale
                : reviewOverdue
                    ? Overdue
                    : Pending;

        return (reviewOverdue, approvalStatus);
    }
}
