using System.Text.Json;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

/// <summary>Shared persisted driver support category and status projection rules.</summary>
public static class DriverSupportIssueProjection
{
    public static readonly string[] SupportedCategoryValues =
    {
        "CustomerUnavailable", "AddressIssue", "DamagedBox", "SevereDelay", "CustomerRefusal",
        "VehicleBreakdown", "TrafficAccident", "RoadIssue", "SevereWeather", "Other",
        "DriverReassignmentRequested", "DeliveryIssue"
    };

    private static readonly HashSet<string> Categories = new(SupportedCategoryValues, StringComparer.Ordinal);
    public static bool IsSupportedCategory(string? category) => category is not null && Categories.Contains(category);

    public static string Bucket(ComplaintStatus status, bool hasContactStage = false)
    {
        if (status is ComplaintStatus.Resolved or ComplaintStatus.Closed or ComplaintStatus.Rejected) return "Resolved";
        if (hasContactStage) return "AwaitingReply";
        return status == ComplaintStatus.UnderReview ? "UnderReview" : "AwaitingReply";
    }

    public static string? StageFromAuditJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("Stage", out var stage) ? stage.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
