using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantLocationCoverageReviewTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantLocationHandlers _locationHandler;
    private readonly RestaurantServiceRegionService _serviceRegionService;
    private readonly RestaurantChangeReviewService _changeReviewService;

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();
    private readonly Guid _countryId = Guid.NewGuid();
    private readonly Guid _regionAId = Guid.NewGuid();
    private readonly Guid _regionBId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    public RestaurantLocationCoverageReviewTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _currentUser = Substitute.For<ICurrentUser>();
        _audit = Substitute.For<IAuditLogger>();
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
        _notifier = Substitute.For<IRegistrationReviewNotifier>();

        var tenantResolver = new KuwaitTenantResolver(_db, _currentUser);

        _locationHandler = new RestaurantLocationHandlers(_db, _currentUser, _clock);
        _serviceRegionService = new RestaurantServiceRegionService(_db, _currentUser, _clock, tenantResolver);
        _changeReviewService = new RestaurantChangeReviewService(_db, _currentUser, _audit, _clock, _notifier);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        var country = new Country
        {
            Code = "KW",
            NameAr = "الكويت",
            NameEn = "Kuwait",
            DialCode = "+965",
            CurrencyCode = "KWD",
            FlagEmoji = "🇰🇼",
            IsActive = true,
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(country, _countryId);
        _db.Countries.Add(country);

        var regionA = new Region
        {
            CountryId = _countryId,
            Code = "KW-CAP-01",
            NameAr = "شرق",
            NameEn = "Sharq",
            Type = RegionType.Area,
            IsActive = true,
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(regionA, _regionAId);
        _db.Regions.Add(regionA);

        var regionB = new Region
        {
            CountryId = _countryId,
            Code = "KW-CAP-02",
            NameAr = "دسمان",
            NameEn = "Dasman",
            Type = RegionType.Area,
            IsActive = true,
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(regionB, _regionBId);
        _db.Regions.Add(regionB);

        var tenant = new Tenant
        {
            Code = "TEN-KW",
            NameAr = "وجبة بلس الكويت",
            NameEn = "MealMate Kuwait",
            CountryId = _countryId,
            CurrencyCode = "KWD",
            Domain = "kw.mealmate.local",
            TimeZoneId = "Asia/Kuwait",
            Status = TenantStatus.Active,
            IsActive = true,
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(tenant, _tenantId);
        _db.Tenants.Add(tenant);

        var reg = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            TradeNameAr = "مطعم السور",
            TradeNameEn = "Al Soor Restaurant",
            LegalNameAr = "شركة السور للأغذية",
            LegalNameEn = "Al Soor Food Co",
            LegalEntityType = "llc",
            ContactPhone = "+96522222222",
            ContactEmail = "info@alsoor.kw",
            Address = "Kuwait City, Sharq",
            Latitude = 29.3800m,
            Longitude = 47.9900m,
            CountryId = _countryId,
            RegionId = _regionAId,
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = _restaurantId,
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddMonths(-1),
            ModifiedAtUtc = _clock.UtcNow.AddMonths(-1),
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(reg, _registrationId);
        _db.RestaurantRegistrations.Add(reg);

        var profile = new RestaurantProfile
        {
            TradeNameAr = "مطعم السور",
            TradeNameEn = "Al Soor Restaurant",
            LegalNameAr = "شركة السور للأغذية",
            LegalNameEn = "Al Soor Food Co",
            CountryId = _countryId,
            PrimaryRegionId = _regionAId,
            Latitude = 29.3800m,
            Longitude = 47.9900m,
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddMonths(-1),
            ModifiedAtUtc = _clock.UtcNow.AddMonths(-1),
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(profile, _restaurantId);
        _db.RestaurantProfiles.Add(profile);

        var activeRegionA = new RestaurantServiceRegion
        {
            RestaurantId = _restaurantId,
            RegionId = _regionAId,
            IsActive = true,
            DeliveryWindowStart = new TimeOnly(8, 0),
            DeliveryWindowEnd = new TimeOnly(14, 0),
            CreatedAtUtc = _clock.UtcNow.AddMonths(-1),
        };
        _db.RestaurantServiceRegions.Add(activeRegionA);

        _db.SaveChanges();
    }

    [Fact]
    public async Task DirectRestaurantLocationPut_CannotChangeApprovedProfileOrRegistration()
    {
        // Authenticate as restaurant owner without admin approval rights
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.TenantIds.Returns(new[] { _tenantId });
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        // Call direct location update command
        var result = await _locationHandler.Handle(
            new UpdateRestaurantLocationCommand(29.3500m, 47.9500m, "New Address In Salmiya"),
            CancellationToken.None);

        // Verify live profile in DB remained untouched at 29.3800, not 29.3500
        var profile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        var registration = await _db.RestaurantRegistrations.FindAsync(_registrationId);

        profile!.Latitude.Should().Be(29.3800m);
        profile.Longitude.Should().Be(47.9900m);
        registration!.Latitude.Should().Be(29.3800m);
    }

    [Fact]
    public async Task DirectRestaurantServiceRegionPut_CannotChangeActiveCoverageForApprovedRestaurant()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.TenantIds.Returns(new[] { _tenantId });
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        // Attempt direct selection of Region B
        await _serviceRegionService.UpdateServiceRegionSelectionAsync(
            new UpdateRestaurantServiceRegionSelectionCommand(new List<Guid> { _regionAId, _regionBId }),
            CancellationToken.None);

        // Invariant: direct PUT cannot change active service regions for approved restaurant
        var regionB = await _db.RestaurantServiceRegions
            .FirstOrDefaultAsync(x => x.RestaurantId == _restaurantId && x.RegionId == _regionBId);

        (regionB == null || !regionB.IsActive).Should().BeTrue("direct PUT must not activate region B without admin review");
    }

    [Fact]
    public async Task ApprovedCoverageProposal_UpdatesProfileRegistrationAndRegionsTogether_Atomically()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.RestaurantId.Returns(_restaurantId);
        _currentUser.TenantIds.Returns(new[] { _tenantId });

        var payload = new RestaurantCoverageProposalPayload(
            Address: "Kuwait City, Dasman Block 3",
            Latitude: 29.3900m,
            Longitude: 47.9950m,
            PrimaryRegionId: _regionBId,
            ServiceRegionIds: new List<Guid> { _regionAId, _regionBId },
            Windows: new List<RestaurantServiceRegionWindowPayload>
            {
                new(_regionAId, true, new TimeOnly(9, 0), new TimeOnly(15, 0)),
                new(_regionBId, true, new TimeOnly(9, 0), new TimeOnly(15, 0)),
            });

        // 1. Submit coverage proposal
        var submitResult = await _changeReviewService.SubmitChangeAsync(
            new SubmitRestaurantChangeCommand(
                _restaurantId,
                RestaurantChangeCategory.Coverage,
                JsonSerializer.Serialize(payload)),
            CancellationToken.None);

        submitResult.IsSuccess.Should().BeTrue();
        var changeId = submitResult.Value.Id;

        // Verify live values are STILL unchanged before approval
        var preProfile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        preProfile!.Latitude.Should().Be(29.3800m);
        preProfile.ApprovedVersion.Should().Be(1);

        // 2. Admin approves proposal
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var decisionResult = await _changeReviewService.DecideChangeAsync(
            new DecideRestaurantChangeCommand(changeId, "Approve", "Approved coverage changes", ExpectedVersion: 1),
            CancellationToken.None);

        decisionResult.IsSuccess.Should().BeTrue();

        // 3. Verify profile, registration, and service regions updated together atomically
        var postProfile = await _db.RestaurantProfiles.FindAsync(_restaurantId);
        var postReg = await _db.RestaurantRegistrations.FindAsync(_registrationId);
        var postRegionB = await _db.RestaurantServiceRegions
            .FirstOrDefaultAsync(x => x.RestaurantId == _restaurantId && x.RegionId == _regionBId);

        postProfile!.Latitude.Should().Be(29.3900m);
        postProfile.Longitude.Should().Be(47.9950m);
        postProfile.PrimaryRegionId.Should().Be(_regionBId);
        postProfile.ApprovedVersion.Should().Be(2);

        postReg!.Address.Should().Be("Kuwait City, Dasman Block 3");
        postReg.ApprovedVersion.Should().Be(2);

        postRegionB.Should().NotBeNull();
        postRegionB!.IsActive.Should().BeTrue();
        postRegionB.DeliveryWindowStart.Should().Be(new TimeOnly(9, 0));
    }
}
