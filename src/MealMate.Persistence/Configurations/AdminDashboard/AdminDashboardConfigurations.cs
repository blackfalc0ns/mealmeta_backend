using MealMate.Domain.AdminDashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.AdminDashboard;

public sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Key).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ValueJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);

        builder.HasIndex(x => x.Key).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class ProfitabilityAlertConfiguration : IEntityTypeConfiguration<ProfitabilityAlert>
{
    public void Configure(EntityTypeBuilder<ProfitabilityAlert> builder)
    {
        builder.ToTable("ProfitabilityAlerts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceDailyBox).HasPrecision(18, 3);
        builder.Property(x => x.DailySupplyGross).HasPrecision(18, 3);
        builder.Property(x => x.MeanReference).HasPrecision(18, 3);
        builder.Property(x => x.CustomerDailyPrice).HasPrecision(18, 3);
        builder.Property(x => x.ExpectedProfit).HasPrecision(18, 3);
        builder.Property(x => x.MinimumProfit).HasPrecision(18, 3);
        builder.Property(x => x.Reason).HasMaxLength(2000);

        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.ProgramId);
        builder.HasIndex(x => x.BundleId);
        builder.HasIndex(x => new { x.RestaurantId, x.ProgramId, x.BundleId });
        builder.HasIndex(x => x.ClassificationRunId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ResolvedByUserId);
        builder.HasIndex(x => x.ResolvedAtUtc);
    }
}

public sealed class AdminNotificationConfiguration : IEntityTypeConfiguration<AdminNotification>
{
    public void Configure(EntityTypeBuilder<AdminNotification> builder)
    {
        builder.ToTable("AdminNotifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TitleAr).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TitleEn).HasMaxLength(300).IsRequired();
        builder.Property(x => x.BodyAr).HasMaxLength(4000);
        builder.Property(x => x.BodyEn).HasMaxLength(4000);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.Channel);
        builder.HasIndex(x => x.IsRead);
        builder.HasIndex(x => x.SentAtUtc);
        builder.HasIndex(x => new { x.UserId, x.IsRead, x.SentAtUtc });
    }
}
