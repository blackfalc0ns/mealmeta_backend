using MealMate.Domain.ComplaintsSupport;
using MealMate.Persistence.Configurations.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealMate.Persistence.Configurations.ComplaintsSupport;

public sealed class ComplaintConfiguration : IEntityTypeConfiguration<Complaint>
{
    public void Configure(EntityTypeBuilder<Complaint> builder)
    {
        builder.ToTable("Complaints");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000).IsRequired();

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.SubscriptionDayId);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.DriverProfileId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.SubmittedAtUtc);
    }
}

public sealed class ComplaintEvidencePhotoConfiguration : IEntityTypeConfiguration<ComplaintEvidencePhoto>
{
    public void Configure(EntityTypeBuilder<ComplaintEvidencePhoto> builder)
    {
        builder.ToTable("ComplaintEvidencePhotos");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.UploadedAtUtc);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.EvidencePhotos)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ComplaintClassificationConfiguration : IEntityTypeConfiguration<ComplaintClassification>
{
    public void Configure(EntityTypeBuilder<ComplaintClassification> builder)
    {
        builder.ToTable("ComplaintClassifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CategoryCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.CategoryCode);
        builder.HasIndex(x => x.Severity);
        builder.HasIndex(x => x.ClassifiedByUserId);
        builder.HasIndex(x => x.ClassifiedAtUtc);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.Classifications)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdminComplaintReviewConfiguration : IEntityTypeConfiguration<AdminComplaintReview>
{
    public void Configure(EntityTypeBuilder<AdminComplaintReview> builder)
    {
        builder.ToTable("AdminComplaintReviews");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DecisionNotes).HasMaxLength(4000);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.ReviewerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ReviewedAtUtc);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.AdminReviews)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantComplaintResponseConfiguration : IEntityTypeConfiguration<RestaurantComplaintResponse>
{
    public void Configure(EntityTypeBuilder<RestaurantComplaintResponse> builder)
    {
        builder.ToTable("RestaurantComplaintResponses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResponseText).HasMaxLength(4000).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.RespondedByUserId);
        builder.HasIndex(x => x.RespondedAtUtc);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.RestaurantResponses)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ResponsibilityDecisionConfiguration : IEntityTypeConfiguration<ResponsibilityDecision>
{
    public void Configure(EntityTypeBuilder<ResponsibilityDecision> builder)
    {
        builder.ToTable("ResponsibilityDecisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(2000).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.Party);
        builder.HasIndex(x => x.DecidedByUserId);
        builder.HasIndex(x => x.DecidedAtUtc);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.ResponsibilityDecisions)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerCompensationConfiguration : IEntityTypeConfiguration<CustomerCompensation>
{
    public void Configure(EntityTypeBuilder<CustomerCompensation> builder)
    {
        builder.ToTable("CustomerCompensations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CompensationType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.OwnsMoney(x => x.Amount, "Amount", required: false);
        builder.OwnsMoney(x => x.PromoCreditAmount, "PromoCreditAmount", required: false);

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CompensationType);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.Compensations)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RestaurantBoxDeductionConfiguration : IEntityTypeConfiguration<RestaurantBoxDeduction>
{
    public void Configure(EntityTypeBuilder<RestaurantBoxDeduction> builder)
    {
        builder.ToTable("RestaurantBoxDeductions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.OwnsRequiredMoney(x => x.Amount, "Amount");

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.RestaurantId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.BoxDeductions)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FreeDayCompensationConfiguration : IEntityTypeConfiguration<FreeDayCompensation>
{
    public void Configure(EntityTypeBuilder<FreeDayCompensation> builder)
    {
        builder.ToTable("FreeDayCompensations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.ComplaintId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Complaint)
            .WithMany(x => x.FreeDayCompensations)
            .HasForeignKey(x => x.ComplaintId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("SupportTickets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(50).IsRequired();

        builder.HasIndex(x => x.CustomerUserId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.AssignedToUserId);
    }
}
