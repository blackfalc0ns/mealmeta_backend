using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public sealed class RestaurantDeliveryWindowTests
{
    private readonly Persistence.AppDbContext db;
    private readonly ICurrentUser user = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
    private readonly RestaurantProfile profile;
    private readonly RestaurantChangeReviewService review;
    private readonly RestaurantDeliveryWindowService service;
    private readonly KuwaitTenantResolver resolver;
    public RestaurantDeliveryWindowTests()
    {
        db = new(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        clock.UtcNow.Returns(new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc));
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", CurrencyCode = "KWD", DialCode = "+965", FlagEmoji = "KW", IsActive = true };
        var tenant = new Tenant { Code = "TEN-KW", NameAr = "الكويت", NameEn = "Kuwait", Country = country, CurrencyCode = "KWD", Domain = "localhost", TimeZoneId = "Asia/Kuwait", IsActive = true, Status = TenantStatus.Active };
        profile = new RestaurantProfile { UserId = Guid.NewGuid(), CountryId = country.Id, TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved };
        db.Countries.Add(country); db.Tenants.Add(tenant); db.RestaurantProfiles.Add(profile);
        db.RestaurantRegistrations.Add(new RestaurantRegistration { SubmittedByUserId = profile.UserId, ResultingRestaurantId = profile.Id, TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant", CountryId = country.Id, Status = VerificationStatus.Approved, ApprovedVersion = 1 });
        db.SaveChanges();
        user.IsAuthenticated.Returns(true); user.UserId.Returns(profile.UserId); user.UserType.Returns(UserType.Restaurant); user.RestaurantId.Returns(profile.Id); user.TenantIds.Returns(new[] { tenant.Id }); user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        resolver = new(db, user);
        review = new(db, user, Substitute.For<IAuditLogger>(), clock);
        service = new(db, user, clock, resolver, review);
    }
    private RestaurantServiceRegion AddRegion(bool active, int start = 9, int end = 17)
    {
        var region = new Region { CountryId = profile.CountryId!.Value, Code = Guid.NewGuid().ToString(), NameAr = "منطقة", NameEn = "Area", IsActive = true, Type = RegionType.Area };
        db.Regions.Add(region);
        var config = new RestaurantServiceRegion { RestaurantId = profile.Id, RegionId = region.Id, IsActive = active, DeliveryWindowStart = new(start, 0), DeliveryWindowEnd = new(end, 0) };
        db.RestaurantServiceRegions.Add(config); db.SaveChanges(); return config;
    }
    [Fact]
    public async Task GlobalSave_PersistsWithoutAnyRegions()
    {
        var result = await service.SaveAsync(new(new(10, 0), new(18, 0)), default);
        Assert.True(result.IsSuccess); Assert.False(result.Value.RequiresReview);
        Assert.Equal(new TimeOnly(10, 0), profile.DeliveryWindowStart);
        var read = await service.GetAsync(null, default);
        Assert.Equal(new TimeOnly(18, 0), read.Value.DeliveryWindowEnd);
    }
    [Fact]
    public async Task GlobalSave_UpdatesActiveAndInactiveProjectionsOnlyForThisRestaurant()
    {
        var a = AddRegion(true); var b = AddRegion(false);
        var other = new RestaurantServiceRegion { RestaurantId = Guid.NewGuid(), RegionId = a.RegionId, DeliveryWindowStart = new(6, 0), DeliveryWindowEnd = new(7, 0) };
        db.RestaurantServiceRegions.Add(other); await db.SaveChangesAsync();
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0)), default)).IsSuccess);
        Assert.Equal(new TimeOnly(10, 0), a.DeliveryWindowStart); Assert.Equal(new TimeOnly(18, 0), b.DeliveryWindowEnd); Assert.Equal(new TimeOnly(6, 0), other.DeliveryWindowStart);
    }
    [Fact]
    public async Task GlobalPreparation_UpdatesAllRegionsAndIsInheritedByNewSelection()
    {
        var active = AddRegion(true); var inactive = AddRegion(false);
        var other = new RestaurantServiceRegion { RestaurantId = Guid.NewGuid(), RegionId = active.RegionId, PreparationLeadTimeMinutes = 15 };
        db.RestaurantServiceRegions.Add(other); await db.SaveChangesAsync();
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0), PreparationLeadTimeMinutes: 45), default)).IsSuccess);
        Assert.Equal(45, profile.PreparationLeadTimeMinutes); Assert.Equal(45, active.PreparationLeadTimeMinutes); Assert.Equal(45, inactive.PreparationLeadTimeMinutes); Assert.Equal(15, other.PreparationLeadTimeMinutes);
        var read = await service.GetAsync(null, default); Assert.Equal(45, read.Value.PreparationLeadTimeMinutes);
        db.RestaurantServiceRegions.Remove(inactive); await db.SaveChangesAsync();
        var regionsService = new RestaurantServiceRegionService(db, user, clock, resolver);
        Assert.True((await regionsService.UpdateServiceRegionSelectionAsync(new(new() { active.RegionId, inactive.RegionId }), default)).IsSuccess);
        var added = await db.RestaurantServiceRegions.SingleAsync(x => x.RestaurantId == profile.Id && x.RegionId == inactive.RegionId);
        Assert.Equal(45, added.PreparationLeadTimeMinutes);
        Assert.True((await regionsService.UpdateServiceRegionAsync(new(active.RegionId, true, new(10, 0), new(18, 0), 20), default)).IsFailure);
        Assert.Equal(45, active.PreparationLeadTimeMinutes);
    }
    [Fact]
    public async Task PreparationProposal_OnlyAppliesOnApproval()
    {
        var a = AddRegion(true); var b = AddRegion(false);
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0), PreparationLeadTimeMinutes: 60), default)).IsSuccess);
        Assert.Null(profile.PreparationLeadTimeMinutes); Assert.Null(a.PreparationLeadTimeMinutes);
        var request = await db.RestaurantSettingsChangeRequests.SingleAsync();
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        Assert.True((await review.DecideChangeAsync(new(request.Id, "Approved", null, request.BaseVersion), default)).IsSuccess);
        Assert.Equal(60, profile.PreparationLeadTimeMinutes); Assert.Equal(60, a.PreparationLeadTimeMinutes); Assert.Equal(60, b.PreparationLeadTimeMinutes);
    }
    [Fact]
    public async Task ConflictingPreparation_IsNotArbitrarilySelected_AndNegativeIsRejected()
    {
        var a = AddRegion(true); var b = AddRegion(true);
        a.PreparationLeadTimeMinutes = 20; b.PreparationLeadTimeMinutes = 40; await db.SaveChangesAsync();
        var read = await service.GetAsync(null, default);
        Assert.True(read.Value.HasConflictingPreparationTimes); Assert.Null(read.Value.PreparationLeadTimeMinutes);
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0), PreparationLeadTimeMinutes: -1), default)).IsFailure);
        Assert.Equal(20, a.PreparationLeadTimeMinutes); Assert.Empty(db.RestaurantSettingsChangeRequests);
    }
    [Fact]
    public async Task UnspecifiedPreparation_ClearsAllRegionsWithoutInventingDuration()
    {
        var a = AddRegion(true); a.PreparationLeadTimeMinutes = 20; await db.SaveChangesAsync();
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0)), default)).IsSuccess);
        Assert.Null(a.PreparationLeadTimeMinutes); Assert.Null((await service.GetAsync(null, default)).Value.PreparationLeadTimeMinutes);
    }
    [Fact]
    public async Task LegacyCoverageReview_CannotOverrideGlobalPreparation()
    {
        var area = AddRegion(true);
        await service.SaveAsync(new(new(10, 0), new(18, 0), PreparationLeadTimeMinutes: 45), default);
        var payload = new RestaurantCoverageProposalPayload(null, 29.3m, 47.9m, area.RegionId, new() { area.RegionId }, new() { new(area.RegionId, true, new(10, 0), new(18, 0), 20) });
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var submitted = await review.SubmitChangeAsync(new(profile.Id, RestaurantChangeCategory.Coverage, JsonSerializer.Serialize(payload), JsonSerializer.Serialize(new[] { "PreparationLeadTimeMinutes" })), default);
        Assert.True(submitted.IsSuccess, submitted.IsFailure ? submitted.Error.ToString() : null);
        var request = await db.RestaurantSettingsChangeRequests.SingleAsync();
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        Assert.True((await review.DecideChangeAsync(new(request.Id, "Approved", null, request.BaseVersion), default)).IsFailure);
        Assert.Equal(45, area.PreparationLeadTimeMinutes); Assert.Equal(45, profile.PreparationLeadTimeMinutes);
    }
    [Fact]
    public async Task ConflictingLegacyWindows_AreNotArbitrarilySelected()
    {
        AddRegion(true); AddRegion(true, 10, 18);
        var read = await service.GetAsync(null, default);
        Assert.True(read.Value.HasConflictingWindows); Assert.Null(read.Value.DeliveryWindowStart); Assert.Null(read.Value.DeliveryWindowEnd);
    }
    [Fact]
    public async Task InvalidWindow_IsRejectedWithoutMutation()
    {
        var result = await service.SaveAsync(new(new(18, 0), new(10, 0)), default);
        Assert.True(result.IsFailure); Assert.Null(profile.DeliveryWindowStart); Assert.Empty(db.RestaurantSettingsChangeRequests);
    }
    [Fact]
    public async Task OtherRestaurantScope_IsRejected()
    {
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var result = await service.SaveAsync(new(new(10, 0), new(18, 0), Guid.NewGuid()), default);
        Assert.True(result.IsFailure); Assert.Null(profile.DeliveryWindowStart);
    }
    [Fact]
    public async Task OwnerSave_QueuesReviewWithoutChangingCoverageOrLocation_ApprovalAppliesGlobally()
    {
        var a = AddRegion(true); var b = AddRegion(false);
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var result = await service.SaveAsync(new(new(10, 0), new(18, 0)), default);
        Assert.True(result.IsSuccess); Assert.True(result.Value.RequiresReview); Assert.Null(profile.DeliveryWindowStart); Assert.Equal(new TimeOnly(9, 0), a.DeliveryWindowStart);
        var request = await db.RestaurantSettingsChangeRequests.SingleAsync();
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        var decision = await review.DecideChangeAsync(new(request.Id, "Approved", null, request.BaseVersion), default);
        Assert.True(decision.IsSuccess); Assert.Equal(new TimeOnly(10, 0), profile.DeliveryWindowStart); Assert.Equal(new TimeOnly(18, 0), b.DeliveryWindowEnd); Assert.True(a.IsActive); Assert.False(b.IsActive); Assert.Null(profile.Latitude);
    }
    [Fact]
    public async Task NewSelectedRegion_InheritsGlobalWindow_AndLegacyOverrideIsRejected()
    {
        await service.SaveAsync(new(new(10, 0), new(18, 0)), default);
        var a = AddRegion(false);
        db.RestaurantServiceRegions.Remove(a); await db.SaveChangesAsync();
        var regionsService = new RestaurantServiceRegionService(db, user, clock, resolver);
        var selected = await regionsService.UpdateServiceRegionSelectionAsync(new(new() { a.RegionId }), default);
        Assert.True(selected.IsSuccess);
        var saved = await db.RestaurantServiceRegions.SingleAsync();
        Assert.Equal(new TimeOnly(10, 0), saved.DeliveryWindowStart); Assert.Equal(new TimeOnly(18, 0), saved.DeliveryWindowEnd);
        var divergent = await regionsService.UpdateServiceRegionAsync(new(a.RegionId, true, new(6, 0), new(8, 0), null), default);
        Assert.True(divergent.IsFailure); Assert.Equal(new TimeOnly(10, 0), saved.DeliveryWindowStart);
    }
    [Fact]
    public async Task TimeOnlySubmission_DoesNotOverwriteExistingCoverageCorrections()
    {
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var original = JsonSerializer.Serialize(new { Address = "Existing correction", ServiceRegionIds = new[] { Guid.NewGuid() } });
        var request = new RestaurantSettingsChangeRequest { RestaurantId = profile.Id, RegistrationId = db.RestaurantRegistrations.Single().Id, Category = RestaurantChangeCategory.Coverage, Status = VerificationStatus.NeedsChanges, PayloadJson = original, BaseVersion = 1 };
        db.RestaurantSettingsChangeRequests.Add(request); await db.SaveChangesAsync();
        var saved = await service.SaveAsync(new(new(10, 0), new(18, 0)), default);
        Assert.True(saved.IsFailure); Assert.Equal(original, request.PayloadJson); Assert.Equal(VerificationStatus.NeedsChanges, request.Status);
    }
    [Fact]
    public async Task StartupBackfill_NewCoverageInheritsCanonicalWindow()
    {
        await service.SaveAsync(new(new(10, 0), new(18, 0), PreparationLeadTimeMinutes: 45), default);
        var a = AddRegion(false);
        profile.PrimaryRegionId = a.RegionId;
        profile.Latitude = 29.3m; profile.Longitude = 47.9m;
        var area = await db.Regions.SingleAsync(); area.CentroidLatitude = 29.3m; area.CentroidLongitude = 47.9m;
        db.RestaurantServiceRegions.Remove(a); await db.SaveChangesAsync();
        await Persistence.Seeding.RestaurantDeliveryCoverageBackfill.SeedAsync(db);
        var created = await db.RestaurantServiceRegions.SingleAsync();
        Assert.Equal(new TimeOnly(10, 0), created.DeliveryWindowStart); Assert.Equal(new TimeOnly(18, 0), created.DeliveryWindowEnd);
        Assert.Equal(45, created.PreparationLeadTimeMinutes);
    }

    [Fact]
    public void Migration_GeneratesColumnsAndUnambiguousBackfillWithoutDatabaseAccess()
    {
        using var relational = new Persistence.AppDbContext(new DbContextOptionsBuilder<Persistence.AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=migration_validation;Username=verification").Options, Substitute.For<IDomainEventDispatcher>());
        Assert.Contains("20261022100000_AddRestaurantDeliveryWindow", relational.Database.GetMigrations());
        var script = relational.GetService<IMigrator>().GenerateScript("20261021130000_Phase02LedgerFoundation", "20261022100000_AddRestaurantDeliveryWindow");
        Assert.Contains("ADD \"DeliveryWindowStart\"", script); Assert.Contains("COUNT(DISTINCT", script); Assert.Contains("UPDATE \"RestaurantServiceRegions\"", script);
        Assert.Contains("ADD \"PreparationLeadTimeMinutes\"", script); Assert.Contains("uniform_preparation", script);
        var snapshot = relational.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = relational.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true);
        var model = relational.GetService<IDesignTimeModel>().Model;
        var differences = relational.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(), model.GetRelationalModel());
        // Check the changed table; the repository has pre-existing migration drift for CustomerServiceCompensations.
        Assert.DoesNotContain(differences, x => (x is ColumnOperation column && column.Table == "RestaurantProfiles") || (x is TableOperation table && table.Name == "RestaurantProfiles"));
    }
    [Fact]
    public async Task WrongTenant_IsRejectedWithoutSaving()
    {
        user.TenantIds.Returns(new[] { Guid.NewGuid() });
        Assert.True((await service.SaveAsync(new(new(10, 0), new(18, 0)), default)).IsFailure);
        Assert.Null(profile.DeliveryWindowStart);
    }

    [Fact]
    public async Task DirectSave_CannotSupersedeOverlappingPendingWindowReview()
    {
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        Assert.True((await service.SaveAsync(new(new(9, 0), new(17, 0)), default)).IsSuccess);
        user.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        var direct = await service.SaveAsync(new(new(10, 0), new(18, 0)), default);
        Assert.True(direct.IsFailure); Assert.Null(profile.DeliveryWindowStart);
    }

}
