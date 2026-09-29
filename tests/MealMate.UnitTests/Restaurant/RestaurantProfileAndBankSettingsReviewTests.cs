using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantProfileAndBankSettingsReviewTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly RestaurantSettingsHandlers _handler;

    private readonly Guid _ownerUserId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _registrationId = Guid.NewGuid();
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly Guid _countryId = Guid.NewGuid();
    private readonly Guid _regionId = Guid.NewGuid();

    public RestaurantProfileAndBankSettingsReviewTests()
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
        _tenantResolver = new KuwaitTenantResolver(_db, _currentUser);

        _handler = new RestaurantSettingsHandlers(
            _db,
            _currentUser,
            _audit,
            _clock,
            _tenantResolver,
            _notifier);

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

        var region = new Region
        {
            CountryId = _countryId,
            Code = "KWT-CAP",
            NameAr = "العاصمة",
            NameEn = "Capital",
            IsActive = true,
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(region, _regionId);
        _db.Regions.Add(region);

        var tenantId = Guid.NewGuid();
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
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(tenant, tenantId);
        _db.Tenants.Add(tenant);
        _currentUser.TenantIds.Returns(new[] { tenantId });

        var reg = new RestaurantRegistration
        {
            SubmittedByUserId = _ownerUserId,
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل للتجارة العامة",
            LegalNameEn = "Aseel General Trading Co",
            LegalEntityType = "llc",
            ContactPhone = "+96511111111",
            ContactEmail = "contact@aseel.kw",
            CommercialRegistrationNumber = "CR-123456",
            CommercialRegistrationExpiry = new DateOnly(2028, 1, 1),
            CommercialLicenseNumber = "CL-987654",
            CommercialLicenseExpiry = new DateOnly(2028, 1, 1),
            FoodLicenseNumber = "FL-555555",
            FoodLicenseExpiry = new DateOnly(2028, 1, 1),
            Address = "Kuwait City, Block 1",
            Latitude = 29.3759m,
            Longitude = 47.9774m,
            CountryId = _countryId,
            RegionId = _regionId,
            CompanyCivilId = "123456789012",
            OwnerName = "أحمد الصالح",
            AuthorizedSignatoryCivilId = "123456789012",
            AuthorizedSignatoryTitle = "المدير العام",
            PrimaryContactName = "أحمد الصالح",
            PrimaryContactTitle = "المدير العام",
            PrimaryContactPhone = "+96511111111",
            PrimaryContactEmail = "contact@aseel.kw",
            BankName = "National Bank of Kuwait",
            BankBeneficiaryName = "Aseel General Trading",
            Iban = "KW81CBKU0000000000001234567890",
            BankAccountNumber = "1234567890",
            SwiftCode = "CBKUKWKW",
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = _restaurantId,
            ApprovedVersion = 1,
            DeliveryByRestaurantConfirmed = true,
            DataResponsibilityConfirmed = true,
            CreatedAtUtc = _clock.UtcNow.AddMonths(-2),
            ModifiedAtUtc = _clock.UtcNow.AddMonths(-2),
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(reg, _registrationId);
        _db.RestaurantRegistrations.Add(reg);

        var profile = new RestaurantProfile
        {
            TradeNameAr = "مطعم أصيل",
            TradeNameEn = "Aseel Restaurant",
            LegalNameAr = "شركة أصيل للتجارة العامة",
            LegalNameEn = "Aseel General Trading Co",
            CommercialLicenseNumber = "CL-987654",
            CountryId = _countryId,
            PrimaryRegionId = _regionId,
            Latitude = 29.3759m,
            Longitude = 47.9774m,
            ContactPhone = "+96511111111",
            ContactEmail = "contact@aseel.kw",
            ApprovedVersion = 1,
            CreatedAtUtc = _clock.UtcNow.AddMonths(-2),
            ModifiedAtUtc = _clock.UtcNow.AddMonths(-2),
        };
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(profile, _restaurantId);
        _db.RestaurantProfiles.Add(profile);

        _db.SaveChanges();
    }

    private RestaurantSettingsDraftPayload CreateValidPayload(
        string? phone = "+96599999999",
        string? iban = "KW74CBKU0000000000001234567890",
        decimal? lat = 29.3759m,
        decimal? lng = 47.9774m) => new(
        LegalNameAr: "شركة أصيل للتجارة العامة",
        LegalNameEn: "Aseel General Trading Co",
        TradeNameAr: "مطعم أصيل الجديد",
        TradeNameEn: "Aseel New Restaurant",
        ContactPhone: phone,
        ContactEmail: "contact@aseel.kw",
        CommercialRegistrationNumber: "CR-123456",
        CommercialRegistrationExpiry: new DateOnly(2028, 1, 1),
        CommercialLicenseNumber: "CL-987654",
        CommercialLicenseExpiry: new DateOnly(2028, 1, 1),
        FoodLicenseNumber: "FL-555555",
        FoodLicenseExpiry: new DateOnly(2028, 1, 1),
        LegalEntityType: "llc",
        CompanyCivilId: "123456789012",
        OwnerName: "أحمد الصالح",
        AuthorizedSignatoryCivilId: "123456789012",
        AuthorizedSignatoryTitle: "المدير العام",
        PrimaryContactName: "أحمد الصالح",
        PrimaryContactTitle: "المدير العام",
        PrimaryContactPhone: phone,
        PrimaryContactEmail: "contact@aseel.kw",
        BankName: "National Bank of Kuwait",
        BankBeneficiaryName: "Aseel General Trading",
        Iban: iban,
        BankAccountNumber: "9876543210",
        SwiftCode: "NBOKKWKW",
        Address: "Kuwait City, Block 2",
        CountryId: _countryId,
        RegionId: _regionId,
        ServiceRegionIds: new List<string> { _regionId.ToString() },
        DeliveryByRestaurantConfirmed: true,
        DataResponsibilityConfirmed: true,
        Latitude: lat,
        Longitude: lng);

    [Fact]
    public async Task SaveAndSubmitDraft_LeavesLiveEntitiesUnchanged_UntilAdminApprove()
    {
        // 1. Authenticate as restaurant owner
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);

        var payload = CreateValidPayload(phone: "+96599999999", iban: "KW74CBKU0000000000001234567890");

        // Save draft
        var saveResult = await _handler.Handle(new SaveRestaurantSettingsDraftCommand(payload), CancellationToken.None);
        saveResult.IsSuccess.Should().BeTrue();

        // Submit draft
        var submitResult = await _handler.Handle(new SubmitRestaurantSettingsDraftCommand(), CancellationToken.None);
        submitResult.IsSuccess.Should().BeTrue();

        // Verify live entities in database are completely UNCHANGED
        var reg = await _db.RestaurantRegistrations.FindAsync(_registrationId);
        var prof = await _db.RestaurantProfiles.FindAsync(_restaurantId);

        reg!.ContactPhone.Should().Be("+96511111111");
        reg.Iban.Should().Be("KW81CBKU0000000000001234567890");
        reg.ApprovedVersion.Should().Be(1);

        prof!.ContactPhone.Should().Be("+96511111111");
        prof.ApprovedVersion.Should().Be(1);

        // Verify change request is Submitted
        var change = await _db.RestaurantSettingsChangeRequests.FirstOrDefaultAsync(x => x.RestaurantId == _restaurantId);
        change.Should().NotBeNull();
        change!.Status.Should().Be(VerificationStatus.Submitted);
        change.BaseVersion.Should().Be(1);

        // 2. Admin approves
        _currentUser.UserId.Returns(_adminUserId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);

        var approveResult = await _handler.Handle(
            new AdminApproveRestaurantSettingsChangeCommand(_restaurantId, "Approved by admin"),
            CancellationToken.None);
        approveResult.IsSuccess.Should().BeTrue();

        // 3. Verify live entities are updated and version is incremented
        var updatedReg = await _db.RestaurantRegistrations.FindAsync(_registrationId);
        var updatedProf = await _db.RestaurantProfiles.FindAsync(_restaurantId);

        updatedReg!.ContactPhone.Should().Be("+96599999999");
        updatedReg.Iban.Should().Be("KW74CBKU0000000000001234567890");
        updatedReg.ApprovedVersion.Should().Be(2);

        updatedProf!.ContactPhone.Should().Be("+96599999999");
        updatedProf.ApprovedVersion.Should().Be(2);
    }

    [Fact]
    public async Task SubmitDraft_Fails_WhenCoordinatesMissing()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);

        var payload = CreateValidPayload(lat: null, lng: null);
        await _handler.Handle(new SaveRestaurantSettingsDraftCommand(payload), CancellationToken.None);

        var submitResult = await _handler.Handle(new SubmitRestaurantSettingsDraftCommand(), CancellationToken.None);
        submitResult.IsFailure.Should().BeTrue();
        submitResult.Error.Code.Should().Be(MessageCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task SubmitDraft_Fails_WhenCoordinatesOutOfKuwait()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);

        var payload = CreateValidPayload(lat: 10.5m, lng: 10.5m);
        await _handler.Handle(new SaveRestaurantSettingsDraftCommand(payload), CancellationToken.None);

        var submitResult = await _handler.Handle(new SubmitRestaurantSettingsDraftCommand(), CancellationToken.None);
        submitResult.IsFailure.Should().BeTrue();
        submitResult.Error.Code.Should().Be(MessageCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task SubmitDraft_Fails_WhenIbanInvalid()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);

        var payload = CreateValidPayload(iban: "KW00INVALIDIBAN999");
        await _handler.Handle(new SaveRestaurantSettingsDraftCommand(payload), CancellationToken.None);

        var submitResult = await _handler.Handle(new SubmitRestaurantSettingsDraftCommand(), CancellationToken.None);
        submitResult.IsFailure.Should().BeTrue();
        submitResult.Error.Code.Should().Be(MessageCodes.Validation.InvalidFormat);
    }

    [Fact]
    public async Task GetSettings_MasksBankDetails_ForGeneralRestaurantView()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_ownerUserId);
        _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var result = await _handler.Handle(new GetRestaurantSettingsQuery(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // Bank details must be masked (****1234 format)
        result.Value.Live.Iban.Should().StartWith("****");
        result.Value.Live.Iban.Should().EndWith("7890");
        result.Value.Live.BankAccountNumber.Should().StartWith("****");
        result.Value.Live.BankAccountNumber.Should().EndWith("7890");
    }
}
