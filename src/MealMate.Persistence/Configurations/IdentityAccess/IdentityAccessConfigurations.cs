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
        builder.HasIndex(x => new { x.ScopeType, x.CountryId, x.RegionId, x.RestaurantId });
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
        builder.HasIndex(x => x.UserId).IsUnique();
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
        builder.Property(x => x.LegalNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LegalNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TradeNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContactPhone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.CommercialRegistrationNumber).HasMaxLength(100);
        builder.Property(x => x.OwnerName).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ChangeRequestNotes).HasMaxLength(2000);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.SubmittedByUserId);
        builder.HasIndex(x => x.CommercialRegistrationNumber);
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
        builder.Property(x => x.ProfileImageStorageKey).HasMaxLength(500);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.ChangeRequestNotes).HasMaxLength(2000);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RestaurantId);
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
