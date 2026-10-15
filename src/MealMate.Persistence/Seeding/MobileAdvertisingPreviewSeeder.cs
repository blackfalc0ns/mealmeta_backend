using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Advertising;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace MealMate.Persistence.Seeding;

/// <summary>
/// Opt-in development fixtures for rendering the mobile advertising surfaces.
/// All business fixtures carry an explicit preview marker and require existing
/// catalog and restaurant records plus a supplied public preview asset.
/// </summary>
public static class MobileAdvertisingPreviewSeeder
{
    private const string PreviewCodePrefix = "MM-MOBILE-AD-PREVIEW-";
    private const string PreviewPlacementMarker = "[MM-MOBILE-AD-PREVIEW]";
    private static readonly TimeSpan BannerUrlLifetime = TimeSpan.FromDays(30);

    public static async Task SeedAsync(IServiceProvider services, string placementKey, string assetFilePath, Guid? customerId = null, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var now = DateTime.UtcNow;

        var programs = await db.Programs
            .Where(program => program.IsActive && !program.IsDeleted)
            .OrderBy(program => program.SortOrder)
            .ToListAsync(cancellationToken);
        var catalogProgram = programs.FirstOrDefault();
        var catalogBundles = catalogProgram is null ? new List<Bundle>() : await db.Bundles
            .Where(bundle => bundle.ProgramId == catalogProgram.Id && bundle.IsActive && !bundle.IsDeleted)
            .OrderBy(bundle => bundle.SortOrder)
            .ToListAsync(cancellationToken);
        if (catalogProgram is null || catalogBundles.Count == 0)
        {
            throw new InvalidOperationException(
                "Mobile advertising preview requires an existing active program and bundle. No fixtures were written.");
        }

        var assetPath = Path.GetFullPath(assetFilePath);
        if (!File.Exists(assetPath))
            throw new FileNotFoundException("The supplied mobile preview asset file does not exist; no fixtures were written.", assetPath);
        var assetInfo = new FileInfo(assetPath);
        if (assetInfo.Length is < 1024 or > 25_000_000)
            throw new InvalidOperationException("The supplied preview image must be between 1 KB and 25 MB; no fixtures were written.");
        await using (var input = File.OpenRead(assetPath))
        {
            var signature = new byte[3];
            var bytesRead = await input.ReadAsync(signature, cancellationToken);
            if (bytesRead < 3 || signature[0] != 0xFF || signature[1] != 0xD8 || signature[2] != 0xFF)
                throw new InvalidOperationException("The supplied preview asset must be a JPEG image; no fixtures were written.");
        }

        var restaurant = await db.RestaurantProfiles
            .Where(item => item.IsOperationallyActive && !item.IsArchived && !item.IsDeleted)
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var adSpace = await db.AdSpaces.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.PlacementKey == placementKey, cancellationToken);
        var placement = adSpace is null ? null : await db.AdPlacements.IgnoreQueryFilters()
            .Include(item => item.AdAuction)
            .SingleOrDefaultAsync(item => item.AdSpaceId == adSpace.Id && item.ReviewNotes == PreviewPlacementMarker, cancellationToken);
        var policy = await db.AdCommercialPolicies
            .Where(item => item.ApprovedByUserId.HasValue && item.ApprovedAtUtc.HasValue && !item.IsDeleted)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        if (policy is null && placement is not null)
        {
            policy = await db.AdCommercialPolicies.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Id == placement.AdAuction.PolicyId && !item.IsDeleted, cancellationToken);
        }
        if (restaurant is null)
        {
            throw new InvalidOperationException(
                "Mobile advertising preview requires an existing operational restaurant. No fixtures were written.");
        }

        var regionId = (Guid?)null;
        if (customerId.HasValue)
        {
            regionId = await db.CustomerAddresses.AsNoTracking()
                .Where(address => address.UserId == customerId.Value && address.IsDefault)
                .Select(address => address.RegionId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!regionId.HasValue)
            {
                throw new InvalidOperationException(
                    "The selected customer has no default address with a RegionId. Supply a customer with a trusted default delivery address; no fixtures were written.");
            }
        }

        if (adSpace is not null && (!adSpace.IsActive || adSpace.IsDeleted))
        {
            throw new InvalidOperationException($"Placement key '{placementKey}' exists but is inactive or deleted; no fixtures were written.");
        }
        if (adSpace is not null && !MarketIncludesRegion(adSpace.MarketIdsJson, regionId))
        {
            throw new InvalidOperationException(
                $"Placement key '{placementKey}' does not include the selected customer's default RegionId. Select a customer in the configured market; no fixtures were written.");
        }

        if (adSpace is null)
        {
            adSpace = new AdSpace
            {
                PlacementKey = placementKey,
                NameAr = "[MM-MOBILE-AD-PREVIEW] مساحة إعلان معاينة",
                NameEn = "[MM-MOBILE-AD-PREVIEW] Mobile preview slot",
                WidthPx = 1200,
                HeightPx = 400,
                MarketIdsJson = JsonSerializer.Serialize(regionId.HasValue ? new[] { regionId.Value } : Array.Empty<Guid>()),
                IsActive = true,
                CreatedAtUtc = now
            };
            db.AdSpaces.Add(adSpace);
        }

        var previewStartsAt = now.AddMinutes(-1);
        var previewEndsAt = now.AddDays(30);
        var previewAuctionId = placement?.AdAuctionId;
        var collidingPlacement = await db.AdPlacements.IgnoreQueryFilters()
            .AnyAsync(item => item.AdSpaceId == adSpace.Id
                && item.ReviewNotes != PreviewPlacementMarker
                && item.Status == AdPlacementStatus.Active
                && item.DisplayStartsAtUtc < previewEndsAt
                && item.DisplayEndsAtUtc > now, cancellationToken);
        if (collidingPlacement)
        {
            throw new InvalidOperationException($"Placement key '{placementKey}' already has an overlapping active creative. Choose another mobile placement key; no fixtures were written.");
        }

        var collidingAuction = await db.AdAuctions.IgnoreQueryFilters()
            .AnyAsync(item => item.AdSpaceId == adSpace.Id
                && item.Status != AdAuctionStatus.Cancelled
                && item.Status != AdAuctionStatus.NoFill
                && (!previewAuctionId.HasValue || previewAuctionId.Value != item.Id)
                && item.DisplayStartsAtUtc < previewEndsAt
                && item.DisplayEndsAtUtc > now, cancellationToken);
        if (collidingAuction)
        {
            throw new InvalidOperationException($"Placement key '{placementKey}' has an overlapping reserved auction window. No fixtures were written.");
        }

        if (policy is null)
        {
            var nextVersion = (await db.AdCommercialPolicies.Select(item => (int?)item.Version).MaxAsync(cancellationToken) ?? 0) + 1;
            var policyResult = AdCommercialPolicy.Create(
                nextVersion, "KWD", 1m, 1m,
                AdCommercialPolicy.TiePolicyFirstAccepted,
                30,
                AdCommercialPolicy.NextBidderPolicyOfferWithConsent,
                "NoRefund");
            if (policyResult.IsFailure)
            {
                throw new InvalidOperationException("Could not construct the isolated mobile preview policy from valid policy values.");
            }

            policy = policyResult.Value!;
            policy.CreatedAtUtc = now;
            // Keep the preview policy unapproved so it cannot become the effective
            // policy for real auctions or payment flows.
            db.AdCommercialPolicies.Add(policy);
        }

        var fixtureProgram = catalogProgram;
        var firstBundle = catalogBundles[0];
        var lastBundle = catalogBundles[^1];
        var imageKey = placement?.CreativeStorageKey;
        var reuseExistingImage = !string.IsNullOrWhiteSpace(imageKey)
            && await storage.ExistsAsync(imageKey, cancellationToken);
        var publicImageUrl = reuseExistingImage
            ? await storage.GetReadUrlAsync(imageKey!, BannerUrlLifetime, cancellationToken)
            : await storage.GetReadUrlAsync("advertising/mobile-ad-preview/preview.jpg", BannerUrlLifetime, cancellationToken);
        if (!Uri.TryCreate(publicImageUrl, UriKind.Absolute, out var publicImageUri)
            || (publicImageUri.Scheme != Uri.UriSchemeHttp && publicImageUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Preview image storage must resolve to a public HTTP(S) URL; local filesystem paths cannot be used by mobile clients. No database fixtures were written.");
        }

        var adminRoleIds = db.Roles.AsNoTracking()
            .Where(role => role.Name == MealMate.Application.Common.Security.AppRoles.SuperAdmin)
            .Select(role => role.Id);
        var uploaderId = await db.UserRoles.AsNoTracking()
            .Where(userRole => adminRoleIds.Contains(userRole.RoleId))
            .Select(userRole => (Guid?)userRole.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!uploaderId.HasValue)
            throw new InvalidOperationException("An existing SuperAdmin is required to own the isolated preview asset record; no fixtures were written.");

        if (!dryRun && !reuseExistingImage)
        {
            await using var asset = File.OpenRead(assetPath);
            var stored = await storage.SaveAsync(asset, Path.GetFileName(assetPath), "image/jpeg", "advertising/mobile-ad-preview", cancellationToken);
            imageKey = stored.StorageKey;
            publicImageUrl = await storage.GetReadUrlAsync(imageKey, BannerUrlLifetime, cancellationToken);
            if (!Uri.TryCreate(publicImageUrl, UriKind.Absolute, out publicImageUri)
                || (publicImageUri.Scheme != Uri.UriSchemeHttp && publicImageUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("The stored preview asset did not resolve to a public HTTP(S) URL; database fixtures were not written.");

            if (!await db.UploadFileRecords.AnyAsync(item => item.StorageKey == stored.StorageKey, cancellationToken))
            {
                db.UploadFileRecords.Add(new MealMate.Domain.Common.UploadFileRecord
                {
                    StorageKey = stored.StorageKey,
                    FileName = stored.FileName,
                    ContentType = stored.ContentType,
                    SizeBytes = stored.SizeBytes,
                    Scope = "advertising",
                    Folder = "mobile-ad-preview",
                    UploadedByUserId = uploaderId.Value,
                    UploadedAtUtc = now
                });
            }
        }
        else if (reuseExistingImage)
        {
            imageKey = placement!.CreativeStorageKey;
        }

        var campaign = await db.Campaigns.IgnoreQueryFilters()
            .Include(item => item.Programs)
            .Include(item => item.Bundles)
            .Include(item => item.RestaurantEnrollments)
            .SingleOrDefaultAsync(item => item.NameEn == "[MM-MOBILE-AD-PREVIEW] Shared savings", cancellationToken);
        if (campaign is null)
        {
            campaign = new Campaign
            {
                NameAr = "[معاينة MM-MOBILE-AD-PREVIEW] وفّروا معاً",
                NameEn = "[MM-MOBILE-AD-PREVIEW] Shared savings",
                Status = CampaignStatus.Active,
                StartsAtUtc = now.AddMinutes(-1),
                EndsAtUtc = now.AddDays(30),
                TotalDiscountRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(15m),
                MealMateShareRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(10m),
                RestaurantShareRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(5m),
                FundingMode = CampaignFundingMode.Shared,
                MaxSubscribers = 100,
                CurrentSubscribers = 0,
                CurrentSnapshotVersion = 1,
                Programs = [new CampaignProgram { ProgramId = fixtureProgram.Id }],
                Bundles = [new CampaignBundle { BundleId = firstBundle.Id }],
                RestaurantEnrollments =
                [
                    new CampaignRestaurantEnrollment
                    {
                        RestaurantId = restaurant.Id,
                        Status = VerificationStatus.Approved,
                        RequestedAtUtc = now,
                        ApprovedAtUtc = now,
                        CapacityOffered = 100,
                        DedicatedDailyCapacity = 10,
                        ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
                        AcceptedSnapshotVersion = 1,
                        RespondedAtUtc = now
                    }
                ],
                CreatedAtUtc = now
            };
            db.Campaigns.Add(campaign);
        }
        else
        {
            campaign.IsDeleted = false;
            campaign.DeletedAtUtc = null;
            campaign.DeletedByUserId = null;
            campaign.Status = CampaignStatus.Active;
            campaign.StartsAtUtc = now.AddMinutes(-1);
            campaign.EndsAtUtc = now.AddDays(30);
            campaign.TotalDiscountRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(15m);
            campaign.MealMateShareRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(10m);
            campaign.RestaurantShareRate = MealMate.Domain.ValueObjects.Percentage.FromPercent(5m);
            campaign.FundingMode = CampaignFundingMode.Shared;
            campaign.MaxSubscribers = 100;
            if (!campaign.Programs.Any(item => item.ProgramId == fixtureProgram.Id))
                campaign.Programs.Add(new CampaignProgram { CampaignId = campaign.Id, ProgramId = fixtureProgram.Id });
            if (!campaign.Bundles.Any(item => item.BundleId == firstBundle.Id))
                campaign.Bundles.Add(new CampaignBundle { CampaignId = campaign.Id, BundleId = firstBundle.Id });
            var enrollment = campaign.RestaurantEnrollments.SingleOrDefault(item => item.RestaurantId == restaurant.Id);
            if (enrollment is null)
            {
                campaign.RestaurantEnrollments.Add(new CampaignRestaurantEnrollment
                {
                    CampaignId = campaign.Id,
                    RestaurantId = restaurant.Id,
                    Status = VerificationStatus.Approved,
                    RequestedAtUtc = now,
                    ApprovedAtUtc = now,
                    CapacityOffered = 100,
                    DedicatedDailyCapacity = 10,
                    ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
                    AcceptedSnapshotVersion = 1,
                    RespondedAtUtc = now
                });
            }
            else
            {
                enrollment.Status = VerificationStatus.Approved;
                enrollment.ApprovedAtUtc = now;
                enrollment.CapacityOffered = 100;
                enrollment.DedicatedDailyCapacity = 10;
                enrollment.ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted;
                enrollment.AcceptedSnapshotVersion = campaign.CurrentSnapshotVersion;
                enrollment.RespondedAtUtc = now;
            }
        }
        var secondProgram = await db.Programs
            .Where(program => program.IsActive && !program.IsDeleted && program.Id != fixtureProgram.Id)
            .OrderBy(program => program.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);
        var secondBundle = secondProgram is null
            ? firstBundle
            : await db.Bundles.Where(bundle => bundle.ProgramId == secondProgram.Id && bundle.IsActive && !bundle.IsDeleted)
                .OrderBy(bundle => bundle.SortOrder).FirstOrDefaultAsync(cancellationToken) ?? firstBundle;
        var secondBundleProgram = secondBundle.ProgramId == fixtureProgram.Id ? fixtureProgram : secondProgram ?? fixtureProgram;

        var offers = new[]
        {
            BuildOffer("PERCENT-20", "خصم ٢٠٪", "20% off", DiscountType.Percentage,
                MealMate.Domain.ValueObjects.Percentage.FromPercent(20m), null, fixtureProgram, firstBundle, publicImageUrl, now),
            BuildOffer("FIXED-15", "خصم ١٥ د.ك", "KWD 15 off", DiscountType.FixedAmount,
                null, new MealMate.Domain.ValueObjects.Money(15m, "KWD"), secondBundleProgram, secondBundle, publicImageUrl, now),
            BuildOffer("PERCENT-10", "خصم ١٠٪", "10% off", DiscountType.Percentage,
                MealMate.Domain.ValueObjects.Percentage.FromPercent(10m), null, fixtureProgram, lastBundle, publicImageUrl, now),
            BuildOffer("CAMPAIGN-15", "عرض حملة التوفير ١٥٪", "15% shared savings campaign", DiscountType.Percentage,
                MealMate.Domain.ValueObjects.Percentage.FromPercent(15m), null, fixtureProgram, firstBundle, publicImageUrl, now, campaign.Id)
        };

        foreach (var offer in offers)
        {
            var existing = await db.Offers.IgnoreQueryFilters()
                .Include(item => item.EligiblePrograms)
                .Include(item => item.EligibleBundles)
                .SingleOrDefaultAsync(item => item.PromoCode == offer.PromoCode, cancellationToken);
            if (existing is null)
            {
                db.Offers.Add(offer);
                continue;
            }

            existing.IsDeleted = false;
            existing.DeletedAtUtc = null;
            existing.DeletedByUserId = null;
            existing.NameAr = offer.NameAr;
            existing.NameEn = offer.NameEn;
            existing.DescriptionAr = offer.DescriptionAr;
            existing.DescriptionEn = offer.DescriptionEn;
            existing.BannerImageUrl = offer.BannerImageUrl;
            existing.Status = offer.Status;
            existing.Type = offer.Type;
            existing.DiscountType = offer.DiscountType;
            existing.FundingMode = offer.FundingMode;
            existing.DiscountRate = offer.DiscountRate;
            existing.DiscountAmount = offer.DiscountAmount;
            existing.StartsAtUtc = offer.StartsAtUtc;
            existing.EndsAtUtc = offer.EndsAtUtc;
            existing.TotalUsageLimit = offer.TotalUsageLimit;
            existing.PerCustomerLimit = offer.PerCustomerLimit;
            existing.CustomerSegment = offer.CustomerSegment;
            existing.CampaignId = offer.CampaignId;
            existing.TermsAndConditionsAr = offer.TermsAndConditionsAr;
            existing.TermsAndConditionsEn = offer.TermsAndConditionsEn;
            var desiredProgramId = offer.EligiblePrograms.Single().ProgramId;
            var currentProgram = existing.EligiblePrograms.SingleOrDefault(item => item.ProgramId == desiredProgramId);
            foreach (var obsoleteProgram in existing.EligiblePrograms.Where(item => item.ProgramId != desiredProgramId).ToList())
            {
                db.OfferPrograms.Remove(obsoleteProgram);
                existing.EligiblePrograms.Remove(obsoleteProgram);
            }
            if (currentProgram is null)
                existing.EligiblePrograms.Add(new OfferProgram { OfferId = existing.Id, ProgramId = desiredProgramId });

            var desiredBundleId = offer.EligibleBundles.Single().BundleId;
            var currentBundle = existing.EligibleBundles.SingleOrDefault(item => item.BundleId == desiredBundleId);
            foreach (var obsoleteBundle in existing.EligibleBundles.Where(item => item.BundleId != desiredBundleId).ToList())
            {
                db.OfferBundles.Remove(obsoleteBundle);
                existing.EligibleBundles.Remove(obsoleteBundle);
            }
            if (currentBundle is null)
                existing.EligibleBundles.Add(new OfferBundle { OfferId = existing.Id, BundleId = desiredBundleId });
        }

        var auction = placement?.AdAuction;
        if (auction is null)
        {
            auction = new AdAuction
            {
                AdSpaceId = adSpace.Id,
                AdSpace = adSpace,
                Status = AdAuctionStatus.Awarded,
                BidsOpenAtUtc = now.AddDays(-2),
                BidsCloseAtUtc = now.AddDays(-1),
                DisplayStartsAtUtc = now.AddMinutes(-1),
                DisplayEndsAtUtc = now.AddDays(30),
                PolicyId = policy.Id,
                PolicyVersion = policy.Version,
                // Awarded is set only so the existing customer read query can render a
                // development preview. No bid, payment attempt, charge, or ledger entry exists.
                CreatedAtUtc = now
            };
            db.AdAuctions.Add(auction);
        }
        else
        {
            auction.DisplayStartsAtUtc = now.AddMinutes(-1);
            auction.DisplayEndsAtUtc = now.AddDays(30);
        }

        if (placement is null)
        {
            placement = new AdPlacement
            {
                AdAuction = auction,
                AdAuctionId = auction.Id,
                RestaurantId = restaurant.Id,
                AdSpaceId = adSpace.Id,
                Status = AdPlacementStatus.Active,
                CreativeStorageKey = imageKey,
                TargetType = "restaurant",
                TargetId = restaurant.Id,
                DisplayStartsAtUtc = now.AddMinutes(-1),
                DisplayEndsAtUtc = now.AddDays(30),
                ReviewNotes = PreviewPlacementMarker,
                CreatedAtUtc = now
            };
            db.AdPlacements.Add(placement);
        }
        else
        {
            placement.IsDeleted = false;
            placement.DeletedAtUtc = null;
            placement.DeletedByUserId = null;
            placement.RestaurantId = restaurant.Id;
            placement.Status = AdPlacementStatus.Active;
            placement.CreativeStorageKey = imageKey;
            placement.TargetType = "restaurant";
            placement.TargetId = restaurant.Id;
            placement.DisplayStartsAtUtc = now.AddMinutes(-1);
            placement.DisplayEndsAtUtc = now.AddDays(30);
        }

        var existingOfferCount = await db.Offers.IgnoreQueryFilters()
            .CountAsync(item => item.PromoCode != null && item.PromoCode.StartsWith(PreviewCodePrefix), cancellationToken);
        var existingCampaignCount = await db.Campaigns.IgnoreQueryFilters()
            .CountAsync(item => item.NameEn == "[MM-MOBILE-AD-PREVIEW] Shared savings", cancellationToken);
        var existingPlacementCount = await db.AdPlacements.IgnoreQueryFilters()
            .CountAsync(item => item.ReviewNotes == PreviewPlacementMarker, cancellationToken);
        if (dryRun)
        {
            Console.WriteLine("Mobile advertising preview dry-run: no rows were inserted or updated.");
            Console.WriteLine($"Prerequisites: active catalog {fixtureProgram.Id}/{firstBundle.Id}; verified JPEG; public storage URL configured; restaurant {restaurant.Id}; ad space {adSpace.Id}; policy v{policy.Version} (approved: {policy.IsApproved}); selected customer RegionId {regionId?.ToString() ?? "not selected"}.");
            Console.WriteLine($"Would create or refresh {offers.Length} tagged offer fixtures and one linked campaign; existing tagged offers: {existingOfferCount}; campaign: {existingCampaignCount}; placements: {existingPlacementCount}.");
            Console.WriteLine($"Would create or refresh one tagged sponsored {placementKey} preview using {(reuseExistingImage ? "the existing preview asset" : $"the supplied asset {Path.GetFileName(assetPath)}")}.");
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"Mobile advertising preview offers seeded or refreshed: {string.Join(", ", offers.Select(item => item.PromoCode))}");
        Console.WriteLine($"Sponsored {placementKey} preview refreshed for existing restaurant {restaurant.Id}; asset is served from the verified storage key.");
        Console.WriteLine("Preview state does not prove payment. No bid, payment attempt, provider transaction, charge, or financial ledger entry was created.");
        if (customerId.HasValue)
        {
            Console.WriteLine($"Customer address scope verified for {customerId.Value} in region {regionId}.");
        }
        Console.WriteLine("Offer BannerImageUrl uses a 30-day storage read URL and is refreshed when this seeder is rerun.");
    }

    private static Offer BuildOffer(
        string code,
        string nameAr,
        string nameEn,
        DiscountType discountType,
        MealMate.Domain.ValueObjects.Percentage? rate,
        MealMate.Domain.ValueObjects.Money? amount,
        Program program,
        Bundle bundle,
        string imageUrl,
        DateTime now,
        Guid? campaignId = null)
        => new()
        {
            NameAr = $"[معاينة] {nameAr}",
            NameEn = $"[PREVIEW] {nameEn}",
            DescriptionAr = "بيانات معاينة معزولة لعرضها في تطبيق الجوال.",
            DescriptionEn = "Isolated preview data for mobile rendering.",
            BannerImageUrl = imageUrl,
            Status = OfferStatus.Active,
            Type = OfferType.StandardDiscount,
            DiscountType = discountType,
            FundingMode = OfferFundingMode.PlatformFull,
            DiscountRate = rate,
            DiscountAmount = amount,
            StartsAtUtc = now.AddMinutes(-1),
            EndsAtUtc = now.AddDays(30),
            TotalUsageLimit = 100,
            CurrentUsageCount = 0,
            PerCustomerLimit = 1,
            CustomerSegment = CustomerSegmentFilter.All,
            CampaignId = campaignId,
            PromoCode = PreviewCodePrefix + code,
            TermsAndConditionsAr = "معاينة تطويرية فقط؛ لا تستخدم كعرض تجاري.",
            TermsAndConditionsEn = "Development preview only; not a commercial offer.",
            CreatedAtUtc = now,
            EligiblePrograms = [new OfferProgram { ProgramId = program.Id }],
            EligibleBundles = [new OfferBundle { BundleId = bundle.Id }]
        };

    private static bool MarketIncludesRegion(string? marketIdsJson, Guid? regionId)
    {
        try
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(marketIdsJson ?? "[]");
            return ids is { Length: 0 } || (regionId.HasValue && ids?.Contains(regionId.Value) == true);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
