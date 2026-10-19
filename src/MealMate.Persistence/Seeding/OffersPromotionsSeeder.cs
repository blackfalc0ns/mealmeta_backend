using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class OffersPromotionsSeeder
{
    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // 1. Seed Active Offers
        var activeOffers = await db.Offers
            .Include(o => o.EligibleRestaurants)
            .ToListAsync(cancellationToken);

        var restaurants = await db.RestaurantProfiles
            .Where(r => r.IsOperationallyActive && !r.IsArchived && !r.IsDeleted)
            .Take(6)
            .ToListAsync(cancellationToken);

        var seededOffers = new List<Offer>();

        if (!activeOffers.Any(o => o.PromoCode == "WEEKLY25"))
        {
            var offer1 = new Offer
            {
                NameAr = "عرض بداية الأسبوع - خصم 25%",
                NameEn = "Weekly Kickoff - 25% Off",
                DescriptionAr = "استمتع بخصم 25% على جميع باقات الوجبات الصحية عند الاشتراك لمدة أسبوعين أو أكثر.",
                DescriptionEn = "Enjoy 25% off all healthy meal packages when subscribing for 2 weeks or more.",
                BannerImageUrl = "https://ik.imagekit.io/f7h9cj23x/offers/offer-weekly-25.jpg",
                PromoCode = "WEEKLY25",
                Status = OfferStatus.Active,
                Type = OfferType.StandardDiscount,
                DiscountType = DiscountType.Percentage,
                DiscountRate = Percentage.FromPercent(25m),
                FundingMode = OfferFundingMode.PlatformFull,
                StartsAtUtc = now.AddDays(-7),
                EndsAtUtc = now.AddDays(60),
                TotalUsageLimit = 1000,
                CurrentUsageCount = 15,
                PerCustomerLimit = 1,
                CustomerSegment = CustomerSegmentFilter.All,
                CreatedAtUtc = now.AddDays(-7),
            };

            foreach (var r in restaurants.Take(2))
            {
                offer1.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = r.Id });
            }

            db.Offers.Add(offer1);
            seededOffers.Add(offer1);
        }

        if (!activeOffers.Any(o => o.PromoCode == "KETO5KWD"))
        {
            var offer2 = new Offer
            {
                NameAr = "خصم باقات الكيتو - 5 د.ك",
                NameEn = "Keto Plans Special - 5 KWD Off",
                DescriptionAr = "وفر 5 دنانير كويتية عند اشتراكك في أي باقة من باقات الكيتو منخفضة الكربوهيدرات.",
                DescriptionEn = "Save 5 KWD on any keto subscription plan with low carbs and healthy fats.",
                BannerImageUrl = "https://ik.imagekit.io/f7h9cj23x/offers/offer-keto-5kwd.jpg",
                PromoCode = "KETO5KWD",
                Status = OfferStatus.Active,
                Type = OfferType.StandardDiscount,
                DiscountType = DiscountType.FixedAmount,
                DiscountAmount = new Money(5.000m, "KWD"),
                FundingMode = OfferFundingMode.PlatformFull,
                StartsAtUtc = now.AddDays(-4),
                EndsAtUtc = now.AddDays(45),
                TotalUsageLimit = 500,
                CurrentUsageCount = 8,
                PerCustomerLimit = 1,
                CustomerSegment = CustomerSegmentFilter.All,
                CreatedAtUtc = now.AddDays(-4),
            };

            foreach (var r in restaurants.Skip(2).Take(2))
            {
                offer2.EligibleRestaurants.Add(new OfferRestaurant { RestaurantId = r.Id });
            }

            db.Offers.Add(offer2);
            seededOffers.Add(offer2);
        }

        if (!activeOffers.Any(o => o.PromoCode == "FAMILY30"))
        {
            var offer3 = new Offer
            {
                NameAr = "عرض التوفير العائلي - خصم 30%",
                NameEn = "Family Savings - 30% Off",
                DescriptionAr = "خصم استثنائي 30% لجميع أفراد العائلة عند الاشتراك في الباقات العائلية المشتركة.",
                DescriptionEn = "Exceptional 30% discount for all family members on shared family subscription plans.",
                BannerImageUrl = "https://ik.imagekit.io/f7h9cj23x/offers/offer-family-30.jpg",
                PromoCode = "FAMILY30",
                Status = OfferStatus.Active,
                Type = OfferType.StandardDiscount,
                DiscountType = DiscountType.Percentage,
                DiscountRate = Percentage.FromPercent(30m),
                FundingMode = OfferFundingMode.PlatformFull,
                StartsAtUtc = now.AddDays(-2),
                EndsAtUtc = now.AddDays(90),
                TotalUsageLimit = 2000,
                CurrentUsageCount = 42,
                PerCustomerLimit = 1,
                CustomerSegment = CustomerSegmentFilter.All,
                CreatedAtUtc = now.AddDays(-2),
            };

            db.Offers.Add(offer3);
            seededOffers.Add(offer3);
        }

        if (!activeOffers.Any(o => o.PromoCode == "ATHLETE20"))
        {
            var offer4 = new Offer
            {
                NameAr = "عرض باقات الرياضيين - خصم 20%",
                NameEn = "Athletic Performance - 20% Off",
                DescriptionAr = "خصم 20% على باقات الأداء الرياضي وبناء العضلات للرياضيين وممارسي اللياقة.",
                DescriptionEn = "20% off athletic performance and muscle gain packages for athletes and fitness enthusiasts.",
                BannerImageUrl = "https://ik.imagekit.io/f7h9cj23x/offers/offer-athlete-20.jpg",
                PromoCode = "ATHLETE20",
                Status = OfferStatus.Active,
                Type = OfferType.StandardDiscount,
                DiscountType = DiscountType.Percentage,
                DiscountRate = Percentage.FromPercent(20m),
                FundingMode = OfferFundingMode.PlatformFull,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddDays(30),
                TotalUsageLimit = 300,
                CurrentUsageCount = 19,
                PerCustomerLimit = 1,
                CustomerSegment = CustomerSegmentFilter.All,
                CreatedAtUtc = now.AddDays(-1),
            };

            db.Offers.Add(offer4);
            seededOffers.Add(offer4);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded offers. Total new offers added: {Count}", seededOffers.Count);

        // Reload all current offers
        var allCurrentOffers = await db.Offers
            .Where(o => o.Status == OfferStatus.Active && o.StartsAtUtc <= now && o.EndsAtUtc >= now)
            .ToListAsync(cancellationToken);

        // 2. Seed Customer Wallets, Referral Codes, Saved Offers, and Calendar for customers
        var customerUsers = await db.Users
            .Where(u => u.UserType == UserType.Customer)
            .Take(25)
            .ToListAsync(cancellationToken);

        if (customerUsers.Count == 0)
        {
            // If no customer users found, also check CustomerProfiles
            var profileUserIds = await db.CustomerProfiles.Select(cp => cp.UserId).Take(25).ToListAsync(cancellationToken);
            customerUsers = await db.Users.Where(u => profileUserIds.Contains(u.Id)).ToListAsync(cancellationToken);
        }

        logger.LogInformation("Ensuring wallet and referral data for {Count} customer users...", customerUsers.Count);

        foreach (var user in customerUsers)
        {
            // Referral Code
            var hasReferralCode = await db.ReferralCodes.AnyAsync(r => r.OwnerUserId == user.Id, cancellationToken);
            if (!hasReferralCode)
            {
                var suffix = user.Id.ToString("N")[..4].ToUpperInvariant();
                var refCode = "MATE" + suffix;
                db.ReferralCodes.Add(new ReferralCode
                {
                    Code = refCode,
                    OwnerUserId = user.Id,
                    RewardPercent = Percentage.FromPercent(30m),
                    MaxUses = 100,
                    UsedCount = 2,
                    IsActive = true,
                    CreatedAtUtc = now.AddDays(-10),
                });
            }

            // Promotional Wallet
            var promoWallet = await db.CustomerPromotionalWallets.FirstOrDefaultAsync(w => w.CustomerUserId == user.Id, cancellationToken);
            if (promoWallet is null)
            {
                promoWallet = new CustomerPromotionalWallet
                {
                    CustomerUserId = user.Id,
                    Balance = new Money(5.000m, "KWD"),
                    IsActive = true,
                    CreatedAtUtc = now.AddDays(-10),
                };
                db.CustomerPromotionalWallets.Add(promoWallet);

                db.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = promoWallet.Id,
                    WalletType = WalletType.Promotional,
                    CustomerUserId = user.Id,
                    Type = WalletTransactionType.Credit,
                    Amount = new Money(5.000m, "KWD"),
                    BalanceAfter = new Money(5.000m, "KWD"),
                    ReferenceType = "ReferralReward",
                    ReferenceId = Guid.NewGuid(),
                    OccurredAtUtc = now.AddDays(-3),
                    Description = "مكافأة دعوة أصدقاء / Referral Reward Credit",
                });
            }

            // Cash Wallet
            var cashWallet = await db.CustomerCashWallets.FirstOrDefaultAsync(w => w.CustomerUserId == user.Id, cancellationToken);
            if (cashWallet is null)
            {
                cashWallet = new CustomerCashWallet
                {
                    CustomerUserId = user.Id,
                    Balance = new Money(12.500m, "KWD"),
                    IsActive = true,
                    CreatedAtUtc = now.AddDays(-15),
                };
                db.CustomerCashWallets.Add(cashWallet);

                db.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = cashWallet.Id,
                    WalletType = WalletType.Cash,
                    CustomerUserId = user.Id,
                    Type = WalletTransactionType.Credit,
                    Amount = new Money(12.500m, "KWD"),
                    BalanceAfter = new Money(12.500m, "KWD"),
                    ReferenceType = "SubscriptionRefund",
                    ReferenceId = Guid.NewGuid(),
                    OccurredAtUtc = now.AddDays(-5),
                    Description = "رصيد مسترجع من اشتراك ملغى / Refund Credit",
                });
            }

            // Saved Offers
            if (allCurrentOffers.Count > 0)
            {
                var targetOffer = allCurrentOffers[0];
                var isSaved = await db.CustomerSavedOffers.AnyAsync(s => s.CustomerUserId == user.Id && s.OfferId == targetOffer.Id, cancellationToken);
                if (!isSaved)
                {
                    db.CustomerSavedOffers.Add(new CustomerSavedOffer
                    {
                        CustomerUserId = user.Id,
                        OfferId = targetOffer.Id,
                        CreatedAtUtc = now.AddDays(-1),
                    });
                }
            }

            // Calendar Reminders
            if (allCurrentOffers.Count > 1)
            {
                var calOffer = allCurrentOffers[1];
                var schedDate = DateOnly.FromDateTime(now.AddDays(3));
                var hasCal = await db.CustomerOfferCalendarEntries.AnyAsync(c => c.CustomerUserId == user.Id && c.OfferId == calOffer.Id, cancellationToken);
                if (!hasCal)
                {
                    db.CustomerOfferCalendarEntries.Add(new CustomerOfferCalendarEntry
                    {
                        CustomerUserId = user.Id,
                        OfferId = calOffer.Id,
                        ScheduledDate = schedDate,
                        Status = CustomerOfferCalendarEntryStatus.Active,
                        Source = "offer-detail",
                        CreatedAtUtc = now.AddHours(-12),
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Successfully completed seeding of Offers, Wallets, Referrals, Saved Offers, and Calendar!");
    }
}
