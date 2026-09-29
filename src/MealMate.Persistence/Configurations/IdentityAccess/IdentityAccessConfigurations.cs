using MealMate.Domain.IdentityAccess;
using MealMate.Persistence.Configurations.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.IdentityAccess;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(150).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Module);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
        builder.HasOne(x => x.Permission)
            .WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccessScopeConfiguration : IEntityTypeConfiguration<AccessScope>
{
    public void Configure(EntityTypeBuilder<AccessScope> builder)
    {
        builder.ToTable("AccessScopes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.HasIndex(x => new { x.ScopeType, x.CountryId, x.RegionId, x.RestaurantId, x.TenantId });
        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserScopeConfiguration : IEntityTypeConfiguration<UserScope>
{
    public void Configure(EntityTypeBuilder<UserScope> builder)
    {
        builder.ToTable("UserScopes");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.AccessScopeId }).IsUnique();
        builder.HasOne(x => x.AccessScope)
            .WithMany(x => x.UserScopes)
            .HasForeignKey(x => x.AccessScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("CustomerProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreferredLanguage).HasMaxLength(10);
        builder.Property(x => x.DeliveryAddressLine).HasMaxLength(500);
        builder.Property(x => x.DeliveryLatitude).HasPrecision(9, 6);
        builder.Property(x => x.DeliveryLongitude).HasPrecision(9, 6);
        builder.Property(x => x.ProfileImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.DietaryPattern).HasMaxLength(80);
        builder.Property(x => x.DietaryNotes).HasMaxLength(1000);
        builder.Property(x => x.AllergiesCompletedAtUtc);
        builder.Property(x => x.DietaryPreferencesCompletedAtUtc);
        builder.Property(x => x.ActiveDeliveryAddressLine).HasMaxLength(500);
        builder.HasIndex(x => x.UserId).IsUnique();
    }
}

public sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).HasMaxLength(80).IsRequired();
        builder.Property(x => x.AddressLine).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Governorate).HasMaxLength(100);
        builder.Property(x => x.Area).HasMaxLength(100);
        builder.Property(x => x.Block).HasMaxLength(50);
        builder.Property(x => x.Street).HasMaxLength(150);
        builder.Property(x => x.Avenue).HasMaxLength(50);
        builder.Property(x => x.Building).HasMaxLength(50);
        builder.Property(x => x.Unit).HasMaxLength(50);
        builder.Property(x => x.Floor).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.HasIndex(x => x.BlockRegionId);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.HasIndex(x => new { x.UserId, x.IsDefault });
    }
}

public sealed class RestaurantProfileConfiguration : IEntityTypeConfiguration<RestaurantProfile>
{
    public void Configure(EntityTypeBuilder<RestaurantProfile> builder)
    {
        builder.ToTable("RestaurantProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LegalNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LegalNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CommercialLicenseNumber).HasMaxLength(100);
        builder.Property(x => x.ContactPhone).HasMaxLength(30);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.CommissionPercent).HasPrecision(5, 2);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.ApprovedVersion).HasDefaultValue(1L);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.VerificationStatus);
        builder.HasIndex(x => x.IsOperationallyActive);
    }
}

public sealed class DriverProfileConfiguration : IEntityTypeConfiguration<DriverProfile>
{
    public void Configure(EntityTypeBuilder<DriverProfile> builder)
    {
        builder.ToTable("DriverProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FullNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(50);
        builder.Property(x => x.LicenseNumber).HasMaxLength(50);
        builder.Property(x => x.VehiclePlate).HasMaxLength(30);
        builder.Property(x => x.VehicleType).HasMaxLength(50);
        builder.Property(x => x.VehicleModel).HasMaxLength(100);
        builder.Property(x => x.VehicleColor).HasMaxLength(50);
        builder.Property(x => x.Nationality).HasMaxLength(100);
        builder.Property(x => x.ProfileImageStorageKey).HasMaxLength(500);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.VerificationStatus);
    }
}

public sealed class InfluencerProfileConfiguration : IEntityTypeConfiguration<InfluencerProfile>
{
    public void Configure(EntityTypeBuilder<InfluencerProfile> builder)
    {
        builder.ToTable("InfluencerProfiles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.InfluencerCode).HasMaxLength(50);
        builder.Property(x => x.LinkedCustomerUserIdRaw).HasMaxLength(50);
        builder.Property(x => x.Iban).HasMaxLength(50);
        builder.Property(x => x.BankName).HasMaxLength(200);
        builder.Property(x => x.AccountHolderName).HasMaxLength(200);
        builder.Property(x => x.SocialLinksJson).HasColumnType("jsonb");
        builder.Property(x => x.DefaultCommissionRate).HasNullablePercentageConversion();
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasIndex(x => x.LinkedCustomerUserId);
        builder.HasIndex(x => x.InfluencerCode).IsUnique().HasFilter("\"InfluencerCode\" IS NOT NULL");
    }
}

public sealed class RestaurantRegistrationConfiguration : IEntityTypeConfiguration<RestaurantRegistration>
{
    public void Configure(EntityTypeBuilder<RestaurantRegistration> builder)
    {
        builder.ToTable("RestaurantRegistrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.LegalNameAr).HasMaxLength(200);
        builder.Property(x => x.LegalNameEn).HasMaxLength(200);
        builder.Property(x => x.TradeNameAr).HasMaxLength(200);
        builder.Property(x => x.TradeNameEn).HasMaxLength(200);
        builder.Property(x => x.ContactPhone).HasMaxLength(30);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.CommercialRegistrationNumber).HasMaxLength(100);
        builder.Property(x => x.CommercialRegistrationExpiry);
        builder.Property(x => x.CommercialLicenseNumber).HasMaxLength(100);
        builder.Property(x => x.CommercialLicenseExpiry);
        builder.Property(x => x.FoodLicenseNumber).HasMaxLength(100);
        builder.Property(x => x.FoodLicenseExpiry);
        builder.Property(x => x.LegalEntityType).HasMaxLength(100);
        builder.Property(x => x.CompanyCivilId).HasMaxLength(50);
        builder.Property(x => x.Website).HasMaxLength(500);
        builder.Property(x => x.InstagramHandle).HasMaxLength(100);
        builder.Property(x => x.RestaurantCode).HasMaxLength(50);
        builder.Property(x => x.OwnerName).HasMaxLength(200);
        builder.Property(x => x.AuthorizedSignatoryCivilId).HasMaxLength(50);
        builder.Property(x => x.AuthorizedSignatoryTitle).HasMaxLength(200);
        builder.Property(x => x.PrimaryContactName).HasMaxLength(200);
        builder.Property(x => x.PrimaryContactTitle).HasMaxLength(200);
        builder.Property(x => x.PrimaryContactPhone).HasMaxLength(30);
        builder.Property(x => x.PrimaryContactEmail).HasMaxLength(256);
        builder.Property(x => x.BankName).HasMaxLength(200);
        builder.Property(x => x.BankBeneficiaryName).HasMaxLength(200);
        builder.Property(x => x.Iban).HasMaxLength(34);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(100);
        builder.Property(x => x.SwiftCode).HasMaxLength(20);
        builder.Property(x => x.SettlementCurrency).HasMaxLength(10);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Latitude).HasPrecision(9, 6);
        builder.Property(x => x.Longitude).HasPrecision(9, 6);
        builder.Property(x => x.SubmissionDate);
        builder.Property(x => x.RestaurantNotes).HasMaxLength(2000);
        builder.Property(x => x.DeliveryByRestaurantConfirmed);
        builder.Property(x => x.DataResponsibilityConfirmed);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ChangeRequestNotes).HasMaxLength(2000);
        builder.Property(x => x.ServiceRegionIdsJson).HasMaxLength(4000);
        builder.Property(x => x.SelectedProgramIdsJson).HasMaxLength(4000);
        builder.Property(x => x.SelectedBundleIdsJson).HasMaxLength(4000);
        builder.Property(x => x.ApprovedVersion).HasDefaultValue(1L);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.SubmittedByUserId);
        builder.HasIndex(x => x.CommercialRegistrationNumber);
        builder.HasIndex(x => x.RestaurantCode)
            .IsUnique()
            .HasFilter("\"RestaurantCode\" IS NOT NULL");
    }
}

public sealed class DriverRegistrationConfiguration : IEntityTypeConfiguration<DriverRegistration>
{
    public void Configure(EntityTypeBuilder<DriverRegistration> builder)
    {
        builder.ToTable("DriverRegistrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FullNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.NationalId).HasMaxLength(50);
        builder.Property(x => x.LicenseNumber).HasMaxLength(50);
        builder.Property(x => x.VehiclePlate).HasMaxLength(30);
        builder.Property(x => x.VehicleType).HasMaxLength(50);
        builder.Property(x => x.VehicleModel).HasMaxLength(100);
        builder.Property(x => x.ProfileImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ChangeRequestNotes).HasMaxLength(2000);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RestaurantId);
    }
}

public sealed class DeliveryManagerRegistrationConfiguration : IEntityTypeConfiguration<DeliveryManagerRegistration>
{
    public void Configure(EntityTypeBuilder<DeliveryManagerRegistration> builder)
    {
        builder.ToTable("DeliveryManagerRegistrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CoverageScopeId).HasMaxLength(100);
        builder.Property(x => x.CoverageScopeName).HasMaxLength(200);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ChangeRequestNotes).HasMaxLength(2000);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.NationalId);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeviceInfo).HasMaxLength(500);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("OtpChallenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Destination).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.Destination, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class AuthLoginChallengeConfiguration : IEntityTypeConfiguration<AuthLoginChallenge>
{
    public void Configure(EntityTypeBuilder<AuthLoginChallenge> builder)
    {
        builder.ToTable("AuthLoginChallenges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NextStep).HasMaxLength(30).IsRequired();
        builder.Property(x => x.DeviceInfo).HasMaxLength(500);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(150).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.SourceChannel).HasMaxLength(50);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.ActorUserId);
        builder.HasIndex(x => x.CorrelationId);
    }
}

public sealed class RestaurantRegistrationFieldReviewConfiguration
    : IEntityTypeConfiguration<RestaurantRegistrationFieldReview>
{
    public void Configure(EntityTypeBuilder<RestaurantRegistrationFieldReview> builder)
    {
        builder.ToTable("RestaurantRegistrationFieldReviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => new { x.RegistrationId, x.FieldKey }).IsUnique();
        builder.HasOne(x => x.Registration)
            .WithMany(x => x.FieldReviews)
            .HasForeignKey(x => x.RegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RestaurantReviewItemConfiguration : IEntityTypeConfiguration<RestaurantReviewItem>
{
    public void Configure(EntityTypeBuilder<RestaurantReviewItem> builder)
    {
        builder.ToTable("RestaurantReviewItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Section).HasMaxLength(80).IsRequired();
        builder.Property(x => x.SubjectType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FieldKey).HasMaxLength(120);
        builder.Property(x => x.VersionId).HasMaxLength(120);
        builder.Property(x => x.Reason).HasMaxLength(4000);
        builder.HasIndex(x => new { x.RestaurantId, x.Section, x.Status });
        builder.HasIndex(x => new { x.RegistrationId, x.SubjectType, x.SubjectId, x.FieldKey }).IsUnique();
    }
}

public sealed class RestaurantReviewHistoryConfiguration : IEntityTypeConfiguration<RestaurantReviewHistory>
{
    public void Configure(EntityTypeBuilder<RestaurantReviewHistory> builder)
    {
        builder.ToTable("RestaurantReviewHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(4000);
        builder.HasIndex(x => new { x.ReviewItemId, x.CreatedAtUtc });
    }
}

public sealed class RestaurantNotificationConfiguration : IEntityTypeConfiguration<RestaurantNotification>
{
    public void Configure(EntityTypeBuilder<RestaurantNotification> builder)
    {
        builder.ToTable("RestaurantNotifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(80).IsRequired();
        builder.Property(x => x.TitleAr).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.BodyAr).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.BodyEn).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Section).HasMaxLength(80);
        builder.Property(x => x.DeepLink).HasMaxLength(500);
        builder.HasIndex(x => new { x.UserId, x.IsRead, x.SentAtUtc });
    }
}

public sealed class RestaurantSettingsChangeRequestConfiguration : IEntityTypeConfiguration<RestaurantSettingsChangeRequest>
{
    public void Configure(EntityTypeBuilder<RestaurantSettingsChangeRequest> builder)
    {
        builder.ToTable("RestaurantSettingsChangeRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.AdminNotes).HasMaxLength(4000);
        builder.Property(x => x.Category).HasDefaultValue(Domain.Enums.RestaurantChangeCategory.Profile);
        builder.Property(x => x.BaseVersion).HasDefaultValue(1L);
        builder.Property(x => x.PayloadVersion).HasDefaultValue(1);
        builder.Property(x => x.ChangedFieldsJson).HasMaxLength(4000);
        builder.Property(x => x.DecisionEventsJson);
        builder.HasIndex(x => new { x.RestaurantId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.RestaurantId, x.Category, x.Status });
    }
}

public sealed class RestaurantNotificationPreferenceConfiguration
    : IEntityTypeConfiguration<RestaurantNotificationPreference>
{
    public void Configure(EntityTypeBuilder<RestaurantNotificationPreference> builder)
    {
        builder.ToTable("RestaurantNotificationPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.RestaurantId, x.EventKey }).IsUnique();
    }
}

public sealed class CustomerDeviceTokenConfiguration : IEntityTypeConfiguration<CustomerDeviceToken>
{
    public void Configure(EntityTypeBuilder<CustomerDeviceToken> builder)
    {
        builder.ToTable("CustomerDeviceTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(128);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsActive });
        builder.HasIndex(x => new { x.UserId, x.DeviceId });
    }
}

public sealed class AdminDeviceTokenConfiguration : IEntityTypeConfiguration<AdminDeviceToken>
{
    public void Configure(EntityTypeBuilder<AdminDeviceToken> builder)
    {
        builder.ToTable("AdminDeviceTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(128);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsActive });
        builder.HasIndex(x => new { x.UserId, x.DeviceId });
    }
}

public sealed class RestaurantDeviceTokenConfiguration : IEntityTypeConfiguration<RestaurantDeviceToken>
{
    public void Configure(EntityTypeBuilder<RestaurantDeviceToken> builder)
    {
        builder.ToTable("RestaurantDeviceTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Token).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(128);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsActive });
        builder.HasIndex(x => new { x.RestaurantId, x.IsActive });
        builder.HasIndex(x => new { x.UserId, x.DeviceId });
    }
}

public sealed class CustomerPushDeliveryLogConfiguration : IEntityTypeConfiguration<CustomerPushDeliveryLog>
{
    public void Configure(EntityTypeBuilder<CustomerPushDeliveryLog> builder)
    {
        builder.ToTable("CustomerPushDeliveryLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.EventKey, x.SubjectId }).IsUnique();
    }
}

public sealed class AdminInvitationConfiguration : IEntityTypeConfiguration<AdminInvitation>
{
    public void Configure(EntityTypeBuilder<AdminInvitation> builder)
    {
        builder.ToTable("AdminInvitations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RevokedReason).HasMaxLength(1000);
        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"Status\" = 1");
        builder.HasIndex(x => x.Status);
        builder.HasOne(x => x.AccessScope)
            .WithMany()
            .HasForeignKey(x => x.AccessScopeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
