using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

// ──────────────────────────────────────────────────────────────────────────────
// Safe DTOs – never expose submitted/reviewer user ids, storage keys,
// content type, size, or other internals.
// ──────────────────────────────────────────────────────────────────────────────

public sealed record MyRegistrationDocumentSummaryDto(
    Guid DocumentId,
    DocumentType DocumentType,
    string FileName,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    VerificationStatus? LatestVerificationStatus,
    string? LatestVerificationReason,
    DateTime? LatestVerificationReviewedAtUtc);

public sealed record MyRestaurantRegistrationDto(
    Guid Id,
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? ContactPhone,
    string? ContactEmail,
    string? CommercialRegistrationNumber,
    DateOnly? CommercialRegistrationExpiry,
    string? CommercialLicenseNumber,
    DateOnly? CommercialLicenseExpiry,
    string? FoodLicenseNumber,
    DateOnly? FoodLicenseExpiry,
    string? LegalEntityType,
    string? CompanyCivilId,
    string? Website,
    string? InstagramHandle,
    string? RestaurantCode,
    string? OwnerName,
    string? AuthorizedSignatoryCivilId,
    string? AuthorizedSignatoryTitle,
    string? PrimaryContactName,
    string? PrimaryContactTitle,
    string? PrimaryContactPhone,
    string? PrimaryContactEmail,
    string? BankName,
    string? BankBeneficiaryName,
    string? Iban,
    string? BankAccountNumber,
    string? SwiftCode,
    string? SettlementCurrency,
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    VerificationStatus Status,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingRestaurantId,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    DateTime? ReviewedAtUtc,
    bool? DeliveryByRestaurantConfirmed,
    bool? DataResponsibilityConfirmed,
    DateOnly? SubmissionDate,
    string? RestaurantNotes,
    IReadOnlyList<MyRegistrationDocumentSummaryDto> Documents,
    IReadOnlyList<FieldReviewSummaryDto>? FieldReviews = null,
    IReadOnlyList<Guid>? ServiceRegionIds = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

// ──────────────────────────────────────────────────────────────────────────────
// Query: GET /api/v1/restaurants/registrations/me
// Returns the newest registration owned by the current authenticated user.
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetMyRestaurantRegistrationQuery : IQuery<MyRestaurantRegistrationDto>;

public sealed class GetMyRestaurantRegistrationQueryHandler
    : IQueryHandler<GetMyRestaurantRegistrationQuery, MyRestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMyRestaurantRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<MyRestaurantRegistrationDto>> Handle(
        GetMyRestaurantRegistrationQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");
        }

        var userId = _currentUser.UserId.Value;

        var registration = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r => r.SubmittedByUserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "No registration found for the current user.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var summaries = docs.Select(d =>
        {
            var latest = d.Verifications
                .OrderByDescending(v => v.CreatedAtUtc)
                .FirstOrDefault();

            return new MyRegistrationDocumentSummaryDto(
                d.Id,
                d.DocumentType,
                d.FileName,
                d.CreatedAtUtc,
                d.ExpiresAtUtc,
                latest?.Status,
                latest?.Reason,
                latest?.ReviewedAtUtc);
        }).ToList();

        // Load field reviews; materialize for legacy rows
        var fieldReviews = await _db.RestaurantRegistrationFieldReviews
            .AsNoTracking()
            .Where(f => f.RegistrationId == registration.Id)
            .ToListAsync(cancellationToken);

        if (fieldReviews.Count == 0 && registration.Status is not VerificationStatus.Draft)
        {
            fieldReviews = RegistrationFieldKeys.All
                .Select(fk => new RestaurantRegistrationFieldReview
                {
                    FieldKey = fk,
                    Status = VerificationStatus.Submitted,
                })
                .ToList();
        }

        var fieldSummaries = fieldReviews
            .Select(RestaurantRegistrationMapping.MapFieldReview)
            .ToList();

        return new MyRestaurantRegistrationDto(
            registration.Id,
            registration.LegalNameAr,
            registration.LegalNameEn,
            registration.TradeNameAr,
            registration.TradeNameEn,
            registration.ContactPhone,
            registration.ContactEmail,
            registration.CommercialRegistrationNumber,
            registration.CommercialRegistrationExpiry,
            registration.CommercialLicenseNumber,
            registration.CommercialLicenseExpiry,
            registration.FoodLicenseNumber,
            registration.FoodLicenseExpiry,
            RestaurantRegistrationRules.NormalizeLegalEntityTypeForOutput(registration.LegalEntityType),
            registration.CompanyCivilId,
            registration.Website,
            registration.InstagramHandle,
            string.IsNullOrWhiteSpace(registration.RestaurantCode)
                ? RestaurantRegistrationRules.CreateRestaurantCode(registration.Id)
                : registration.RestaurantCode,
            registration.OwnerName,
            registration.AuthorizedSignatoryCivilId,
            registration.AuthorizedSignatoryTitle,
            registration.PrimaryContactName,
            registration.PrimaryContactTitle,
            registration.PrimaryContactPhone,
            registration.PrimaryContactEmail,
            registration.BankName,
            registration.BankBeneficiaryName,
            registration.Iban,
            registration.BankAccountNumber,
            registration.SwiftCode,
            RestaurantRegistrationRules.SettlementCurrency,
            registration.Address,
            registration.CountryId,
            registration.RegionId,
            registration.Status,
            registration.RejectionReason,
            registration.ChangeRequestNotes,
            registration.ResultingRestaurantId,
            registration.CreatedAtUtc,
            registration.ModifiedAtUtc,
            registration.ReviewedAtUtc,
            registration.DeliveryByRestaurantConfirmed,
            registration.DataResponsibilityConfirmed,
            registration.SubmissionDate,
            registration.RestaurantNotes,
            summaries,
            fieldSummaries,
            RestaurantRegistrationMapping.ParseServiceRegionIds(registration.ServiceRegionIdsJson),
            registration.Latitude,
            registration.Longitude);
    }
}
