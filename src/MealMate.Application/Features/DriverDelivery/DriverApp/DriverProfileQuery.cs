using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverProfileResponseDto(
    Guid DriverProfileId, string FullNameAr, string FullNameEn, string FullName,
    string? PhoneNumber, string DriverCode, string? ProfileImageStorageKey, string? ProfileImageUrl,
    bool IsOnline, string Status, string StatusText, decimal? AverageRating, int ReviewsCount,
    int TotalOrders, decimal? AcceptanceRatePercent, DateTime? JoinedAtUtc, string? DriverRank,
    string DriverDescription, DriverProfileVehicleDto Vehicle, IReadOnlyList<DriverProfileDocumentDto> Documents,
    DriverProfileAssignmentDto? Assignment, DriverProfileSupportTicketDto? LatestSupportTicket);

public sealed record DriverProfileVehicleDto(
    string? VehicleType, string? VehicleTypeLocalized, string? VehicleModel, string? VehiclePlate,
    string? PlateGovernorate, int? VehicleYear, string? VehicleColor, string? VehicleColorLocalized,
    string? VerificationStatus, string? VerificationStatusText);

public sealed record DriverProfileDocumentDto(
    Guid DocumentId, string DocumentType, string DocumentTitle, string Status, string StatusText,
    string? VerificationStatus, DateOnly? ExpiryDate, int? DaysUntilExpiry, bool RequiresRenewal);

public sealed record DriverProfileAssignmentDto(
    Guid? RestaurantId, string? RestaurantName, Guid? BranchId, string? BranchName);

public sealed record DriverProfileSupportTicketDto(
    Guid TicketId, string Subject, string Body, string Status, string Priority, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);

public sealed record GetDriverProfileQuery : IQuery<DriverProfileResponseDto>;

public sealed class GetDriverProfileQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IUserAccountService accounts,
    IFileStorage storage, ILocalizedMessageProvider messages)
    : IQueryHandler<GetDriverProfileQuery, DriverProfileResponseDto>
{
    public async Task<Result<DriverProfileResponseDto>> Handle(GetDriverProfileQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
            return Error.Unauthorized(MessageCodes.Common.Unauthorized);
        if (!currentUser.IsInRole(AppRoles.Driver))
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var userId = currentUser.UserId.Value;
        // Read-only ownership lookup. Never accept a driver ID from the client or repair ownership during a GET.
        var driver = await db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
        if (driver is null)
            return Error.NotFound(MessageCodes.Dispatcher.DriverNotFound);

        var registrations = await db.DriverRegistrations.AsNoTracking()
            .Where(r => r.ResultingDriverProfileId == driver.Id)
            .OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        var registration = registrations.FirstOrDefault();
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        var imageKey = driver.ProfileImageStorageKey ?? registration?.ProfileImageStorageKey;
        var imageUrl = string.IsNullOrWhiteSpace(imageKey) ? null
            : await storage.GetReadUrlAsync(imageKey, TimeSpan.FromMinutes(15), cancellationToken);

        var isOnline = await db.DriverWorkSessions.AsNoTracking()
            .AnyAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active" && s.EndedAtUtc == null, cancellationToken);
        var ratings = await db.DriverRatings.AsNoTracking().Where(r => r.DriverProfileId == driver.Id)
            .Select(r => (decimal)r.Score).ToListAsync(cancellationToken);

        // Attribute a stop to the most recent assignment at delivery time; reassignments must not double-count orders.
        var totalOrders = await db.TripStops.AsNoTracking()
            .Where(s => s.Trip.DriverAssignments
                .Where(a => s.DeliveredAtUtc == null || a.AssignedAtUtc <= s.DeliveredAtUtc)
                .OrderByDescending(a => a.AssignedAtUtc).ThenByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
                .Select(a => a.DriverProfileId).FirstOrDefault() == driver.Id)
            .CountAsync(cancellationToken);

        var registrationIds = registrations.Select(r => r.Id).ToArray();
        var storedDocuments = await db.Documents.AsNoTracking().Include(d => d.Verifications)
            .Where(d => (d.OwnerType == DocumentOwnerType.Driver && d.OwnerId == driver.Id)
                || (d.OwnerType == DocumentOwnerType.Registration && registrationIds.Contains(d.OwnerId)))
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).ToListAsync(cancellationToken);
        // Renewals supersede the earlier upload of the same document type.
        var latestDocuments = storedDocuments.GroupBy(d => d.DocumentType).Select(g => g.First()).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var documents = latestDocuments
            .Where(d => d.DocumentType is not DocumentType.ProfileImage and not DocumentType.VehiclePhoto and not DocumentType.BranchPhoto and not DocumentType.MealPhoto)
            .Select(d => MapDocument(d, driver, today)).OrderBy(d => d.DocumentType).ToList();

        // There is no independent fleet approval field. Expose vehicle registration verification only.
        var vehicleRegistration = latestDocuments.FirstOrDefault(d => d.DocumentType == DocumentType.VehicleRegistration);
        var vehicleVerification = vehicleRegistration is null ? null : LatestReview(vehicleRegistration)?.Status.ToString();
        var vehicleType = driver.VehicleType ?? registration?.VehicleType;
        var typeLabel = VehicleCatalogData.Types.FirstOrDefault(t => string.Equals(t.Code, vehicleType, StringComparison.OrdinalIgnoreCase));
        var color = driver.VehicleColor ?? registration?.VehicleColor;
        var colorLabel = VehicleCatalogData.Colors.FirstOrDefault(c => string.Equals(c.Hex, color, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.NameAr, color, StringComparison.OrdinalIgnoreCase) || string.Equals(c.NameEn, color, StringComparison.OrdinalIgnoreCase));
        var vehicle = new DriverProfileVehicleDto(vehicleType,
            typeLabel is null ? vehicleType : messages.Localize(typeLabel.NameAr, typeLabel.NameEn),
            driver.VehicleModel ?? registration?.VehicleModel, driver.VehiclePlate ?? registration?.VehiclePlate,
            null, driver.VehicleYear ?? registration?.VehicleYear, color,
            colorLabel is null ? color : messages.Localize(colorLabel.NameAr, colorLabel.NameEn),
            vehicleVerification, vehicleVerification is null ? null : Label("verification", vehicleVerification));

        var restaurant = driver.RestaurantId.HasValue
            ? await db.RestaurantProfiles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == driver.RestaurantId, cancellationToken)
            : null;
        var branch = await (from assignment in db.BranchStaffAssignments.AsNoTracking()
                            join b in db.RestaurantBranches.AsNoTracking() on assignment.RestaurantBranchId equals b.Id
                            where assignment.UserId == userId && assignment.IsActive && b.IsActive
                                && (!driver.RestaurantId.HasValue || db.RestaurantRegistrations.Any(r =>
                                    r.Id == b.RestaurantRegistrationId && r.ResultingRestaurantId == driver.RestaurantId))
                            orderby b.IsPrimary descending, assignment.CreatedAtUtc descending, b.Id
                            select b).FirstOrDefaultAsync(cancellationToken);
        var assignmentDto = restaurant is null && branch is null ? null : new DriverProfileAssignmentDto(
            restaurant?.Id, restaurant is null ? null : messages.Localize(restaurant.TradeNameAr, restaurant.TradeNameEn),
            branch?.Id, branch is null ? null : messages.Localize(branch.NameAr, branch.NameEn));

        var ticket = await db.Set<SupportTicket>().AsNoTracking()
            .Where(t => t.CreatedByUserId == userId || t.CustomerUserId == userId)
            .OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id).FirstOrDefaultAsync(cancellationToken);
        var ticketDto = ticket is null ? null : new DriverProfileSupportTicketDto(
            ticket.Id, ticket.Subject, ticket.Body, ticket.Status, ticket.Priority, ticket.CreatedAtUtc, ticket.ModifiedAtUtc);
        var status = isOnline ? "Online" : "Offline";
        var descriptionKey = driver.VerificationStatus == VerificationStatus.Approved ? "approved_driver" : "driver";
        return new DriverProfileResponseDto(driver.Id, driver.FullNameAr, driver.FullNameEn,
            messages.Localize(driver.FullNameAr, driver.FullNameEn), account?.PhoneNumber ?? registration?.Phone,
            driver.Id.ToString("N"), imageKey, imageUrl, isOnline, status, Label("status", status),
            ratings.Count == 0 ? null : Math.Round(ratings.Average(), 2), ratings.Count, totalOrders,
            null, driver.ActivatedAtUtc ?? account?.CreatedAtUtc, null,
            Label("description", descriptionKey), vehicle, documents, assignmentDto, ticketDto);
    }

    private DriverProfileDocumentDto MapDocument(Document document, DriverProfile driver, DateOnly today)
    {
        var expires = document.ExpiresAtUtc ?? (document.DocumentType switch
        {
            DocumentType.DrivingLicense or DocumentType.DrivingLicenseFront or DocumentType.DrivingLicenseBack => driver.LicenseExpiryUtc,
            DocumentType.NationalId or DocumentType.NationalIdFront or DocumentType.NationalIdBack => driver.NationalIdExpiryUtc,
            DocumentType.VehicleRegistration => driver.VehicleRegistrationExpiryUtc,
            DocumentType.Contract or DocumentType.EmploymentContract => driver.ContractExpiryUtc,
            _ => null
        });
        DateOnly? expiry = expires.HasValue ? DateOnly.FromDateTime(expires.Value) : null;
        int? days = expiry?.DayNumber - today.DayNumber;
        var verification = LatestReview(document)?.Status.ToString();
        var status = days < 0 ? "Expired" : days <= 30 ? "ExpiringSoon" : verification ?? "Unreviewed";
        return new DriverProfileDocumentDto(document.Id, document.DocumentType.ToString(),
            Label("document", document.DocumentType.ToString()), status, Label("verification", status),
            verification, expiry, days, days <= 30);
    }

    private static DocumentVerification? LatestReview(Document document) => document.Verifications
        .OrderByDescending(v => v.ReviewedAtUtc ?? v.CreatedAtUtc).ThenByDescending(v => v.CreatedAtUtc).ThenByDescending(v => v.Id)
        .FirstOrDefault();

    private string Label(string group, string code) => messages.GetMessage($"driver.profile.{group}.{code}", code);
}
