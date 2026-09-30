using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Application.Features.Catalog;
using System.Text.Json;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

/// <summary>Summary of a per-field review.</summary>
public sealed record FieldReviewSummaryDto(
    string FieldKey,
    VerificationStatus Status,
    string? Reason,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc);

/// <summary>Canonical field keys for restaurant registration review.</summary>
public static class RegistrationFieldKeys
{
    // Identity / licenses
    public const string LegalNameAr = "legalNameAr";
    public const string LegalNameEn = "legalNameEn";
    public const string TradeNameAr = "tradeNameAr";
    public const string TradeNameEn = "tradeNameEn";
    public const string RestaurantCode = "restaurantCode";
    public const string LegalEntityType = "legalEntityType";
    public const string CompanyCivilId = "companyCivilId";
    public const string CommercialRegistrationNumber = "commercialRegistrationNumber";
    public const string CommercialRegistrationExpiry = "commercialRegistrationExpiry";
    public const string CommercialLicenseNumber = "commercialLicenseNumber";
    public const string CommercialLicenseExpiry = "commercialLicenseExpiry";
    public const string FoodLicenseNumber = "foodLicenseNumber";
    public const string FoodLicenseExpiry = "foodLicenseExpiry";
    public const string HeadquartersAddressAr = "headquartersAddressAr";
    public const string ContactPhone = "contactPhone";
    public const string ContactEmail = "contactEmail";
    public const string Website = "website";
    public const string InstagramHandle = "instagramHandle";

    // Contact / signatory
    public const string PrimaryContactName = "primaryContactName";
    public const string PrimaryContactTitle = "primaryContactTitle";
    public const string PrimaryContactPhone = "primaryContactPhone";
    public const string PrimaryContactEmail = "primaryContactEmail";
    public const string OwnerName = "ownerName";
    public const string AuthorizedSignatoryCivilId = "authorizedSignatoryCivilId";
    public const string AuthorizedSignatoryTitle = "authorizedSignatoryTitle";

    // Bank
    public const string BankName = "bankName";
    public const string BeneficiaryName = "beneficiaryName";
    public const string Iban = "iban";
    public const string AccountNumber = "accountNumber";
    public const string SwiftCode = "swiftCode";
    public const string SettlementCurrency = "settlementCurrency";

    // Geographic
    public const string CountryId = "countryId";
    public const string RegionId = "regionId";
    public const string Latitude = "latitude";
    public const string Longitude = "longitude";

    /// <summary>All canonical keys grouped conceptually (identity, contact, bank).</summary>
    public static readonly string[] All =
    [
        // Identity / licenses
        LegalNameAr, LegalNameEn, TradeNameAr, TradeNameEn,
        LegalEntityType, CompanyCivilId,
        CommercialRegistrationNumber, CommercialRegistrationExpiry,
        CommercialLicenseNumber, CommercialLicenseExpiry,
        FoodLicenseNumber, FoodLicenseExpiry,
        HeadquartersAddressAr, ContactPhone, ContactEmail,
        Website, InstagramHandle,
        // Contact / signatory
        PrimaryContactName, PrimaryContactTitle, PrimaryContactPhone, PrimaryContactEmail,
        OwnerName, AuthorizedSignatoryCivilId, AuthorizedSignatoryTitle,
        // Bank
        BankName, BeneficiaryName, Iban, AccountNumber, SwiftCode,
        // Geographic
        CountryId, RegionId, Latitude, Longitude,
    ];

    /// <summary>Keys that are required by the onboarding template before submission.</summary>
    public static readonly string[] RequiredForSubmission =
    [
        LegalNameAr, LegalNameEn, TradeNameAr, TradeNameEn,
        LegalEntityType, CompanyCivilId,
        CommercialRegistrationNumber, CommercialRegistrationExpiry,
        CommercialLicenseNumber, CommercialLicenseExpiry,
        FoodLicenseNumber, FoodLicenseExpiry,
        HeadquartersAddressAr, ContactPhone, ContactEmail,
        PrimaryContactName, PrimaryContactTitle, PrimaryContactPhone, PrimaryContactEmail,
        OwnerName, AuthorizedSignatoryCivilId, AuthorizedSignatoryTitle,
        BankName, BeneficiaryName, Iban, AccountNumber,
        Latitude, Longitude,
    ];

    private static readonly HashSet<string> AllSet = new(All);

    public static bool IsValid(string fieldKey) => AllSet.Contains(fieldKey);

    /// <summary>
    /// Returns the canonical keys whose corresponding field is non-null on the registration.
    /// Excludes optional fields (countryId, regionId, website, instagramHandle, swiftCode)
    /// when they are null.
    /// </summary>
    public static IReadOnlyList<string> RequiredForApproval(Domain.IdentityAccess.RestaurantRegistration r)
    {
        var required = new List<string>(All);
        required.Remove(Latitude);
        required.Remove(Longitude);
        RemoveIfNull(ref required, r.CountryId, CountryId);
        RemoveIfNull(ref required, r.RegionId, RegionId);
        RemoveIfNull(ref required, r.Website, Website);
        RemoveIfNull(ref required, r.InstagramHandle, InstagramHandle);
        RemoveIfNull(ref required, r.SwiftCode, SwiftCode);
        return required;
    }

    private static void RemoveIfNull(ref List<string> list, string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value)) list.Remove(key);
    }

    private static void RemoveIfNull(ref List<string> list, Guid? value, string key)
    {
        if (value is null) list.Remove(key);
    }
}

internal static class RestaurantRegistrationRules
{
    public const string SettlementCurrency = "KWD";

    public static string CreateRestaurantCode(Guid registrationId) =>
        $"RST-{registrationId:N}".ToUpperInvariant();

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? NormalizeCommercialRegistrationNumber(string? value) =>
        NormalizeOptional(value)?.ToUpperInvariant();

    public static string? NormalizeIban(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    public static bool IsValidKuwaitIban(string? value)
    {
        var normalized = NormalizeIban(value);
        if (normalized is not { Length: 30 }
            || !normalized.StartsWith("KW", StringComparison.Ordinal)
            || !char.IsAsciiDigit(normalized[2])
            || !char.IsAsciiDigit(normalized[3])
            || !normalized.AsSpan(4, 4).ToArray().All(char.IsAsciiLetterUpper)
            || !normalized.AsSpan(8).ToArray().All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        var rearranged = normalized[4..] + normalized[..4];
        var remainder = 0;
        foreach (var character in rearranged)
        {
            if (char.IsAsciiDigit(character))
            {
                remainder = ((remainder * 10) + (character - '0')) % 97;
            }
            else
            {
                remainder = ((remainder * 100) + (character - 'A' + 10)) % 97;
            }
        }

        return remainder == 1;
    }

    public static bool IsValidEmail(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized is not null
               && System.Net.Mail.MailAddress.TryCreate(normalized, out var address)
               && string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidHttpsWebsite(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null) return true;

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }

    /// <summary>Loose Kuwait bounding box used to reject clearly invalid map pins.</summary>
    public static bool IsWithinKuwaitBounds(decimal? latitude, decimal? longitude)
        => MealMate.Application.Common.Geography.KuwaitGeoBounds.IsWithin(latitude, longitude);

    public static bool TryNormalizeLegalEntityType(string? value, out string? normalized)
    {
        var trimmed = NormalizeOptional(value);
        if (trimmed is null)
        {
            normalized = null;
            return true;
        }

        var key = trimmed
            .ToLowerInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        normalized = key switch
        {
            "company" or "corporation" or "corp" or "llc" or "limitedliabilitycompany"
                or "شركة" or "شركةذاتمسؤوليةمحدودة" => "company",
            "establishment" or "institution" or "enterprise" or "soleproprietorship"
                or "مؤسسة" or "مؤسسه" or "مؤسسةفردية" or "مؤسسهفرديه" => "establishment",
            _ => null,
        };
        return normalized is not null;
    }

    public static string? NormalizeLegalEntityTypeForOutput(string? value) =>
        TryNormalizeLegalEntityType(value, out var normalized)
            ? normalized
            : NormalizeOptional(value);

    public static string? MaskSensitive(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null) return null;
        return normalized.Length <= 4
            ? new string('*', normalized.Length)
            : $"****{normalized[^4..]}";
    }
}

public sealed record RestaurantRegistrationDto(
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
    Guid? SubmittedByUserId,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId,
    bool? DeliveryByRestaurantConfirmed,
    bool? DataResponsibilityConfirmed,
    DateOnly? SubmissionDate,
    string? RestaurantNotes,
    IReadOnlyList<FieldReviewSummaryDto>? FieldReviews = null,
    IReadOnlyList<Guid>? ServiceRegionIds = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    IReadOnlyList<Guid>? SelectedProgramIds = null,
    IReadOnlyList<Guid>? SelectedBundleIds = null);

public sealed record CreateRestaurantRegistrationCommand(
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
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    string? Website = null,
    string? InstagramHandle = null,
    string? SwiftCode = null,
    bool? DeliveryByRestaurantConfirmed = null,
    bool? DataResponsibilityConfirmed = null,
    string? RestaurantNotes = null,
    List<string>? ServiceRegionIds = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    List<Guid>? SelectedProgramIds = null,
    List<Guid>? SelectedBundleIds = null) : ICommand<RestaurantRegistrationDto>;

public sealed class CreateRestaurantRegistrationCommandValidator : AbstractValidator<CreateRestaurantRegistrationCommand>
{
    public CreateRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.ContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.LegalEntityType)
            .Must(value => RestaurantRegistrationRules.TryNormalizeLegalEntityType(value, out _))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.PrimaryContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.Iban)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidKuwaitIban(value))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Website)
            .Must(RestaurantRegistrationRules.IsValidHttpsWebsite)
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);

        RuleFor(x => x.LegalNameAr).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.LegalNameEn).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.TradeNameAr).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.TradeNameEn).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.ContactPhone).MaximumLength(30).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.ContactEmail).MaximumLength(256).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.PrimaryContactEmail).MaximumLength(256).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.Website).MaximumLength(500).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.RestaurantNotes).MaximumLength(2000).WithMessageCode(MessageCodes.Validation.MaxLength);
    }
}

public sealed class CreateRestaurantRegistrationCommandHandler
    : ICommandHandler<CreateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;
    private readonly IRegistrationPushNotificationService? _pushNotifier;

    public CreateRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        KuwaitTenantResolver tenantResolver,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _tenantResolver = tenantResolver;
        _pushNotifier = pushNotifier;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        CreateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated
            || _currentUser.UserId is null
            || _currentUser.UserType != UserType.Restaurant)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication is required.");
        }

        var tenantResult = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResult.IsFailure) return Result.Failure<RestaurantRegistrationDto>(tenantResult.Error);

        var hasBlockingRegistration = await _db.RestaurantRegistrations.AnyAsync(
            r => r.SubmittedByUserId == _currentUser.UserId
                 && r.Status != VerificationStatus.Expired
                 && r.Status != VerificationStatus.Rejected,
            cancellationToken);
        var hasRestaurantProfile = await _db.RestaurantProfiles.AnyAsync(
            profile => profile.UserId == _currentUser.UserId,
            cancellationToken);
        if (hasBlockingRegistration || hasRestaurantProfile)
        {
            return Error.Conflict(
                MessageCodes.Auth.RestaurantRegistrationExists,
                "An open restaurant registration already exists for this account.");
        }

        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(request.LegalEntityType, out var legalEntityType))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");
        }

        var commercialRegistrationNumber = RestaurantRegistrationRules.NormalizeCommercialRegistrationNumber(
            request.CommercialRegistrationNumber);
        if (commercialRegistrationNumber is not null
            && await _db.RestaurantRegistrations.AnyAsync(
                r => r.CommercialRegistrationNumber != null
                     && r.CommercialRegistrationNumber.Trim().ToUpper() == commercialRegistrationNumber
                     && r.Status != VerificationStatus.Rejected
                     && r.Status != VerificationStatus.Expired,
                cancellationToken))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var selection = await RegionCatalogRules.ValidateRestaurantSelectionAsync(
            _db, request.CountryId ?? tenantResult.Value.CountryId, request.RegionId, request.ServiceRegionIds, false, tenantResult.Value.CountryId, cancellationToken);
        if (selection.Error is not null) return Result.Failure<RestaurantRegistrationDto>(selection.Error);
        var serviceRegionIds = selection.Ids.Select(x => x.ToString()).ToList();

        var registration = new Domain.IdentityAccess.RestaurantRegistration
        {
            SubmittedByUserId = _currentUser.UserId,
            LegalNameAr = RestaurantRegistrationRules.NormalizeOptional(request.LegalNameAr),
            LegalNameEn = RestaurantRegistrationRules.NormalizeOptional(request.LegalNameEn),
            TradeNameAr = RestaurantRegistrationRules.NormalizeOptional(request.TradeNameAr),
            TradeNameEn = RestaurantRegistrationRules.NormalizeOptional(request.TradeNameEn),
            ContactPhone = RestaurantRegistrationRules.NormalizeOptional(request.ContactPhone),
            ContactEmail = RestaurantRegistrationRules.NormalizeOptional(request.ContactEmail)?.ToLowerInvariant(),
            CommercialRegistrationNumber = commercialRegistrationNumber,
            CommercialRegistrationExpiry = request.CommercialRegistrationExpiry,
            CommercialLicenseNumber = RestaurantRegistrationRules.NormalizeOptional(request.CommercialLicenseNumber),
            CommercialLicenseExpiry = request.CommercialLicenseExpiry,
            FoodLicenseNumber = RestaurantRegistrationRules.NormalizeOptional(request.FoodLicenseNumber),
            FoodLicenseExpiry = request.FoodLicenseExpiry,
            LegalEntityType = legalEntityType,
            CompanyCivilId = RestaurantRegistrationRules.NormalizeOptional(request.CompanyCivilId),
            Website = RestaurantRegistrationRules.NormalizeOptional(request.Website),
            InstagramHandle = RestaurantRegistrationRules.NormalizeOptional(request.InstagramHandle),
            RestaurantCode = null,
            OwnerName = RestaurantRegistrationRules.NormalizeOptional(request.OwnerName),
            AuthorizedSignatoryCivilId = RestaurantRegistrationRules.NormalizeOptional(request.AuthorizedSignatoryCivilId),
            AuthorizedSignatoryTitle = RestaurantRegistrationRules.NormalizeOptional(request.AuthorizedSignatoryTitle),
            PrimaryContactName = RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactName),
            PrimaryContactTitle = RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactTitle),
            PrimaryContactPhone = RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactPhone),
            PrimaryContactEmail = RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactEmail)?.ToLowerInvariant(),
            BankName = RestaurantRegistrationRules.NormalizeOptional(request.BankName),
            BankBeneficiaryName = RestaurantRegistrationRules.NormalizeOptional(request.BankBeneficiaryName),
            Iban = RestaurantRegistrationRules.NormalizeIban(request.Iban),
            BankAccountNumber = RestaurantRegistrationRules.NormalizeOptional(request.BankAccountNumber),
            SwiftCode = RestaurantRegistrationRules.NormalizeOptional(request.SwiftCode)?.ToUpperInvariant(),
            SettlementCurrency = RestaurantRegistrationRules.SettlementCurrency,
            Address = RestaurantRegistrationRules.NormalizeOptional(request.Address),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CountryId = tenantResult.Value.CountryId,
            RegionId = request.RegionId,
            ServiceRegionIdsJson = serviceRegionIds.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(serviceRegionIds) : null,
            SelectedProgramIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SelectedProgramIds?.Distinct().ToArray() ?? []),
            SelectedBundleIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SelectedBundleIds?.Distinct().ToArray() ?? []),
            Status = VerificationStatus.Draft,
            DeliveryByRestaurantConfirmed = request.DeliveryByRestaurantConfirmed,
            DataResponsibilityConfirmed = request.DataResponsibilityConfirmed,
            SubmissionDate = null,
            RestaurantNotes = RestaurantRegistrationRules.NormalizeOptional(request.RestaurantNotes),
            CreatedAtUtc = _clock.UtcNow,
        };

        // The code is server-owned and derived from the already-generated entity id,
        // making it stable without a race-prone "next number" query.
        registration.RestaurantCode = RestaurantRegistrationRules.CreateRestaurantCode(registration.Id);

        _db.RestaurantRegistrations.Add(registration);
        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationCreated",
            "RestaurantRegistration",
            registration.Id,
            After: RestaurantRegistrationMapping.MapAuditSnapshot(registration),
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record UpdateRestaurantRegistrationCommand(
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
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    string? Website = null,
    string? InstagramHandle = null,
    bool? DeliveryByRestaurantConfirmed = null,
    bool? DataResponsibilityConfirmed = null,
    string? RestaurantNotes = null,
    List<string>? ServiceRegionIds = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    List<Guid>? SelectedProgramIds = null,
    List<Guid>? SelectedBundleIds = null) : ICommand<RestaurantRegistrationDto>;

public sealed class UpdateRestaurantRegistrationCommandValidator : AbstractValidator<UpdateRestaurantRegistrationCommand>
{
    public UpdateRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.ContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.PrimaryContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.LegalEntityType)
            .Must(value => RestaurantRegistrationRules.TryNormalizeLegalEntityType(value, out _))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Iban)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidKuwaitIban(value))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Website)
            .Must(RestaurantRegistrationRules.IsValidHttpsWebsite)
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);

        RuleFor(x => x.LegalNameAr).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.LegalNameEn).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.TradeNameAr).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.TradeNameEn).MaximumLength(200).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.ContactPhone).MaximumLength(30).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.ContactEmail).MaximumLength(256).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.PrimaryContactEmail).MaximumLength(256).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.Website).MaximumLength(500).WithMessageCode(MessageCodes.Validation.MaxLength);
        RuleFor(x => x.RestaurantNotes).MaximumLength(2000).WithMessageCode(MessageCodes.Validation.MaxLength);
    }
}

public sealed class UpdateRestaurantRegistrationCommandHandler
    : ICommandHandler<UpdateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;

    public UpdateRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        KuwaitTenantResolver tenantResolver)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _tenantResolver = tenantResolver;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        UpdateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        var correctionDocuments = registration.Status == VerificationStatus.UnderReview
            ? await _db.Documents
                .Include(d => d.Verifications)
                .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                .ToListAsync(cancellationToken)
            : [];
        var hasOutstandingCorrection = registration.FieldReviews.Any(f =>
                f.Status is VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired)
            || correctionDocuments.Any(d => d.Verifications
                .OrderByDescending(v => v.CreatedAtUtc)
                .FirstOrDefault()?.Status is VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired);

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired)
            && !(registration.Status == VerificationStatus.UnderReview && hasOutstandingCorrection))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be edited in current status.");
        }

        if (_currentUser.UserId is not Guid ownerUserId
            || registration.SubmittedByUserId != ownerUserId)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        // PUT is a full replacement of the editable snapshot. This lets a draft
        // intentionally clear values with JSON null; submission remains the gate
        // that requires all mandatory fields to be populated.
        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(request.LegalEntityType, out var legalEntityType))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");
        }

        var commercialRegistrationNumber = RestaurantRegistrationRules.NormalizeCommercialRegistrationNumber(
            request.CommercialRegistrationNumber);
        if (commercialRegistrationNumber is not null
            && await _db.RestaurantRegistrations.AnyAsync(
                r => r.Id != registration.Id
                     && r.CommercialRegistrationNumber != null
                     && r.CommercialRegistrationNumber.Trim().ToUpper() == commercialRegistrationNumber
                     && r.Status != VerificationStatus.Rejected
                     && r.Status != VerificationStatus.Expired,
                cancellationToken))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var tenantResultForUpdate = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResultForUpdate.IsFailure) return Result.Failure<RestaurantRegistrationDto>(tenantResultForUpdate.Error);
        var normalizedServiceRegionIds = new List<Guid>();
        if (request.CountryId.HasValue
            || request.RegionId.HasValue
            || request.ServiceRegionIds?.Any(value => !string.IsNullOrWhiteSpace(value)) == true)
        {
            var selection = await RegionCatalogRules.ValidateRestaurantSelectionAsync(
                _db,
                request.CountryId ?? tenantResultForUpdate.Value.CountryId,
                request.RegionId,
                request.ServiceRegionIds ?? [],
                false,
                tenantResultForUpdate.Value.CountryId,
                cancellationToken);
            if (selection.Error is not null) return Result.Failure<RestaurantRegistrationDto>(selection.Error);
            normalizedServiceRegionIds = selection.Ids;
        }

        if (string.IsNullOrWhiteSpace(registration.RestaurantCode))
        {
            registration.RestaurantCode = RestaurantRegistrationRules.CreateRestaurantCode(registration.Id);
        }

        var before = RestaurantRegistrationMapping.MapAuditSnapshot(registration);

        // Track which field values actually changed (for non-draft updates, reset reviews)
        var changedFields = new List<string>();

        string? ChangedIfDifferentNullable(string? newValue, string? current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        Guid? ChangedIfDifferentGuid(Guid? newValue, Guid? current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        DateOnly? ChangedIfDifferentDate(DateOnly? newValue, DateOnly? current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        decimal? ChangedIfDifferentDecimal(decimal? newValue, decimal? current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        registration.LegalNameAr = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.LegalNameAr), registration.LegalNameAr, RegistrationFieldKeys.LegalNameAr);
        registration.LegalNameEn = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.LegalNameEn), registration.LegalNameEn, RegistrationFieldKeys.LegalNameEn);
        registration.TradeNameAr = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.TradeNameAr), registration.TradeNameAr, RegistrationFieldKeys.TradeNameAr);
        registration.TradeNameEn = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.TradeNameEn), registration.TradeNameEn, RegistrationFieldKeys.TradeNameEn);
        registration.LegalEntityType = ChangedIfDifferentNullable(
            legalEntityType, registration.LegalEntityType, RegistrationFieldKeys.LegalEntityType);
        registration.CompanyCivilId = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.CompanyCivilId), registration.CompanyCivilId, RegistrationFieldKeys.CompanyCivilId);
        registration.CommercialRegistrationNumber = ChangedIfDifferentNullable(
            commercialRegistrationNumber, registration.CommercialRegistrationNumber, RegistrationFieldKeys.CommercialRegistrationNumber);
        registration.CommercialRegistrationExpiry = ChangedIfDifferentDate(
            request.CommercialRegistrationExpiry, registration.CommercialRegistrationExpiry, RegistrationFieldKeys.CommercialRegistrationExpiry);
        registration.CommercialLicenseNumber = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.CommercialLicenseNumber), registration.CommercialLicenseNumber, RegistrationFieldKeys.CommercialLicenseNumber);
        registration.CommercialLicenseExpiry = ChangedIfDifferentDate(
            request.CommercialLicenseExpiry, registration.CommercialLicenseExpiry, RegistrationFieldKeys.CommercialLicenseExpiry);
        registration.FoodLicenseNumber = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.FoodLicenseNumber), registration.FoodLicenseNumber, RegistrationFieldKeys.FoodLicenseNumber);
        registration.FoodLicenseExpiry = ChangedIfDifferentDate(
            request.FoodLicenseExpiry, registration.FoodLicenseExpiry, RegistrationFieldKeys.FoodLicenseExpiry);
        registration.ContactPhone = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.ContactPhone), registration.ContactPhone, RegistrationFieldKeys.ContactPhone);
        registration.ContactEmail = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.ContactEmail)?.ToLowerInvariant(), registration.ContactEmail, RegistrationFieldKeys.ContactEmail);
        registration.Website = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.Website), registration.Website, RegistrationFieldKeys.Website);
        registration.InstagramHandle = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.InstagramHandle), registration.InstagramHandle, RegistrationFieldKeys.InstagramHandle);
        registration.PrimaryContactName = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactName), registration.PrimaryContactName, RegistrationFieldKeys.PrimaryContactName);
        registration.PrimaryContactTitle = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactTitle), registration.PrimaryContactTitle, RegistrationFieldKeys.PrimaryContactTitle);
        registration.PrimaryContactPhone = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactPhone), registration.PrimaryContactPhone, RegistrationFieldKeys.PrimaryContactPhone);
        registration.PrimaryContactEmail = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.PrimaryContactEmail)?.ToLowerInvariant(), registration.PrimaryContactEmail, RegistrationFieldKeys.PrimaryContactEmail);
        registration.OwnerName = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.OwnerName), registration.OwnerName, RegistrationFieldKeys.OwnerName);
        registration.AuthorizedSignatoryCivilId = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.AuthorizedSignatoryCivilId), registration.AuthorizedSignatoryCivilId, RegistrationFieldKeys.AuthorizedSignatoryCivilId);
        registration.AuthorizedSignatoryTitle = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.AuthorizedSignatoryTitle), registration.AuthorizedSignatoryTitle, RegistrationFieldKeys.AuthorizedSignatoryTitle);
        registration.BankName = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.BankName), registration.BankName, RegistrationFieldKeys.BankName);
        registration.BankBeneficiaryName = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.BankBeneficiaryName), registration.BankBeneficiaryName, RegistrationFieldKeys.BeneficiaryName);
        registration.Iban = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeIban(request.Iban), registration.Iban, RegistrationFieldKeys.Iban);
        registration.BankAccountNumber = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.BankAccountNumber), registration.BankAccountNumber, RegistrationFieldKeys.AccountNumber);
        registration.SwiftCode = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.SwiftCode)?.ToUpperInvariant(), registration.SwiftCode, RegistrationFieldKeys.SwiftCode);
        registration.SettlementCurrency = RestaurantRegistrationRules.SettlementCurrency;
        registration.Address = ChangedIfDifferentNullable(
            RestaurantRegistrationRules.NormalizeOptional(request.Address), registration.Address, RegistrationFieldKeys.HeadquartersAddressAr);
        registration.Latitude = ChangedIfDifferentDecimal(
            request.Latitude, registration.Latitude, RegistrationFieldKeys.Latitude);
        registration.Longitude = ChangedIfDifferentDecimal(
            request.Longitude, registration.Longitude, RegistrationFieldKeys.Longitude);
        registration.CountryId = ChangedIfDifferentGuid(
            tenantResultForUpdate.Value.CountryId, registration.CountryId, RegistrationFieldKeys.CountryId);
        registration.RegionId = ChangedIfDifferentGuid(
            request.RegionId, registration.RegionId, RegistrationFieldKeys.RegionId);
        registration.DeliveryByRestaurantConfirmed = request.DeliveryByRestaurantConfirmed;
        registration.DataResponsibilityConfirmed = request.DataResponsibilityConfirmed;
        registration.RestaurantNotes = RestaurantRegistrationRules.NormalizeOptional(request.RestaurantNotes);
        registration.ServiceRegionIdsJson = normalizedServiceRegionIds.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(normalizedServiceRegionIds.Select(id => id.ToString()).ToList())
            : null;
        if (request.SelectedProgramIds is not null)
            registration.SelectedProgramIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SelectedProgramIds.Distinct().ToArray());
        if (request.SelectedBundleIds is not null)
            registration.SelectedBundleIdsJson = System.Text.Json.JsonSerializer.Serialize(request.SelectedBundleIds.Distinct().ToArray());

        // During corrections, the restaurant may only change fields the admin marked as needing attention.
        if (registration.Status is not VerificationStatus.Draft)
        {
            var wasUnderReview = registration.Status == VerificationStatus.UnderReview;
            var reviewByField = registration.FieldReviews.ToDictionary(f => f.FieldKey);
            var blockedField = changedFields.FirstOrDefault(fieldKey =>
                !reviewByField.TryGetValue(fieldKey, out var review)
                || review.Status is not (VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired));

            if (blockedField is not null)
            {
                return Error.Validation(
                    MessageCodes.Auth.InvalidState,
                    $"Field cannot be changed unless it was rejected or requested for changes: {blockedField}");
            }

            foreach (var fieldKey in changedFields)
            {
                if (reviewByField.TryGetValue(fieldKey, out var review))
                {
                    review.Status = VerificationStatus.Submitted;
                    review.Reason = null;
                    review.ReviewedByUserId = null;
                    review.ReviewedAtUtc = null;
                }
            }

            // A correction saved while the request is under review must move the
            // registration into a state that the submit command accepts. Without
            // this transition, resetting the rejected field review to Submitted
            // removes the only outstanding-correction marker and traps the user.
            if (wasUnderReview && changedFields.Count > 0)
            {
                registration.Status = VerificationStatus.ResubmissionRequired;
            }
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationUpdated",
            "RestaurantRegistration",
            registration.Id,
            Before: before,
            After: RestaurantRegistrationMapping.MapAuditSnapshot(registration),
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration, registration.FieldReviews.ToList());
    }
}

public sealed record SubmitRestaurantRegistrationCommand(Guid Id) : ICommand<RestaurantRegistrationDto>;

public sealed class SubmitRestaurantRegistrationCommandHandler
    : ICommandHandler<SubmitRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialRegistration,
        DocumentType.CommercialLicense,
        DocumentType.FoodLicense,
        DocumentType.AuthorizedSignatoryId,
        DocumentType.AuthorizedSignatoryAuthorization,
        DocumentType.BankIbanProof,
        DocumentType.ProfileImage,
        DocumentType.BranchLicense,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationPushNotificationService _pushNotifier;

    public SubmitRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _pushNotifier = pushNotifier ?? NullRegistrationPushNotificationService.Instance;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        SubmitRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not Guid ownerUserId
            || registration.SubmittedByUserId != ownerUserId)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var hasOutstandingCorrection = registration.FieldReviews.Any(f =>
                f.Status is VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired)
            || docs.Any(d => d.Verifications
                .OrderByDescending(v => v.CreatedAtUtc)
                .FirstOrDefault()?.Status is VerificationStatus.Rejected or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired);

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired)
            && !(registration.Status == VerificationStatus.UnderReview && hasOutstandingCorrection))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Invalid status for submit.");
        }

        foreach (var required in RequiredDocs)
        {
            // Use the newest uploaded document per required DocumentType so replacing a rejected file works.
            var doc = docs
                .Where(d => d.DocumentType == required)
                .OrderByDescending(d => d.CreatedAtUtc)
                .FirstOrDefault();
            if (doc is null)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing document: {required}");
            }

            var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            if (latest?.Status == VerificationStatus.Rejected)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Rejected document must be re-uploaded: {required}");
            }
        }

        // Validate required template fields before submission.
        var missingField = RegistrationFieldKeys.RequiredForSubmission
            .FirstOrDefault(key => key switch
            {
                RegistrationFieldKeys.LegalNameAr => string.IsNullOrWhiteSpace(registration.LegalNameAr),
                RegistrationFieldKeys.LegalNameEn => string.IsNullOrWhiteSpace(registration.LegalNameEn),
                RegistrationFieldKeys.TradeNameAr => string.IsNullOrWhiteSpace(registration.TradeNameAr),
                RegistrationFieldKeys.TradeNameEn => string.IsNullOrWhiteSpace(registration.TradeNameEn),
                RegistrationFieldKeys.LegalEntityType => string.IsNullOrWhiteSpace(registration.LegalEntityType),
                RegistrationFieldKeys.CompanyCivilId => string.IsNullOrWhiteSpace(registration.CompanyCivilId),
                RegistrationFieldKeys.CommercialRegistrationNumber => string.IsNullOrWhiteSpace(registration.CommercialRegistrationNumber),
                RegistrationFieldKeys.CommercialRegistrationExpiry => registration.CommercialRegistrationExpiry is null,
                RegistrationFieldKeys.CommercialLicenseNumber => string.IsNullOrWhiteSpace(registration.CommercialLicenseNumber),
                RegistrationFieldKeys.CommercialLicenseExpiry => registration.CommercialLicenseExpiry is null,
                RegistrationFieldKeys.FoodLicenseNumber => string.IsNullOrWhiteSpace(registration.FoodLicenseNumber),
                RegistrationFieldKeys.FoodLicenseExpiry => registration.FoodLicenseExpiry is null,
                RegistrationFieldKeys.HeadquartersAddressAr => string.IsNullOrWhiteSpace(registration.Address),
                RegistrationFieldKeys.ContactPhone => string.IsNullOrWhiteSpace(registration.ContactPhone),
                RegistrationFieldKeys.ContactEmail => string.IsNullOrWhiteSpace(registration.ContactEmail),
                RegistrationFieldKeys.PrimaryContactName => string.IsNullOrWhiteSpace(registration.PrimaryContactName),
                RegistrationFieldKeys.PrimaryContactTitle => string.IsNullOrWhiteSpace(registration.PrimaryContactTitle),
                RegistrationFieldKeys.PrimaryContactPhone => string.IsNullOrWhiteSpace(registration.PrimaryContactPhone),
                RegistrationFieldKeys.PrimaryContactEmail => string.IsNullOrWhiteSpace(registration.PrimaryContactEmail),
                RegistrationFieldKeys.OwnerName => string.IsNullOrWhiteSpace(registration.OwnerName),
                RegistrationFieldKeys.AuthorizedSignatoryCivilId => string.IsNullOrWhiteSpace(registration.AuthorizedSignatoryCivilId),
                RegistrationFieldKeys.AuthorizedSignatoryTitle => string.IsNullOrWhiteSpace(registration.AuthorizedSignatoryTitle),
                RegistrationFieldKeys.BankName => string.IsNullOrWhiteSpace(registration.BankName),
                RegistrationFieldKeys.BeneficiaryName => string.IsNullOrWhiteSpace(registration.BankBeneficiaryName),
                RegistrationFieldKeys.Iban => string.IsNullOrWhiteSpace(registration.Iban),
                RegistrationFieldKeys.AccountNumber => string.IsNullOrWhiteSpace(registration.BankAccountNumber),
                RegistrationFieldKeys.Latitude => registration.Latitude is null
                    || !RestaurantRegistrationRules.IsWithinKuwaitBounds(registration.Latitude, registration.Longitude),
                RegistrationFieldKeys.Longitude => registration.Longitude is null
                    || !RestaurantRegistrationRules.IsWithinKuwaitBounds(registration.Latitude, registration.Longitude),
                _ => false,
            });

        if (missingField is not null)
        {
            return Error.Validation(
                MessageCodes.Validation.RequiredField,
                $"Required field is missing: {missingField}");
        }

        if (registration.DeliveryByRestaurantConfirmed is not true)
        {
            return Error.Validation(
                MessageCodes.Validation.RequiredField,
                "Delivery responsibility confirmation must be accepted.");
        }

        if (registration.DataResponsibilityConfirmed is not true)
        {
            return Error.Validation(
                MessageCodes.Validation.RequiredField,
                "Data responsibility confirmation must be accepted.");
        }

        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(registration.LegalEntityType, out var legalEntityType)
            || legalEntityType is null)
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");
        }

        if (!RestaurantRegistrationRules.IsValidEmail(registration.ContactEmail)
            || !RestaurantRegistrationRules.IsValidEmail(registration.PrimaryContactEmail))
        {
            return Error.Validation(MessageCodes.Validation.EmailInvalid, "A registration email address is invalid.");
        }

        if (!RestaurantRegistrationRules.IsValidHttpsWebsite(registration.Website))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Website must be an absolute HTTPS URL.");
        }

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var expiredField = new[]
            {
                (RegistrationFieldKeys.CommercialRegistrationExpiry, registration.CommercialRegistrationExpiry),
                (RegistrationFieldKeys.CommercialLicenseExpiry, registration.CommercialLicenseExpiry),
                (RegistrationFieldKeys.FoodLicenseExpiry, registration.FoodLicenseExpiry),
            }
            .FirstOrDefault(item => item.Item2 < today);
        if (expiredField != default)
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                $"License date is expired: {expiredField.Item1}");
        }

        var normalizedCommercialRegistration = RestaurantRegistrationRules.NormalizeCommercialRegistrationNumber(
            registration.CommercialRegistrationNumber)!;
        if (await _db.RestaurantRegistrations.AnyAsync(
                r => r.Id != registration.Id
                     && r.CommercialRegistrationNumber != null
                     && r.CommercialRegistrationNumber.Trim().ToUpper() == normalizedCommercialRegistration
                     && r.Status != VerificationStatus.Rejected
                     && r.Status != VerificationStatus.Expired,
                cancellationToken))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var normalizedIban = RestaurantRegistrationRules.NormalizeIban(registration.Iban);
        if (!RestaurantRegistrationRules.IsValidKuwaitIban(normalizedIban))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "IBAN must be a valid Kuwait IBAN with 30 total characters and a valid checksum.");
        }

        registration.Iban = normalizedIban;
        registration.CommercialRegistrationNumber = normalizedCommercialRegistration;
        registration.LegalEntityType = legalEntityType;
        registration.ContactEmail = registration.ContactEmail!.Trim().ToLowerInvariant();
        registration.PrimaryContactEmail = registration.PrimaryContactEmail!.Trim().ToLowerInvariant();
        registration.Website = RestaurantRegistrationRules.NormalizeOptional(registration.Website);
        registration.SettlementCurrency = RestaurantRegistrationRules.SettlementCurrency;
        registration.SubmissionDate = today;

        // Seed missing field reviews as Submitted, preserve existing Approved rows.
        // Add via the DbSet directly so newly constructed reviews are INSERTed;
        // adding through the tracked parent navigation can mark them Modified and
        // cause DbUpdateConcurrencyException on SaveChanges.
        var existingKeys = registration.FieldReviews
            .Where(f => f.Status == VerificationStatus.Approved)
            .Select(f => f.FieldKey)
            .ToHashSet();

        var seededFieldReviews = new List<RestaurantRegistrationFieldReview>();
        foreach (var fieldKey in RegistrationFieldKeys.All)
        {
            if (!existingKeys.Contains(fieldKey)
                && registration.FieldReviews.All(f => f.FieldKey != fieldKey))
            {
                seededFieldReviews.Add(new RestaurantRegistrationFieldReview
                {
                    RegistrationId = registration.Id,
                    FieldKey = fieldKey,
                    Status = VerificationStatus.Submitted,
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
        }
        _db.RestaurantRegistrationFieldReviews.AddRange(seededFieldReviews);

        registration.Status = VerificationStatus.Submitted;
        registration.SubmissionDate ??= DateOnly.FromDateTime(_clock.UtcNow);
        registration.ModifiedAtUtc = _clock.UtcNow;
        registration.RejectionReason = null;
        registration.ChangeRequestNotes = null;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationSubmitted",
            "RestaurantRegistration",
            registration.Id,
            SourceChannel: "restaurant"), cancellationToken);

        var restaurantName = registration.TradeNameAr
            ?? registration.TradeNameEn
            ?? registration.LegalNameAr
            ?? "مطعم جديد";

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "RestaurantRegistrationSubmitted",
            AggregateType = "RestaurantRegistration",
            AggregateId = registration.Id.ToString(),
            DeduplicationKey = $"RestaurantRegistrationSubmitted_{registration.Id}_{registration.ModifiedAtUtc:yyyyMMddHHmmss}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                RegistrationId = registration.Id,
                RestaurantName = restaurantName,
                SubmittedByUserId = registration.SubmittedByUserId
            }),
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var mappedFieldReviews = registration.FieldReviews.Concat(seededFieldReviews).ToList();
        return RestaurantRegistrationMapping.Map(registration, mappedFieldReviews);
    }
}

public sealed record ResendRestaurantRegistrationNotificationCommand(Guid RegistrationId) : ICommand;

public sealed class ResendRestaurantRegistrationNotificationCommandHandler
    : ICommandHandler<ResendRestaurantRegistrationNotificationCommand>
{
    private readonly IAppDbContext _db;
    private readonly IRegistrationPushNotificationService _pushNotifier;

    public ResendRestaurantRegistrationNotificationCommandHandler(
        IAppDbContext db,
        IRegistrationPushNotificationService pushNotifier)
    {
        _db = db;
        _pushNotifier = pushNotifier;
    }

    public async Task<Result> Handle(
        ResendRestaurantRegistrationNotificationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId && !r.IsDeleted, cancellationToken);

        if (registration is null)
            return Error.NotFound(MessageCodes.Common.NotFound, "Registration not found.");

        var restaurantName = registration.TradeNameAr
            ?? registration.TradeNameEn
            ?? registration.LegalNameAr
            ?? "مطعم جديد";

        await _pushNotifier.NotifyAdminsNewRegistrationAsync(
            registration.Id,
            restaurantName,
            cancellationToken);

        return Result.Success();
    }
}

public sealed record AdminRestaurantDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<RestaurantRegistrationDto>;

public sealed class AdminRestaurantDecisionCommandHandler
    : ICommandHandler<AdminRestaurantDecisionCommand, RestaurantRegistrationDto>
{
    private const int MaxReasonLength = 2000;

    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialRegistration,
        DocumentType.CommercialLicense,
        DocumentType.FoodLicense,
        DocumentType.AuthorizedSignatoryId,
        DocumentType.AuthorizedSignatoryAuthorization,
        DocumentType.BankIbanProof,
        DocumentType.ProfileImage,
        DocumentType.BranchLicense,
    ];

    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly ITransactionalEmailSender? _emailSender;
    private readonly IRegistrationPushNotificationService _pushNotifier;

    public AdminRestaurantDecisionCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationReviewNotifier? notifier = null,
        ITransactionalEmailSender? emailSender = null,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
        _emailSender = emailSender;
        _pushNotifier = pushNotifier ?? NullRegistrationPushNotificationService.Instance;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        AdminRestaurantDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        var action = request.Action.ToLowerInvariant();

        // Approve does NOT require a reason; reject and request-changes do.
        if (action is not "approve" && string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, $"Reason must not exceed {MaxReasonLength} characters.");
        }
        switch (action)
        {
            case "approve":
                {
                    if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                    {
                        return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot approve in current status.");
                    }

                    if (!registration.CountryId.HasValue)
                    {
                        return Error.Validation("catalog.restaurant_country_required", "Restaurant country is required before approval.");
                    }

                    // Check that all canonical required fields (excluding null optionals) are Approved
                    var requiredFields = RegistrationFieldKeys.RequiredForApproval(registration);
                    var fieldReviewByKey = registration.FieldReviews.ToDictionary(f => f.FieldKey);
                    foreach (var fieldKey in requiredFields)
                    {
                        if (!fieldReviewByKey.TryGetValue(fieldKey, out var review)
                            || review.Status != VerificationStatus.Approved)
                        {
                            return Error.Validation(
                                MessageCodes.Auth.DocumentsIncomplete,
                                $"Required field review not approved: {fieldKey}");
                        }
                    }

                    // Check documents
                    var docs = await _db.Documents
                        .Include(d => d.Verifications)
                        .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                        .ToListAsync(cancellationToken);

                    // All required document types must be present and approved (use newest per type)
                    foreach (var required in RequiredDocs)
                    {
                        var doc = docs
                            .Where(d => d.DocumentType == required)
                            .OrderByDescending(d => d.CreatedAtUtc)
                            .FirstOrDefault();
                        if (doc is null)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing required document: {required}");
                        }

                        var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                        if (latest is null || latest.Status != VerificationStatus.Approved)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Required document not approved: {required}");
                        }
                    }

                    var selectedProgramIds = JsonSerializer.Deserialize<List<Guid>>(registration.SelectedProgramIdsJson ?? "[]") ?? [];
                    var selectedBundleIds = JsonSerializer.Deserialize<List<Guid>>(registration.SelectedBundleIdsJson ?? "[]") ?? [];
                    var selectedBundles = await _db.Bundles.AsNoTracking()
                        .Where(x => selectedBundleIds.Contains(x.Id) && selectedProgramIds.Contains(x.ProgramId) && x.IsActive)
                        .Select(x => new { x.Id, x.ProgramId })
                        .ToListAsync(cancellationToken);
                    if (selectedBundles.Count != selectedBundleIds.Distinct().Count())
                        return Error.Validation("restaurant_offerings.invalid_bundle", "Selected bundles must belong to selected active programs.");

                    var profile = new RestaurantProfile
                    {
                        UserId = registration.SubmittedByUserId ?? Guid.Empty,
                        LegalNameAr = registration.LegalNameAr,
                        LegalNameEn = registration.LegalNameEn,
                        TradeNameAr = registration.TradeNameAr,
                        TradeNameEn = registration.TradeNameEn,
                        CommercialLicenseNumber = registration.CommercialLicenseNumber,
                        CountryId = registration.CountryId,
                        PrimaryRegionId = registration.RegionId,
                        Latitude = registration.Latitude,
                        Longitude = registration.Longitude,
                        ContactPhone = registration.ContactPhone,
                        ContactEmail = registration.ContactEmail,
                        VerificationStatus = VerificationStatus.Approved,
                        IsOperationallyActive = false,
                        CreatedAtUtc = _clock.UtcNow,
                    };
                    _db.RestaurantProfiles.Add(profile);
                    foreach (var selectedBundle in selectedBundles)
                    {
                        _db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
                        {
                            RestaurantId = profile.Id,
                            ProgramId = selectedBundle.ProgramId,
                            BundleId = selectedBundle.Id,
                            IsActive = false,
                        });
                    }

                    var regionIdsToActivate = new HashSet<Guid>();
                    if (registration.RegionId.HasValue) regionIdsToActivate.Add(registration.RegionId.Value);
                    if (!string.IsNullOrWhiteSpace(registration.ServiceRegionIdsJson))
                    {
                        try
                        {
                            var rawServiceRegions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(registration.ServiceRegionIdsJson) ?? [];
                            var allRegions = await _db.Regions.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
                            foreach (var item in rawServiceRegions)
                            {
                                if (string.IsNullOrWhiteSpace(item)) continue;
                                var trimmed = item.Trim();
                                var match = allRegions.FirstOrDefault(r =>
                                    r.Id.ToString().Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                    r.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                    r.NameEn.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                    r.NameAr.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                    r.Code.EndsWith($"-{trimmed}", StringComparison.OrdinalIgnoreCase) ||
                                    r.Code.Replace("KW-", "").Equals(trimmed, StringComparison.OrdinalIgnoreCase));
                                if (match != null) regionIdsToActivate.Add(match.Id);
                            }
                        }
                        catch { }
                    }

                    foreach (var regId in regionIdsToActivate)
                    {
                        _db.RestaurantServiceRegions.Add(new Domain.RestaurantOperations.RestaurantServiceRegion
                        {
                            RestaurantId = profile.Id,
                            RegionId = regId,
                            IsActive = true,
                            DeliveryWindowStart = new TimeOnly(8, 0),
                            DeliveryWindowEnd = new TimeOnly(14, 0),
                            CreatedAtUtc = _clock.UtcNow
                        });
                    }

                    registration.Status = VerificationStatus.Approved;
                    registration.ResultingRestaurantId = profile.Id;
                    registration.ReviewedByUserId = _currentUser.UserId;
                    registration.ReviewedAtUtc = _clock.UtcNow;
                    registration.RejectionReason = null;

                    if (registration.SubmittedByUserId is { } userId)
                    {
                        await _users.SetRestaurantIdAsync(userId, profile.Id, cancellationToken);
                        var roles = await _users.GetRolesAsync(userId, cancellationToken);
                        if (!roles.Contains(AppRoles.RestaurantOwner, StringComparer.OrdinalIgnoreCase))
                        {
                            var roleAssignment = await _users.AddToRoleAsync(userId, AppRoles.RestaurantOwner, cancellationToken);
                            if (roleAssignment.IsFailure)
                            {
                                return roleAssignment.Error;
                            }
                        }
                    }

                    // Create Delivery Manager account and email credentials if ContactEmail is present
                    var deliveryManagerEmail = registration.ContactEmail?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(deliveryManagerEmail))
                    {
                        var existingUser = await _users.FindByEmailAsync(deliveryManagerEmail, cancellationToken);
                        if (existingUser is null)
                        {
                            var tempPassword = MealMate.Application.Common.Helpers.TemporaryPasswordGenerator.Generate();
                            var contactName = !string.IsNullOrWhiteSpace(registration.OwnerName)
                                ? registration.OwnerName
                                : registration.TradeNameAr;
                            var contactPhone = !string.IsNullOrWhiteSpace(registration.ContactPhone)
                                ? registration.ContactPhone
                                : "+96500000000";

                            var createDm = await _users.CreateAsync(new CreateUserRequest(
                                deliveryManagerEmail,
                                contactPhone,
                                tempPassword,
                                contactName,
                                UserType.DeliveryManager,
                                AccountStatus.Registered,
                                profile.Id), cancellationToken);

                            if (createDm.IsSuccess)
                            {
                                var dmUser = createDm.Value;
                                await _users.AddToRoleAsync(dmUser.Id, AppRoles.DeliveryManager, cancellationToken);

                                var rendered = MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.Render(
                                    MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.DeliveryManagerInvite,
                                    "ar",
                                    new Dictionary<string, string>
                                    {
                                        ["recipient_name"] = dmUser.FullName ?? dmUser.Email,
                                        ["email"] = dmUser.Email,
                                        ["password"] = tempPassword,
                                        ["support_email"] = "support@mealmate.app"
                                    });

                                if (rendered.IsSuccess && _emailSender != null)
                                {
                                    await _emailSender.SendAsync(new TransactionalEmailRequest(
                                        dmUser.Email,
                                        rendered.Value.Subject,
                                        rendered.Value.Html,
                                        rendered.Value.Text,
                                        TemplateId: MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.DeliveryManagerInvite), cancellationToken);
                                }
                            }
                        }
                        else if (existingUser.UserType == UserType.DeliveryManager && existingUser.RestaurantId == null)
                        {
                            await _users.SetRestaurantIdAsync(existingUser.Id, profile.Id, cancellationToken);
                            await _users.UpdateAccountStatusAsync(existingUser.Id, AccountStatus.Registered, cancellationToken);
                        }
                    }

                    await _audit.LogAsync(new AuditEntry(
                        "RestaurantRegistrationApproved",
                        "RestaurantRegistration",
                        registration.Id,
                        After: RestaurantRegistrationMapping.MapAuditSnapshot(registration),
                        Reason: string.IsNullOrEmpty(reason) ? null : reason,
                        SourceChannel: "admin"), cancellationToken);

                    _db.OutboxMessages.Add(new OutboxMessage
                    {
                        EventType = "RestaurantRegistrationApproved",
                        AggregateType = "RestaurantRegistration",
                        AggregateId = registration.Id.ToString(),
                        DeduplicationKey = $"RestaurantRegistrationApproved_{registration.Id}",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            RegistrationId = registration.Id,
                            RestaurantId = registration.ResultingRestaurantId,
                            OwnerUserId = registration.SubmittedByUserId,
                            RestaurantName = registration.TradeNameAr ?? registration.TradeNameEn ?? registration.LegalNameAr ?? "مطعم"
                        }),
                        CreatedAtUtc = _clock.UtcNow
                    });
                    break;
                }
                case "reject":
                    if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                    {
                        return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be rejected in its current status.");
                    }

                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationRejected",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "RestaurantRegistrationRejected",
                    AggregateType = "RestaurantRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"RestaurantRegistrationRejected_{registration.Id}_{registration.ReviewedAtUtc:yyyyMMddHHmmss}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        OwnerUserId = registration.SubmittedByUserId,
                        Reason = reason
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;
            case "request-changes":
                if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot request changes in current status.");
                }

                registration.Status = VerificationStatus.NeedsChanges;
                registration.ChangeRequestNotes = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationChangesRequested",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "RestaurantRegistrationChangesRequested",
                    AggregateType = "RestaurantRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"RestaurantRegistrationChangesRequested_{registration.Id}_{registration.ReviewedAtUtc:yyyyMMddHHmmss}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        OwnerUserId = registration.SubmittedByUserId,
                        Reason = reason
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;
            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        await _db.SaveChangesAsync(cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Per-field field review (approve / reject individual field)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AdminFieldReviewCommand(
    Guid RegistrationId,
    string FieldKey,
    string Action, // "approve" | "reject"
    string? Reason) : ICommand<RestaurantRegistrationDto>;

public sealed class AdminFieldReviewCommandHandler
    : ICommandHandler<AdminFieldReviewCommand, RestaurantRegistrationDto>
{
    private static readonly HashSet<string> ValidActions = ["approve", "reject"];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly IRegistrationPushNotificationService _pushNotifier;

    public AdminFieldReviewCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationReviewNotifier? notifier = null,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullRegistrationPushNotificationService.Instance;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        AdminFieldReviewCommand request,
        CancellationToken cancellationToken)
    {
        // --- validate field key ---
        if (!RegistrationFieldKeys.IsValid(request.FieldKey))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, $"Unknown field key: {request.FieldKey}");
        }

        var action = request.Action.ToLowerInvariant();
        if (!ValidActions.Contains(action))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Action must be 'approve' or 'reject'.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;

        // reject requires nonblank reason
        if (action == "reject" && string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required when rejecting a field.");
        }

        // --- load registration with existing field reviews ---
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        // only Submittable / UnderReview registrations can be reviewed
        if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration is not in a reviewable state.");
        }

        // --- upsert the field review ---
        var review = registration.FieldReviews
            .FirstOrDefault(f => f.FieldKey == request.FieldKey);

        if (review is null)
        {
            review = new RestaurantRegistrationFieldReview
            {
                RegistrationId = registration.Id,
                FieldKey = request.FieldKey,
                CreatedAtUtc = _clock.UtcNow,
            };
            registration.FieldReviews.Add(review);
            _db.RestaurantRegistrationFieldReviews.Add(review);
        }

        review.Status = action == "approve" ? VerificationStatus.Approved : VerificationStatus.Rejected;
        review.Reason = action == "reject" ? reason : null;
        review.ReviewedByUserId = _currentUser.UserId;
        review.ReviewedAtUtc = _clock.UtcNow;

        if (request.FieldKey == RegistrationFieldKeys.HeadquartersAddressAr)
        {
            foreach (var coordKey in new[] { RegistrationFieldKeys.Latitude, RegistrationFieldKeys.Longitude })
            {
                var coordReview = registration.FieldReviews.FirstOrDefault(f => f.FieldKey == coordKey);
                if (coordReview is null)
                {
                    coordReview = new RestaurantRegistrationFieldReview
                    {
                        RegistrationId = registration.Id,
                        FieldKey = coordKey,
                        CreatedAtUtc = _clock.UtcNow,
                    };
                    registration.FieldReviews.Add(coordReview);
                    _db.RestaurantRegistrationFieldReviews.Add(coordReview);
                }

                coordReview.Status = review.Status;
                coordReview.Reason = review.Reason;
                coordReview.ReviewedByUserId = _currentUser.UserId;
                coordReview.ReviewedAtUtc = _clock.UtcNow;
            }
        }

        // Keep the review session open even when an individual field is rejected.
        // The final admin decision sends all accumulated field/document feedback
        // to the restaurant in one coherent change request.
        if (registration.Status == VerificationStatus.Submitted)
        {
            registration.Status = VerificationStatus.UnderReview;
        }

        registration.ReviewedByUserId = _currentUser.UserId;
        registration.ReviewedAtUtc = _clock.UtcNow;

        var changeType = action == "approve" ? "field-approved" : "field-rejected";
        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationFieldReview",
            "RestaurantRegistration",
            registration.Id,
            After: new { FieldKey = request.FieldKey, Status = review.Status, Reason = review.Reason },
            Reason: string.IsNullOrEmpty(reason) ? null : reason,
            SourceChannel: "admin"), cancellationToken);

        if (registration.SubmittedByUserId is { } notifyUserId)
        {
            await _notifier.NotifyRegistrationUpdated(
                notifyUserId, registration.Id, changeType, cancellationToken);

            if (action == "reject")
            {
                await _pushNotifier.NotifyRestaurantFieldRejectedAsync(
                    notifyUserId, request.FieldKey, reason, cancellationToken);
            }
        }

        return RestaurantRegistrationMapping.Map(registration, registration.FieldReviews.ToList());
    }
}

public sealed record RestaurantRegistrationSummaryDto(
    Guid Id,
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? RestaurantCode,
    VerificationStatus Status,
    DateOnly? SubmissionDate,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ResultingRestaurantId);

public sealed record ListRestaurantRegistrationsQuery(int Page = 1, int PageSize = 20, VerificationStatus? Status = null)
    : IQuery<PagedResult<RestaurantRegistrationSummaryDto>>;

public sealed class ListRestaurantRegistrationsQueryHandler
    : IQueryHandler<ListRestaurantRegistrationsQuery, PagedResult<RestaurantRegistrationSummaryDto>>
{
    private readonly IAppDbContext _db;

    public ListRestaurantRegistrationsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<RestaurantRegistrationSummaryDto>>> Handle(
        ListRestaurantRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.RestaurantRegistrations.AsNoTracking().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(x => new RestaurantRegistrationSummaryDto(
                x.Id,
                x.LegalNameAr,
                x.LegalNameEn,
                x.TradeNameAr,
                x.TradeNameEn,
                x.RestaurantCode,
                x.Status,
                x.SubmissionDate,
                x.CreatedAtUtc,
                x.ModifiedAtUtc,
                x.ReviewedAtUtc,
                x.ResultingRestaurantId))
            .ToListAsync(cancellationToken);

        items = items
            .Select(item => string.IsNullOrWhiteSpace(item.RestaurantCode)
                ? item with { RestaurantCode = RestaurantRegistrationRules.CreateRestaurantCode(item.Id) }
                : item)
            .ToList();

        return new PagedResult<RestaurantRegistrationSummaryDto>(items, total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetRestaurantRegistrationQuery(Guid Id) : IQuery<RestaurantRegistrationDto>;

public sealed class GetRestaurantRegistrationQueryHandler : IQueryHandler<GetRestaurantRegistrationQuery, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(GetRestaurantRegistrationQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.RestaurantRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (!RegistrationAccess.CanView(
                _currentUser,
                item.SubmittedByUserId,
                PermissionCodes.RestaurantRegView,
                PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var isOwner = _currentUser.UserId is Guid userId && item.SubmittedByUserId == userId;
        return RestaurantRegistrationMapping.Map(item, maskSensitive: !isOwner);
    }
}

internal static class RestaurantRegistrationMapping
{
    public static RestaurantRegistrationDto Map(
        Domain.IdentityAccess.RestaurantRegistration r,
        IReadOnlyList<RestaurantRegistrationFieldReview>? fieldReviews = null,
        bool maskSensitive = false) => new(
        r.Id,
        r.LegalNameAr,
        r.LegalNameEn,
        r.TradeNameAr,
        r.TradeNameEn,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.ContactPhone) : r.ContactPhone,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.ContactEmail) : r.ContactEmail,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.CommercialRegistrationNumber) : r.CommercialRegistrationNumber,
        r.CommercialRegistrationExpiry,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.CommercialLicenseNumber) : r.CommercialLicenseNumber,
        r.CommercialLicenseExpiry,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.FoodLicenseNumber) : r.FoodLicenseNumber,
        r.FoodLicenseExpiry,
        RestaurantRegistrationRules.NormalizeLegalEntityTypeForOutput(r.LegalEntityType),
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.CompanyCivilId) : r.CompanyCivilId,
        r.Website,
        r.InstagramHandle,
        string.IsNullOrWhiteSpace(r.RestaurantCode)
            ? RestaurantRegistrationRules.CreateRestaurantCode(r.Id)
            : r.RestaurantCode,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.OwnerName) : r.OwnerName,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.AuthorizedSignatoryCivilId) : r.AuthorizedSignatoryCivilId,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.AuthorizedSignatoryTitle) : r.AuthorizedSignatoryTitle,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactName) : r.PrimaryContactName,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactTitle) : r.PrimaryContactTitle,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactPhone) : r.PrimaryContactPhone,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactEmail) : r.PrimaryContactEmail,
        r.BankName,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.BankBeneficiaryName) : r.BankBeneficiaryName,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.Iban) : r.Iban,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.BankAccountNumber) : r.BankAccountNumber,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.SwiftCode) : r.SwiftCode,
        RestaurantRegistrationRules.SettlementCurrency,
        maskSensitive ? RestaurantRegistrationRules.MaskSensitive(r.Address) : r.Address,
        r.CountryId,
        r.RegionId,
        r.Status,
        r.RejectionReason,
        r.ChangeRequestNotes,
        r.ResultingRestaurantId,
        r.SubmittedByUserId,
        r.CreatedAtUtc,
        r.ModifiedAtUtc,
        r.ReviewedAtUtc,
        r.ReviewedByUserId,
        r.DeliveryByRestaurantConfirmed,
        r.DataResponsibilityConfirmed,
        r.SubmissionDate,
        r.RestaurantNotes,
        fieldReviews?.Select(MapFieldReview).ToList(),
        ParseServiceRegionIds(r.ServiceRegionIdsJson),
        r.Latitude,
        r.Longitude,
        JsonSerializer.Deserialize<List<Guid>>(r.SelectedProgramIdsJson ?? "[]") ?? [],
        JsonSerializer.Deserialize<List<Guid>>(r.SelectedBundleIdsJson ?? "[]") ?? []);

    public static object MapAuditSnapshot(Domain.IdentityAccess.RestaurantRegistration r) => new
    {
        r.Id,
        r.Status,
        r.LegalNameAr,
        r.LegalNameEn,
        r.TradeNameAr,
        r.TradeNameEn,
        ContactPhone = RestaurantRegistrationRules.MaskSensitive(r.ContactPhone),
        ContactEmail = RestaurantRegistrationRules.MaskSensitive(r.ContactEmail),
        CommercialRegistrationNumber = RestaurantRegistrationRules.MaskSensitive(r.CommercialRegistrationNumber),
        CommercialLicenseNumber = RestaurantRegistrationRules.MaskSensitive(r.CommercialLicenseNumber),
        FoodLicenseNumber = RestaurantRegistrationRules.MaskSensitive(r.FoodLicenseNumber),
        CompanyCivilId = RestaurantRegistrationRules.MaskSensitive(r.CompanyCivilId),
        OwnerName = RestaurantRegistrationRules.MaskSensitive(r.OwnerName),
        AuthorizedSignatoryCivilId = RestaurantRegistrationRules.MaskSensitive(r.AuthorizedSignatoryCivilId),
        PrimaryContactName = RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactName),
        PrimaryContactPhone = RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactPhone),
        PrimaryContactEmail = RestaurantRegistrationRules.MaskSensitive(r.PrimaryContactEmail),
        BankBeneficiaryName = RestaurantRegistrationRules.MaskSensitive(r.BankBeneficiaryName),
        Iban = RestaurantRegistrationRules.MaskSensitive(r.Iban),
        BankAccountNumber = RestaurantRegistrationRules.MaskSensitive(r.BankAccountNumber),
        SwiftCode = RestaurantRegistrationRules.MaskSensitive(r.SwiftCode),
        Address = RestaurantRegistrationRules.MaskSensitive(r.Address),
        r.Latitude,
        r.Longitude,
        LegalEntityType = RestaurantRegistrationRules.NormalizeLegalEntityTypeForOutput(r.LegalEntityType),
        SettlementCurrency = RestaurantRegistrationRules.SettlementCurrency,
        r.DeliveryByRestaurantConfirmed,
        r.DataResponsibilityConfirmed,
        r.SubmissionDate,
        r.CreatedAtUtc,
        r.ModifiedAtUtc,
    };

    internal static IReadOnlyList<Guid> ParseServiceRegionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [])
                .Select(value => Guid.TryParse(value, out var id) ? id : (Guid?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    public static FieldReviewSummaryDto MapFieldReview(RestaurantRegistrationFieldReview f) => new(
        f.FieldKey, f.Status, f.Reason, f.ReviewedByUserId, f.ReviewedAtUtc);
}

public sealed record CreateAndSubmitTestRestaurantCommand(string? RestaurantName = null) : ICommand<TestRestaurantSubmissionDto>;

public sealed record TestRestaurantSubmissionDto(
    Guid RegistrationId,
    Guid OwnerUserId,
    string RestaurantName,
    string OwnerEmail,
    string Status,
    string AdminDeepLink);

public sealed class CreateAndSubmitTestRestaurantCommandHandler
    : ICommandHandler<CreateAndSubmitTestRestaurantCommand, TestRestaurantSubmissionDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationPushNotificationService _pushNotifier;
    private readonly IAuditLogger _audit;

    public CreateAndSubmitTestRestaurantCommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IAuditLogger audit,
        IRegistrationPushNotificationService? pushNotifier = null)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
        _pushNotifier = pushNotifier ?? NullRegistrationPushNotificationService.Instance;
    }

    public async Task<Result<TestRestaurantSubmissionDto>> Handle(
        CreateAndSubmitTestRestaurantCommand request,
        CancellationToken cancellationToken)
    {
        var suffix = Random.Shared.Next(1000, 9999);
        var tradeName = string.IsNullOrWhiteSpace(request.RestaurantName)
            ? $"مطعم تجريبي #{suffix}"
            : request.RestaurantName.Trim();

        var ownerUserId = Guid.NewGuid();
        var email = $"test.restaurant.{suffix}@mealmate.local";
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var registration = new Domain.IdentityAccess.RestaurantRegistration
        {
            SubmittedByUserId = ownerUserId,
            LegalNameAr = $"شركة {tradeName} ذ.م.م",
            LegalNameEn = $"Test Restaurant {suffix} Co.",
            TradeNameAr = tradeName,
            TradeNameEn = $"Test Restaurant {suffix}",
            LegalEntityType = "wll",
            CompanyCivilId = $"29{suffix}1234567",
            CommercialRegistrationNumber = $"CR{suffix}{Random.Shared.Next(100, 999)}",
            CommercialRegistrationExpiry = today.AddYears(2),
            CommercialLicenseNumber = $"CL{suffix}",
            CommercialLicenseExpiry = today.AddYears(2),
            FoodLicenseNumber = $"FL{suffix}",
            FoodLicenseExpiry = today.AddYears(2),
            ContactPhone = $"+9655000{suffix}",
            ContactEmail = email,
            PrimaryContactName = "أحمد السالم",
            PrimaryContactTitle = "المدير العام",
            PrimaryContactPhone = $"+9656000{suffix}",
            PrimaryContactEmail = email,
            OwnerName = "عبدالله الكندري",
            AuthorizedSignatoryCivilId = $"28{suffix}9876543",
            AuthorizedSignatoryTitle = "الشريك المفوض",
            BankName = "بنك الكويت الوطني (NBK)",
            BankBeneficiaryName = $"شركة {tradeName}",
            Iban = "KW81NBOK0000000000001234567890",
            BankAccountNumber = "1234567890",
            SwiftCode = "NBOKKWKW",
            Address = "مدينة الكويت - شرق - شارع أحمد الجابر",
            Latitude = 29.378586m,
            Longitude = 47.990341m,
            SettlementCurrency = "KWD",
            DeliveryByRestaurantConfirmed = true,
            DataResponsibilityConfirmed = true,
            Status = VerificationStatus.Submitted,
            SubmissionDate = today,
            CreatedAtUtc = _clock.UtcNow,
        };
        registration.RestaurantCode = RestaurantRegistrationRules.CreateRestaurantCode(registration.Id);
        _db.RestaurantRegistrations.Add(registration);

        var requiredDocs = new[]
        {
            DocumentType.CommercialRegistration,
            DocumentType.CommercialLicense,
            DocumentType.FoodLicense,
            DocumentType.AuthorizedSignatoryId,
            DocumentType.AuthorizedSignatoryAuthorization,
            DocumentType.BankIbanProof,
            DocumentType.ProfileImage,
            DocumentType.BranchLicense,
        };

        foreach (var docType in requiredDocs)
        {
            var doc = new Document
            {
                OwnerType = DocumentOwnerType.Registration,
                OwnerId = registration.Id,
                DocumentType = docType,
                FileName = $"{docType}.pdf",
                ContentType = docType is DocumentType.ProfileImage ? "image/png" : "application/pdf",
                StoragePath = $"test/seed/{registration.Id}/{docType}.pdf",
                SizeBytes = 1024,
                CreatedAtUtc = _clock.UtcNow,
            };
            doc.Verifications.Add(new DocumentVerification
            {
                DocumentId = doc.Id,
                Status = VerificationStatus.Submitted,
                CreatedAtUtc = _clock.UtcNow,
            });
            _db.Documents.Add(doc);
        }

        foreach (var fieldKey in RegistrationFieldKeys.All)
        {
            _db.RestaurantRegistrationFieldReviews.Add(new RestaurantRegistrationFieldReview
            {
                RegistrationId = registration.Id,
                FieldKey = fieldKey,
                Status = VerificationStatus.Submitted,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationSubmitted",
            "RestaurantRegistration",
            registration.Id,
            SourceChannel: "test_script"), cancellationToken);

        await _pushNotifier.NotifyAdminsNewRegistrationAsync(
            registration.Id,
            tradeName,
            cancellationToken);

        return new TestRestaurantSubmissionDto(
            registration.Id,
            ownerUserId,
            tradeName,
            email,
            "Submitted",
            $"/admin/accounts/pending/{registration.Id}");
    }
}
