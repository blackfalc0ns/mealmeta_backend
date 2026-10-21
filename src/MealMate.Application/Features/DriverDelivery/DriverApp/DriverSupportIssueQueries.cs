using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetDriverSupportIssuesQuery(string Status = "All", string? Search = null, int PageNumber = 1, int PageSize = 20) : IQuery<DriverSupportIssuesResponseDto>;
public sealed record GetDriverSupportIssueQuery(Guid IssueId) : IQuery<DriverSupportIssueDetailDto>;

public sealed class DriverSupportIssueQueries(IAppDbContext db, ICurrentUser user, ILocalizedMessageProvider messages)
    : IQueryHandler<GetDriverSupportIssuesQuery, DriverSupportIssuesResponseDto>, IQueryHandler<GetDriverSupportIssueQuery, DriverSupportIssueDetailDto>
{
    public async Task<Result<DriverSupportIssuesResponseDto>> Handle(GetDriverSupportIssuesQuery request, CancellationToken ct)
    {
        var forbidden = await DriverProfileIdAsync(ct);
        if (forbidden is null) return Error.Forbidden("DRIVER_SCOPE_REQUIRED", messages.GetMessage("driver_support_issue.forbidden"));
        var status = request.Status?.Trim();
        if (status is not ("All" or "UnderReview" or "AwaitingReply" or "Resolved") || request.PageNumber < 1 || request.PageSize is < 1 or > 100 || request.Search?.Length > 200)
            return Error.Validation("DRIVER_SUPPORT_QUERY_INVALID", messages.GetMessage("driver_support_issue.query_invalid"));
        var profileId = forbidden.Value;
        var q = db.Complaints.AsNoTracking().Where(c => c.DriverProfileId == profileId && DriverSupportIssueProjection.SupportedCategoryValues.Contains(c.Category));
        var rows = await q.OrderByDescending(c => c.SubmittedAtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId.HasValue && rows.Select(x => x.Id).Contains(a.EntityId.Value)
            && (a.Action == "DriverSupportProgressUpdated" || a.Action == "SupportIssueResolved" || a.Action == "DriverReportedIssue"))
            .ToListAsync(ct);
        var stages = rows.ToDictionary(x => x.Id, x => ParseStages(audits.Where(a => a.EntityId == x.Id)));
        var under = rows.Count(x => Bucket(x.Status, stages[x.Id]) == "UnderReview"); var awaiting = rows.Count(x => Bucket(x.Status, stages[x.Id]) == "AwaitingReply");
        var resolved = rows.Count(x => Bucket(x.Status, stages[x.Id]) == "Resolved");
        var stopMap = await ResolveStopsMapAsync(rows, ct);
        var filtered = rows.Where(x => status == "All" || Bucket(x.Status, stages[x.Id]) == status);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var codeByStop = stopMap.Values.Where(s => s is not null).DistinctBy(s => s!.Id).ToDictionary(s => s!.Id, s => s!.OrderCode);
            filtered = filtered.Where(x => Contains(x.Title, term) || Contains(x.Description, term) || Contains(x.Category, term)
                || (stopMap.GetValueOrDefault(x.Id) is { } stop && codeByStop.TryGetValue(stop.Id, out var code) && Contains(code, term)));
        }
        var selected = filtered.OrderByDescending(c => c.SubmittedAtUtc).ThenBy(c => c.Id).ToList(); var total = selected.Count;
        var page = selected.Skip((int)Math.Min((long)(request.PageNumber - 1) * request.PageSize, int.MaxValue)).Take(request.PageSize).ToList();
        var orderCodes = stopMap.Values.Where(s => s is not null).DistinctBy(s => s!.Id).ToDictionary(s => s!.Id, s => s!.OrderCode);
        return Result.Success(new DriverSupportIssuesResponseDto(rows.Count, under, awaiting, resolved, total, request.PageNumber, request.PageSize,
            page.Select(c => new DriverSupportIssueListItemDto(c.Id, c.Category, Label(c.Category), c.Title, Bucket(c.Status, stages[c.Id]), StatusLabel(Bucket(c.Status, stages[c.Id])), c.SubmittedAtUtc, stopMap.GetValueOrDefault(c.Id) is { } stop ? orderCodes.GetValueOrDefault(stop.Id) : null)).ToArray()));
    }

    public async Task<Result<DriverSupportIssueDetailDto>> Handle(GetDriverSupportIssueQuery request, CancellationToken ct)
    {
        var profileId = await DriverProfileIdAsync(ct); if (profileId is null) return Error.Forbidden("DRIVER_SCOPE_REQUIRED", messages.GetMessage("driver_support_issue.forbidden"));
        var c = await db.Complaints.AsNoTracking().Include(x => x.EvidencePhotos).FirstOrDefaultAsync(x => x.Id == request.IssueId && x.DriverProfileId == profileId && DriverSupportIssueProjection.SupportedCategoryValues.Contains(x.Category), ct);
        if (c is null) return Error.NotFound("DRIVER_SUPPORT_ISSUE_NOT_FOUND", messages.GetMessage("driver_support_issue.not_found"));
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId == c.Id && (a.Action == "DriverSupportProgressUpdated" || a.Action == "SupportIssueResolved" || a.Action == "DriverSupportPhotosAdded")).ToListAsync(ct);
        var events = ParseStages(audits); var bucket = Bucket(c.Status, events);
        var stop = await ResolveStopAsync(c, ct);
        var orderCreatedAt = stop?.DailyOrderId is Guid dailyOrderId
            ? await db.RestaurantDailyOrders.AsNoTracking().Where(o => o.Id == dailyOrderId).Select(o => (DateTime?)o.CreatedAtUtc).FirstOrDefaultAsync(ct)
            : null;
        var resolved = audits.Where(a => a.Action == "SupportIssueResolved").OrderByDescending(a => a.OccurredAtUtc).FirstOrDefault();
        string? notes = null; DateTime? resolvedAt = null;
        if (resolved is not null) { resolvedAt = resolved.OccurredAtUtc; notes = resolved.Reason ?? JsonValue(resolved.AfterJson, "ResolutionNotes"); }
        var steps = new[] { "Submitted", "UnderReview", "ContactingCustomer", "Resolved" };
        var actual = new Dictionary<string, DateTime?> { ["Submitted"] = c.SubmittedAtUtc == default ? null : c.SubmittedAtUtc, ["UnderReview"] = EventTime(events, "UnderReview"), ["ContactingCustomer"] = EventTime(events, "ContactingCustomer"), ["Resolved"] = resolvedAt };
        var currentStage = bucket switch { "UnderReview" => "UnderReview", "AwaitingReply" => events.ContainsKey("ContactingCustomer") ? "ContactingCustomer" : "Submitted", "Resolved" => "Resolved", _ => "Submitted" };
        var updatedAt = new DateTime?[] { c.ModifiedAtUtc, audits.Select(a => (DateTime?)a.OccurredAtUtc).Max(), c.EvidencePhotos.Select(p => (DateTime?)p.UploadedAtUtc).Max() }.Max();
        return Result.Success(new DriverSupportIssueDetailDto(c.Id, c.Category, Label(c.Category), c.Title, c.Description, bucket, c.Status.ToString(), StatusLabel(bucket), c.SubmittedAtUtc,
            updatedAt, stop?.OrderCode, stop?.FullAddress,
            stop is null ? null : messages.IsArabic() ? stop.ZoneNameAr : stop.ZoneNameEn, orderCreatedAt,
            steps.Select(s => new DriverSupportIssueStageDto(s, StageLabel(s), s == currentStage ? "Current" : actual[s].HasValue ? "Completed" : "Unrecorded", actual[s])).ToArray(),
            c.EvidencePhotos.OrderBy(x => x.UploadedAtUtc).Select(x => CatalogImageStorage.ToContentUrl(x.StoragePath) ?? string.Empty).ToArray(), notes, resolvedAt, false, null));
    }

    private async Task<Guid?> DriverProfileIdAsync(CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserType != UserType.Driver || !user.UserId.HasValue) return null;
        return await db.DriverProfiles.AsNoTracking().Where(d => d.UserId == user.UserId.Value && !d.IsDeleted).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
    }
    private async Task<Dictionary<Guid, TripStop?>> ResolveStopsMapAsync(List<Complaint> complaints, CancellationToken ct)
    {
        var ids = complaints.SelectMany(c => new Guid?[] { c.ReassignmentTripStopId, c.OrderId, c.SubscriptionDayId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var stops = ids.Length == 0 ? new List<TripStop>() : await db.TripStops.AsNoTracking().Include(s => s.Trip)
            .Where(s => ids.Contains(s.Id) || s.DailyOrderId.HasValue && ids.Contains(s.DailyOrderId.Value) || s.SubscriptionDayId.HasValue && ids.Contains(s.SubscriptionDayId.Value)).ToListAsync(ct);
        var map = new Dictionary<Guid, TripStop?>();
        foreach (var c in complaints)
        {
            TripStop? stop = c.ReassignmentTripStopId.HasValue ? stops.FirstOrDefault(s => s.Id == c.ReassignmentTripStopId && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId)) : null;
            stop ??= c.OrderId.HasValue ? stops.FirstOrDefault(s => s.Id == c.OrderId && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId)) : null;
            if (stop is null)
            {
                var candidates = stops.Where(s => (c.OrderId.HasValue && s.DailyOrderId == c.OrderId || c.SubscriptionDayId.HasValue && s.SubscriptionDayId == c.SubscriptionDayId)
                    && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId)).Take(2).ToList();
                if (candidates.Count == 1) stop = candidates[0];
            }
            map[c.Id] = stop;
        }
        return map;
    }
    private async Task<TripStop?> ResolveStopAsync(Complaint c, CancellationToken ct)
    {
        if (c.ReassignmentTripStopId.HasValue) { var x = await db.TripStops.AsNoTracking().Include(s => s.Trip).FirstOrDefaultAsync(s => s.Id == c.ReassignmentTripStopId && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId), ct); if (x is not null) return x; }
        if (c.OrderId.HasValue) { var x = await db.TripStops.AsNoTracking().Include(s => s.Trip).FirstOrDefaultAsync(s => s.Id == c.OrderId && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId), ct); if (x is not null) return x; }
        var matches = await db.TripStops.AsNoTracking().Include(s => s.Trip).Where(s => (c.OrderId.HasValue && s.DailyOrderId == c.OrderId || c.SubscriptionDayId.HasValue && s.SubscriptionDayId == c.SubscriptionDayId)
            && (!c.RestaurantId.HasValue || s.Trip.RestaurantId == c.RestaurantId)).Take(2).ToListAsync(ct);
        return matches.Count == 1 ? matches[0] : null;
    }
    private Dictionary<string, DateTime> ParseStages(IEnumerable<MealMate.Domain.IdentityAccess.AuditLog> audits)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var a in audits.Where(a => a.Action == "DriverSupportProgressUpdated").OrderBy(a => a.OccurredAtUtc))
        { var stage = DriverSupportIssueProjection.StageFromAuditJson(a.AfterJson); if (stage is "UnderReview" or "ContactingCustomer") result[stage] = a.OccurredAtUtc; }
        return result;
    }
    private static string? JsonValue(string? json, string key) { if (json is null) return null; try { using var document = System.Text.Json.JsonDocument.Parse(json); return document.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null; } catch (System.Text.Json.JsonException) { return null; } }
    private static DateTime? EventTime(Dictionary<string, DateTime> events, string stage) => events.TryGetValue(stage, out var value) ? value : null;
    private static bool Contains(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    private static string Bucket(ComplaintStatus s, Dictionary<string, DateTime>? events = null) => DriverSupportIssueProjection.Bucket(s, events?.ContainsKey("ContactingCustomer") == true);
    private string Label(string category) => messages.GetMessage($"driver_support_issue.category.{category}");
    private string StatusLabel(string status) => messages.GetMessage($"driver_support_issue.status.{status}");
    private string StageLabel(string stage) => messages.GetMessage($"driver_support_issue.stage.{stage}");
}
