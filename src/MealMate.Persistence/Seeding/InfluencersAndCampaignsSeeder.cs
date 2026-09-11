using System.Text.Json;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Influencers;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class InfluencersAndCampaignsSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("InfluencersAndCampaignsSeeder");

        logger.LogInformation("Starting seeding of Influencers, Marketing Campaigns, and Withdrawals Hub data...");

        var now = DateTime.UtcNow;

        // 1. Seed Influencer Profiles if none exist
        if (!await db.InfluencerProfiles.AnyAsync(cancellationToken))
        {
            var inf1 = new InfluencerProfile
            {
                UserId = Guid.NewGuid(),
                DisplayName = "فهد المطيري (Fahad Fitness)",
                InfluencerCode = "FAHAD",
                DefaultCommissionRate = Percentage.FromPercent(10m),
                VerificationStatus = VerificationStatus.Approved,
                IsActive = true,
                BankName = "National Bank of Kuwait (NBK)",
                AccountHolderName = "Fahad Al-Mutairi",
                Iban = "KW81NBOK0000000001234567890123",
                HasCompletedSocialOnboarding = true,
                SocialLinksJson = JsonSerializer.Serialize(new
                {
                    instagram = "@fahad_fit",
                    tiktok = "@fahad.fitness",
                    youtube = "@FahadFitnessKW",
                    snapchat = "fahad_kw",
                    whatsapp = "+96599887766",
                    telegram = "t.me/fahad_fitness"
                }),
                CreatedAtUtc = now.AddDays(-60),
            };

            var inf2 = new InfluencerProfile
            {
                UserId = Guid.NewGuid(),
                DisplayName = "سارة القحطاني (Sara Diet)",
                InfluencerCode = "SARA",
                DefaultCommissionRate = Percentage.FromPercent(12m),
                VerificationStatus = VerificationStatus.Approved,
                IsActive = true,
                BankName = "Boubyan Bank",
                AccountHolderName = "Sara Al-Qahtani",
                Iban = "KW55BOUB0000000009876543210987",
                HasCompletedSocialOnboarding = true,
                SocialLinksJson = JsonSerializer.Serialize(new
                {
                    instagram = "@sara.diet",
                    snapchat = "sara_healthy",
                    whatsapp = "+96599112233",
                    tiktok = "@sara.nutrition"
                }),
                CreatedAtUtc = now.AddDays(-45),
            };

            var inf3 = new InfluencerProfile
            {
                UserId = Guid.NewGuid(),
                DisplayName = "أحمد الشمري (Ahmed Healthy)",
                InfluencerCode = "AHMED",
                DefaultCommissionRate = Percentage.FromPercent(8m),
                VerificationStatus = VerificationStatus.Approved,
                IsActive = true,
                BankName = "Gulf Bank",
                AccountHolderName = "Ahmed Al-Shammari",
                Iban = "KW12GULF0000000004567890123456",
                HasCompletedSocialOnboarding = true,
                SocialLinksJson = JsonSerializer.Serialize(new
                {
                    tiktok = "@ahmed.healthy",
                    telegram = "t.me/ahmed_healthy",
                    instagram = "@ahmed_kw_fit"
                }),
                CreatedAtUtc = now.AddDays(-30),
            };

            db.InfluencerProfiles.AddRange(inf1, inf2, inf3);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded 3 realistic Influencer profiles.");

            // 2. Seed Commissions for Wallet Balances
            var comm1 = new InfluencerCommissionCalculation
            {
                InfluencerProfileId = inf1.Id,
                SubscriptionId = Guid.NewGuid(),
                PaidAmountAfterDiscount = new Money(4500m, "KWD"),
                CommissionRate = Percentage.FromPercent(10m),
                CommissionAmount = new Money(450m, "KWD"),
                Status = PaymentStatus.Succeeded,
                CalculatedAtUtc = now.AddDays(-40),
            };
            var comm2 = new InfluencerCommissionCalculation
            {
                InfluencerProfileId = inf1.Id,
                SubscriptionId = Guid.NewGuid(),
                PaidAmountAfterDiscount = new Money(7500m, "KWD"),
                CommissionRate = Percentage.FromPercent(10m),
                CommissionAmount = new Money(750m, "KWD"),
                Status = PaymentStatus.Succeeded,
                CalculatedAtUtc = now.AddDays(-15),
            };
            var comm3 = new InfluencerCommissionCalculation
            {
                InfluencerProfileId = inf2.Id,
                SubscriptionId = Guid.NewGuid(),
                PaidAmountAfterDiscount = new Money(7083m, "KWD"),
                CommissionRate = Percentage.FromPercent(12m),
                CommissionAmount = new Money(850m, "KWD"),
                Status = PaymentStatus.Succeeded,
                CalculatedAtUtc = now.AddDays(-20),
            };
            var comm4 = new InfluencerCommissionCalculation
            {
                InfluencerProfileId = inf3.Id,
                SubscriptionId = Guid.NewGuid(),
                PaidAmountAfterDiscount = new Money(6250m, "KWD"),
                CommissionRate = Percentage.FromPercent(8m),
                CommissionAmount = new Money(500m, "KWD"),
                Status = PaymentStatus.Succeeded,
                CalculatedAtUtc = now.AddDays(-10),
            };

            db.InfluencerCommissionCalculations.AddRange(comm1, comm2, comm3, comm4);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded Influencer Commission Calculations.");

            // 3. Seed Withdrawal Requests covering all 4 major statuses
            // a) Completed/Paid Withdrawal
            var paidReq = new InfluencerWithdrawalRequest
            {
                InfluencerProfileId = inf1.Id,
                Amount = new Money(450m, "KWD"),
                Status = WithdrawalStatus.Paid,
                RequestedAtUtc = now.AddDays(-25),
                ReviewedAtUtc = now.AddDays(-23),
                ReferenceNumber = "WD-2026-08-15-0001",
                WithdrawalMethod = "BankTransfer",
                BankName = inf1.BankName,
                AccountHolderName = inf1.AccountHolderName,
                Iban = inf1.Iban,
                CreatedAtUtc = now.AddDays(-25),
            };

            db.InfluencerWithdrawalRequests.Add(paidReq);
            await db.SaveChangesAsync(cancellationToken);

            var payout = new InfluencerPayoutHistory
            {
                InfluencerProfileId = inf1.Id,
                WithdrawalRequestId = paidReq.Id,
                Amount = new Money(450m, "KWD"),
                PaidAtUtc = now.AddDays(-23),
                BankReference = "TRF-2026-9812450",
            };

            // b) Requested (Pending Admin Review)
            var pendingReq = new InfluencerWithdrawalRequest
            {
                InfluencerProfileId = inf1.Id,
                Amount = new Money(150m, "KWD"),
                Status = WithdrawalStatus.Requested,
                RequestedAtUtc = now.AddHours(-14),
                ReferenceNumber = "WD-2026-09-11-0002",
                WithdrawalMethod = "BankTransfer",
                BankName = inf1.BankName,
                AccountHolderName = inf1.AccountHolderName,
                Iban = inf1.Iban,
                CreatedAtUtc = now.AddHours(-14),
            };

            // c) Under Review
            var reviewReq = new InfluencerWithdrawalRequest
            {
                InfluencerProfileId = inf2.Id,
                Amount = new Money(300m, "KWD"),
                Status = WithdrawalStatus.UnderReview,
                RequestedAtUtc = now.AddDays(-2),
                ReviewedAtUtc = now.AddDays(-1),
                ReferenceNumber = "WD-2026-09-09-0003",
                WithdrawalMethod = "BankTransfer",
                BankName = inf2.BankName,
                AccountHolderName = inf2.AccountHolderName,
                Iban = inf2.Iban,
                CreatedAtUtc = now.AddDays(-2),
            };

            // d) Rejected (with Zero-Loss instant unfreeze)
            var rejectReq = new InfluencerWithdrawalRequest
            {
                InfluencerProfileId = inf3.Id,
                Amount = new Money(200m, "KWD"),
                Status = WithdrawalStatus.Rejected,
                RequestedAtUtc = now.AddDays(-5),
                ReviewedAtUtc = now.AddDays(-4),
                ReferenceNumber = "WD-2026-09-06-0004",
                WithdrawalMethod = "BankTransfer",
                BankName = inf3.BankName,
                AccountHolderName = inf3.AccountHolderName,
                Iban = inf3.Iban,
                RejectionReason = "رقم الآيبان البنكي غير مطابق لاسم المستفيد المسجل في النظام",
                CreatedAtUtc = now.AddDays(-5),
            };

            db.InfluencerWithdrawalRequests.AddRange(pendingReq, reviewReq, rejectReq);
            db.InfluencerPayoutHistories.Add(payout);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded 4 realistic Withdrawal Requests across all statuses.");
        }

        // 4. Seed Collaborative Marketing Campaign if none exist
        if (!await db.Campaigns.AnyAsync(cancellationToken))
        {
            var campaign = new Campaign
            {
                NameAr = "حملة الصيف الصحي 2026",
                NameEn = "Summer Health Campaign 2026",
                Status = CampaignStatus.Launched,
                StartsAtUtc = now.AddDays(-15),
                EndsAtUtc = now.AddDays(45),
                TotalDiscountRate = Percentage.FromPercent(20m),
                MealMateShareRate = Percentage.FromPercent(10m),
                RestaurantShareRate = Percentage.FromPercent(10m),
                FundingMode = CampaignFundingMode.Shared,
                MaxSubscribers = 100,
                CurrentSubscribers = 24,
                CreatedAtUtc = now.AddDays(-20),
            };

            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync(cancellationToken);

            var split = new CampaignDiscountSplit
            {
                CampaignId = campaign.Id,
                FundingMode = CampaignFundingMode.Shared,
                MealMateShareRate = Percentage.FromPercent(10m),
                RestaurantShareRate = Percentage.FromPercent(10m),
                Notes = "توزيع تكلفة الخصم 50% على المنصة و50% على المطعم المشترك",
                CreatedAtUtc = now.AddDays(-20),
            };

            db.CampaignDiscountSplits.Add(split);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded Collaborative Marketing Campaign.");
        }

        logger.LogInformation("Influencers, Campaigns, and Withdrawals seeding completed successfully.");
    }
}
