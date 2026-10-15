using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Advertising;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("================================================================================");
Console.WriteLine(" MealMate Advertising & Promo Codes Initial Live Seeder");
Console.WriteLine("================================================================================");

var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION")
    ?? "Host=157.173.104.121;Port=5432;Database=mealmate_db;Username=mealmate_user;Password=Fahad1990@MealMate;Trust Server Certificate=true;Timeout=60;Command Timeout=120";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
        npgsql.UseNetTopologySuite();
    })
    .Options;

await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());

var strategy = db.Database.CreateExecutionStrategy();

await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
        var now = DateTime.UtcNow;

        // 1. Advertising Settings
        var settings = await db.Set<AdvertisingSettings>().FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new AdvertisingSettings
            {
                AuctionsEnabled = true,
                BusinessTimezone = "Asia/Kuwait",
                ExpectedVersion = 1,
                LastChangedAtUtc = now,
                LastChangeReason = "Production system initialization",
                CreatedAtUtc = now
            };
            db.Set<AdvertisingSettings>().Add(settings);
            await db.SaveChangesAsync();
            Console.WriteLine("[+] Seeded AdvertisingSettings: AuctionsEnabled=true, Timezone=Asia/Kuwait");
        }
        else
        {
            settings.AuctionsEnabled = true;
            settings.BusinessTimezone = "Asia/Kuwait";
            settings.LastChangedAtUtc = now;
            await db.SaveChangesAsync();
            Console.WriteLine("[OK] AdvertisingSettings already present; ensured AuctionsEnabled=true");
        }

        // 2. Commercial Policy
        var policy = await db.Set<AdCommercialPolicy>().FirstOrDefaultAsync(p => p.ApprovedByUserId.HasValue);
        if (policy == null)
        {
            var adminUser = await db.Set<ApplicationUser>().FirstOrDefaultAsync();
            var adminId = adminUser?.Id ?? Guid.NewGuid();

            policy = new AdCommercialPolicy
            {
                Version = 1,
                CurrencyCode = "KWD",
                MinimumBid = 5.0m,
                BidStep = 1.0m,
                TiePolicy = AdCommercialPolicy.TiePolicyFirstAccepted,
                WinnerPaymentDeadlineMinutes = 1440,
                NextBidderPolicy = AdCommercialPolicy.NextBidderPolicyOfferWithConsent,
                StopSettlementRule = "ProrataUnservedDays",
                ApprovedByUserId = adminId,
                ApprovedAtUtc = now,
                CreatedAtUtc = now
            };
            db.Set<AdCommercialPolicy>().Add(policy);
            await db.SaveChangesAsync();
            Console.WriteLine($"[+] Seeded Approved AdCommercialPolicy: Version={policy.Version}, MinBid={policy.MinimumBid} {policy.CurrencyCode}");
        }
        else
        {
            Console.WriteLine($"[OK] Approved AdCommercialPolicy already present: Version={policy.Version}, MinBid={policy.MinimumBid} {policy.CurrencyCode}");
        }

        // 3. Ad Spaces (Surfaces)
        var desiredSpaces = new[]
        {
            new AdSpace
            {
                PlacementKey = "home_hero",
                NameAr = "بانر الشاشة الرئيسية المميز",
                NameEn = "Home Screen Hero Banner",
                WidthPx = 1080,
                HeightPx = 400,
                MarketIdsJson = "[]",
                IsActive = true,
                CreatedAtUtc = now
            },
            new AdSpace
            {
                PlacementKey = "category_sponsor",
                NameAr = "بانر رعاية التصنيفات والبرامج",
                NameEn = "Category Sponsor Banner",
                WidthPx = 1080,
                HeightPx = 250,
                MarketIdsJson = "[]",
                IsActive = true,
                CreatedAtUtc = now
            },
            new AdSpace
            {
                PlacementKey = "offers_banner",
                NameAr = "بانر مركز العروض الكبرى",
                NameEn = "Offers Hub Top Banner",
                WidthPx = 1080,
                HeightPx = 300,
                MarketIdsJson = "[]",
                IsActive = true,
                CreatedAtUtc = now
            }
        };

        var existingSpaces = await db.Set<AdSpace>().ToListAsync();
        var spacesByKey = existingSpaces.ToDictionary(s => s.PlacementKey);

        foreach (var space in desiredSpaces)
        {
            if (!spacesByKey.ContainsKey(space.PlacementKey))
            {
                db.Set<AdSpace>().Add(space);
                spacesByKey[space.PlacementKey] = space;
                Console.WriteLine($"[+] Seeded AdSpace: Key={space.PlacementKey}, NameAr={space.NameAr}");
            }
            else
            {
                Console.WriteLine($"[OK] AdSpace already exists: Key={space.PlacementKey}");
            }
        }
        await db.SaveChangesAsync();

        // 4. Open Auction for Restaurant Bidding
        var heroSpace = spacesByKey["home_hero"];
        var openAuction = await db.Set<AdAuction>()
            .FirstOrDefaultAsync(a => a.AdSpaceId == heroSpace.Id && a.Status == AdAuctionStatus.Open);

        if (openAuction == null)
        {
            openAuction = new AdAuction
            {
                AdSpaceId = heroSpace.Id,
                Status = AdAuctionStatus.Open,
                BidsOpenAtUtc = now.AddDays(-1),
                BidsCloseAtUtc = now.AddDays(3),
                DisplayStartsAtUtc = now.AddDays(4),
                DisplayEndsAtUtc = now.AddDays(11),
                PolicyId = policy.Id,
                PolicyVersion = policy.Version,
                CurrencyCode = policy.CurrencyCode,
                CreatedAtUtc = now
            };
            db.Set<AdAuction>().Add(openAuction);
            await db.SaveChangesAsync();
            Console.WriteLine($"[+] Seeded Open AdAuction for Restaurants: ID={openAuction.Id}, Space={heroSpace.PlacementKey}, Window=[{openAuction.BidsOpenAtUtc:yyyy-MM-dd} to {openAuction.BidsCloseAtUtc:yyyy-MM-dd}]");
        }
        else
        {
            Console.WriteLine($"[OK] Open AdAuction already exists: ID={openAuction.Id}");
        }

        // 5. Active Ad Placement for Mobile Customer App Display
        var restaurant = await db.Set<RestaurantProfile>().FirstOrDefaultAsync(r => r.IsOperationallyActive);
        if (restaurant != null)
        {
            var activePlacement = await db.Set<AdPlacement>()
                .FirstOrDefaultAsync(p => p.AdSpaceId == heroSpace.Id && (p.Status == AdPlacementStatus.Active || p.Status == AdPlacementStatus.Scheduled));

            if (activePlacement == null)
            {
                // Create an awarded auction for historical display
                var displayAuction = new AdAuction
                {
                    AdSpaceId = heroSpace.Id,
                    Status = AdAuctionStatus.Awarded,
                    BidsOpenAtUtc = now.AddDays(-7),
                    BidsCloseAtUtc = now.AddDays(-2),
                    DisplayStartsAtUtc = now.AddDays(-1),
                    DisplayEndsAtUtc = now.AddDays(14),
                    PolicyId = policy.Id,
                    PolicyVersion = policy.Version,
                    CurrencyCode = policy.CurrencyCode,
                    WinningRestaurantId = restaurant.Id,
                    WinningAmount = 15.0m,
                    ClosedAtUtc = now.AddDays(-2),
                    CreatedAtUtc = now.AddDays(-7)
                };
                db.Set<AdAuction>().Add(displayAuction);
                await db.SaveChangesAsync();

                var bannerKey = "uploads/branding/banner-hero-mealmate.png";

                activePlacement = new AdPlacement
                {
                    AdAuctionId = displayAuction.Id,
                    RestaurantId = restaurant.Id,
                    AdSpaceId = heroSpace.Id,
                    Status = AdPlacementStatus.Active,
                    CreativeStorageKey = bannerKey,
                    TargetType = "restaurant",
                    TargetId = restaurant.Id,
                    DisplayStartsAtUtc = now.AddDays(-1),
                    DisplayEndsAtUtc = now.AddDays(14),
                    ReviewedByUserId = policy.ApprovedByUserId,
                    ReviewedAtUtc = now.AddDays(-1),
                    ReviewNotes = "Official launch partner banner",
                    CreatedAtUtc = now.AddDays(-1)
                };
                db.Set<AdPlacement>().Add(activePlacement);
                await db.SaveChangesAsync();
                Console.WriteLine($"[+] Seeded Active AdPlacement for Customer Mobile: ID={activePlacement.Id}, Restaurant={restaurant.TradeNameAr}, StorageKey={bannerKey}");
            }
            else
            {
                Console.WriteLine($"[OK] Active AdPlacement already exists: ID={activePlacement.Id}, Status={activePlacement.Status}");
            }
        }

        // 6. Production Promo Codes
        var promoList = new[]
        {
            new PromoCode
            {
                Code = "WELCOME10",
                DiscountPercent = Percentage.FromPercent(10.0m),
                MaxUses = 500,
                PerCustomerLimit = 1,
                UsedCount = 0,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddYears(1),
                IsActive = true,
                CreatedAtUtc = now
            },
            new PromoCode
            {
                Code = "MEALMATE20",
                DiscountPercent = Percentage.FromPercent(20.0m),
                MaxUses = 200,
                PerCustomerLimit = 1,
                UsedCount = 0,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddMonths(6),
                IsActive = true,
                CreatedAtUtc = now
            },
            new PromoCode
            {
                Code = "FAMILY15",
                DiscountPercent = Percentage.FromPercent(15.0m),
                MaxUses = 100,
                PerCustomerLimit = 1,
                UsedCount = 0,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddMonths(6),
                IsActive = true,
                TargetingJson = "{\"SubscriptionType\":\"Family\"}",
                CreatedAtUtc = now
            },
            new PromoCode
            {
                Code = "SAVE5KWD",
                DiscountAmount = new Money(5.0m, "KWD"),
                MaxUses = 100,
                PerCustomerLimit = 1,
                UsedCount = 0,
                StartsAtUtc = now.AddDays(-1),
                EndsAtUtc = now.AddMonths(3),
                IsActive = true,
                CreatedAtUtc = now
            }
        };

        var existingCodes = await db.Set<PromoCode>().Select(p => p.Code).ToListAsync();
        foreach (var promo in promoList)
        {
            if (!existingCodes.Contains(promo.Code))
            {
                db.Set<PromoCode>().Add(promo);
                Console.WriteLine($"[+] Seeded PromoCode '{promo.Code}': {(promo.DiscountPercent != null ? $"{promo.DiscountPercent.AsPercent}%" : $"{promo.DiscountAmount!.Amount} KWD")}, MaxUses={promo.MaxUses}");
            }
            else
            {
                Console.WriteLine($"[OK] PromoCode already exists: '{promo.Code}'");
            }
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        Console.WriteLine("\n[SUCCESS] All initial Advertising and Promo Code records successfully committed to PostgreSQL database.");
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"\n[ERROR]: {ex.Message}\n{ex.StackTrace}");
    }
});

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
