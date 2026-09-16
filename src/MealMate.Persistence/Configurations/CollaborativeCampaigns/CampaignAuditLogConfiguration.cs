using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.CollaborativeCampaigns;

public sealed class CampaignAuditLogConfiguration : IEntityTypeConfiguration<CampaignAuditLog>
{
    public void Configure(EntityTypeBuilder<CampaignAuditLog> builder)
    {
        builder.ToTable("CampaignAuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CampaignId).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BeforeStateJson).HasColumnType("text");
        builder.Property(x => x.AfterStateJson).HasColumnType("text");
        builder.Property(x => x.DetailsJson).HasColumnType("text");
        builder.Property(x => x.JustificationReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.CampaignId);
        builder.HasIndex(x => x.ActorId);
        builder.HasIndex(x => x.Action);
        builder.HasIndex(x => x.CreatedAtUtc);

        builder.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
