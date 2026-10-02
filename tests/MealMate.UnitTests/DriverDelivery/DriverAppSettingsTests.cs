using System.Globalization;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.DriverDelivery;

public sealed class DriverAppSettingsTests
{
    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static ICurrentUser CurrentUser(Guid? userId, bool isDriver = true)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(userId.HasValue);
        user.UserId.Returns(userId);
        user.IsInRole(AppRoles.Driver).Returns(isDriver);
        return user;
    }

    private static async Task<(bool Success, JsonElement Payload)> Get(AppDbContext db, Guid? userId, bool isDriver = true)
    {
        var handler = new GetDriverAppSettingsQueryHandler(db, CurrentUser(userId, isDriver));
        var result = await handler.Handle(new GetDriverAppSettingsQuery(), CancellationToken.None);
        object payload = result.IsSuccess ? result.Value : result.Error;
        return (result.IsSuccess, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<(bool Success, JsonElement Payload)> Put(AppDbContext db, Guid? userId, UpdateDriverAppSettingsCommand command, bool isDriver = true)
    {
        var handler = new UpdateDriverAppSettingsCommandHandler(db, CurrentUser(userId, isDriver));
        var result = await handler.Handle(command, CancellationToken.None);
        object payload = result.IsSuccess ? result.Value : result.Error;
        return (result.IsSuccess, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Get_WithoutStoredRow_ReturnsAllNulls_AndCreatesNoRow()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (success, p) = await Get(db, driver.UserId);

        Assert.True(success);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("preferredLanguage").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("preferredNavigationApp").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("autoLaunchNavigation").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("soundAlertsEnabled").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("vibrationEnabled").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("biometricLoginEnabled").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("offlineSync").GetProperty("autoSyncOnReconnect").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("offlineSync").GetProperty("pendingSyncItemsCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("offlineSync").GetProperty("lastSyncedAtUtc").ValueKind);
        Assert.False(await db.DriverAppSettings.AnyAsync());
    }

    [Fact]
    public async Task Get_ReturnsOnlyTheSignedInDriverSettings()
    {
        await using var db = CreateDb();
        var mine = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        var other = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "آخر", FullNameEn = "Other" };
        db.DriverProfiles.AddRange(mine, other);
        db.DriverAppSettings.AddRange(
            new DriverAppSettings { DriverProfileId = mine.Id, PreferredLanguage = "ar", SoundAlertsEnabled = true },
            new DriverAppSettings { DriverProfileId = other.Id, PreferredLanguage = "en", SoundAlertsEnabled = false });
        await db.SaveChangesAsync();

        var (success, p) = await Get(db, mine.UserId);

        Assert.True(success);
        Assert.Equal("ar", p.GetProperty("preferredLanguage").GetString());
        Assert.True(p.GetProperty("soundAlertsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Get_UnauthenticatedWrongRoleOrMissingProfile_FailsTruthfully()
    {
        await using var db = CreateDb();
        db.DriverProfiles.Add(new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "آخر", FullNameEn = "Other" });
        await db.SaveChangesAsync();

        var anonymous = await Get(db, null);
        Assert.False(anonymous.Success);
        Assert.Equal(4, anonymous.Payload.GetProperty("type").GetInt32());

        var wrongRole = await Get(db, Guid.NewGuid(), isDriver: false);
        Assert.False(wrongRole.Success);
        Assert.Equal(5, wrongRole.Payload.GetProperty("type").GetInt32());

        var missingProfile = await Get(db, Guid.NewGuid());
        Assert.False(missingProfile.Success);
        Assert.Equal(2, missingProfile.Payload.GetProperty("type").GetInt32());
        Assert.Equal(MessageCodes.Dispatcher.DriverNotFound, missingProfile.Payload.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Put_CreatesRowOnFirstSave_AndReturnsReadAfterWrite()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (success, p) = await Put(db, driver.UserId, new UpdateDriverAppSettingsCommand(
            PreferredLanguage: "ar", PreferredNavigationApp: "GoogleMaps", AutoLaunchNavigation: true,
            SoundAlertsEnabled: true, VibrationEnabled: null, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: true));

        Assert.True(success);
        Assert.Equal("ar", p.GetProperty("preferredLanguage").GetString());
        Assert.Equal("GoogleMaps", p.GetProperty("preferredNavigationApp").GetString());
        Assert.True(p.GetProperty("autoLaunchNavigation").GetBoolean());
        Assert.True(p.GetProperty("soundAlertsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("vibrationEnabled").ValueKind);
        Assert.True(p.GetProperty("offlineSync").GetProperty("autoSyncOnReconnect").GetBoolean());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("offlineSync").GetProperty("pendingSyncItemsCount").ValueKind);

        var stored = await db.DriverAppSettings.SingleAsync(s => s.DriverProfileId == driver.Id);
        Assert.Equal("ar", stored.PreferredLanguage);
        Assert.Equal("GoogleMaps", stored.PreferredNavigationApp);
        Assert.True(stored.AutoLaunchNavigation);
        Assert.True(stored.AutoSyncOnReconnect);
        Assert.Null(stored.VibrationEnabled);

        var (getSuccess, gp) = await Get(db, driver.UserId);
        Assert.True(getSuccess);
        Assert.Equal("ar", gp.GetProperty("preferredLanguage").GetString());
        Assert.True(gp.GetProperty("soundAlertsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Put_LeavesOmittedFieldsUnchanged_AndDoesNotTouchOtherDrivers()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        var other = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "آخر", FullNameEn = "Other" };
        db.DriverProfiles.AddRange(driver, other);
        db.DriverAppSettings.AddRange(
            new DriverAppSettings { DriverProfileId = driver.Id, PreferredLanguage = "en",
                PreferredNavigationApp = "Waze", SoundAlertsEnabled = true },
            new DriverAppSettings { DriverProfileId = other.Id, PreferredLanguage = "ar", VibrationEnabled = true });
        await db.SaveChangesAsync();

        var (success, p) = await Put(db, driver.UserId, new UpdateDriverAppSettingsCommand(
            PreferredLanguage: null, PreferredNavigationApp: null, AutoLaunchNavigation: null,
            SoundAlertsEnabled: null, VibrationEnabled: false, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: null));

        Assert.True(success);
        Assert.Equal("en", p.GetProperty("preferredLanguage").GetString());
        Assert.Equal("Waze", p.GetProperty("preferredNavigationApp").GetString());
        Assert.True(p.GetProperty("soundAlertsEnabled").GetBoolean());
        Assert.False(p.GetProperty("vibrationEnabled").GetBoolean());

        var mine = await db.DriverAppSettings.SingleAsync(s => s.DriverProfileId == driver.Id);
        Assert.Equal("en", mine.PreferredLanguage);
        Assert.Equal("Waze", mine.PreferredNavigationApp);
        Assert.True(mine.SoundAlertsEnabled);
        Assert.False(mine.VibrationEnabled);
        Assert.Null(mine.AutoLaunchNavigation);

        var others = await db.DriverAppSettings.SingleAsync(s => s.DriverProfileId == other.Id);
        Assert.Equal("ar", others.PreferredLanguage);
        Assert.True(others.VibrationEnabled);
    }

    [Fact]
    public async Task Put_NormalizesCasing_ToCanonicalValues()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (success, p) = await Put(db, driver.UserId, new UpdateDriverAppSettingsCommand(
            PreferredLanguage: "EN", PreferredNavigationApp: "waze", AutoLaunchNavigation: null,
            SoundAlertsEnabled: null, VibrationEnabled: null, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: null));

        Assert.True(success);
        Assert.Equal("en", p.GetProperty("preferredLanguage").GetString());
        Assert.Equal("Waze", p.GetProperty("preferredNavigationApp").GetString());
    }

    [Fact]
    public async Task Put_RejectsInvalidLanguage_AndPersistsNothing()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (success, p) = await Put(db, driver.UserId, new UpdateDriverAppSettingsCommand(
            PreferredLanguage: "fr", PreferredNavigationApp: null, AutoLaunchNavigation: null,
            SoundAlertsEnabled: null, VibrationEnabled: null, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: null));

        Assert.False(success);
        Assert.Equal(1, p.GetProperty("type").GetInt32());
        Assert.Equal("driver.settings.language_invalid", p.GetProperty("code").GetString());
        Assert.False(await db.DriverAppSettings.AnyAsync());
    }

    [Fact]
    public async Task Put_RejectsInvalidNavigationApp_AndPersistsNothing()
    {
        await using var db = CreateDb();
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "أحمد", FullNameEn = "Ahmed" };
        db.DriverProfiles.Add(driver);
        await db.SaveChangesAsync();

        var (success, p) = await Put(db, driver.UserId, new UpdateDriverAppSettingsCommand(
            PreferredLanguage: null, PreferredNavigationApp: "BingMaps", AutoLaunchNavigation: null,
            SoundAlertsEnabled: null, VibrationEnabled: null, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: null));

        Assert.False(success);
        Assert.Equal(1, p.GetProperty("type").GetInt32());
        Assert.Equal("driver.settings.navigation_app_invalid", p.GetProperty("code").GetString());
        Assert.False(await db.DriverAppSettings.AnyAsync());
    }

    [Fact]
    public async Task Put_WithoutDriverProfile_ReturnsNotFound_AndPersistsNothing()
    {
        await using var db = CreateDb();

        var (success, p) = await Put(db, Guid.NewGuid(), new UpdateDriverAppSettingsCommand(
            PreferredLanguage: "ar", PreferredNavigationApp: null, AutoLaunchNavigation: null,
            SoundAlertsEnabled: null, VibrationEnabled: null, BiometricLoginEnabled: null,
            AutoSyncOnReconnect: null));

        Assert.False(success);
        Assert.Equal(2, p.GetProperty("type").GetInt32());
        Assert.Equal(MessageCodes.Dispatcher.DriverNotFound, p.GetProperty("code").GetString());
        Assert.False(await db.DriverAppSettings.AnyAsync());
    }
}
