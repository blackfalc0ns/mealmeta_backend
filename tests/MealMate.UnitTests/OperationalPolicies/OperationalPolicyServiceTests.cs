using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;

public class OperationalPolicyServiceTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogger _audit;
    private readonly DateTime _baseNow = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    public OperationalPolicyServiceTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _cache = new MemoryCache(new MemoryCacheOptions());
        _audit = Substitute.For<IAuditLogger>();
    }

    [Fact]
    public void FirstDayDeadline_UsesPaymentVerifiedPlusWindow_WhenItClosesBeforeFirstDelivery()
    {
        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(),
            Freeze: new FreezePolicySettings());

        // Worked example: payment verified Thu 2026-10-08 09:00 UTC; first delivery Sun
        // 2026-10-11 (Kuwait midnight = Oct 10 21:00 UTC). Payment + 24h is still before
        // delivery, so it becomes the first day's selection deadline.
        var deadline = CreateSut().CalculateFirstDaySelectionDeadlineUtc(
            new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 11), policy);

        deadline.Should().Be(new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void FirstDayDeadline_LatePaymentFallsBackToStandardCutoff_WithoutInventingWindow()
    {
        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(),
            Freeze: new FreezePolicySettings());

        // Payment + 24h (Oct 11 15:00 UTC) would land after first delivery (Oct 10 21:00 UTC):
        // no alternative deadline is invented; the standard cutoff applies (already past, so the
        // late-payment path handles dispatch).
        var deadline = CreateSut().CalculateFirstDaySelectionDeadlineUtc(
            new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 11), policy);

        deadline.Should().Be(new DateTime(2026, 10, 7, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void FirstDayDeadline_RespectsConfiguredWindowHours()
    {
        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72, FirstDaySelectionWindowHours: 12),
            Cancellation: new CancellationPolicySettings(),
            Freeze: new FreezePolicySettings());

        var deadline = CreateSut().CalculateFirstDaySelectionDeadlineUtc(
            new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 11), policy);

        deadline.Should().Be(new DateTime(2026, 10, 8, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task FirstDayWindowOutsideAllowedRange_IsRejectedWithoutPersisting()
    {
        var settings = new OperationalPolicySettings() with { Cutoff = new CutoffPolicySettings(FirstDaySelectionWindowHours: 0) };

        (await CreateSut().UpdateSettingsAsync(settings, Guid.NewGuid())).IsFailure.Should().BeTrue();
        (await _db.SystemSettings.CountAsync()).Should().Be(0);
    }

    private OperationalPolicyService CreateSut()
    {
        return new OperationalPolicyService(_db, _cache, _audit);
    }

    [Fact]
    public void MinuteCutoff_IsFiveMinutesBeforeLocalServiceMidnight()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<OperationalPolicySettings>("""
            {"Cutoff":{"CutoffLeadMinutes":5},"Cancellation":{},"Freeze":{}}
            """)!;
        CreateSut().CalculateSelectionDeadlineUtc(new DateOnly(2026, 8, 30), settings)
            .Should().Be(new DateTime(2026, 8, 29, 20, 55, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task PublishedSettings_AreImmediatelyVisibleAcrossIndependentCaches()
    {
        var reader = new OperationalPolicyService(_db, new MemoryCache(new MemoryCacheOptions()), _audit);
        await reader.GetSettingsAsync();
        var writer = CreateSut();
        await writer.UpdateSettingsAsync(new OperationalPolicySettings() with { Cutoff = new CutoffPolicySettings(24) }, Guid.NewGuid());
        (await reader.GetSettingsAsync()).Cutoff.CutoffLeadHours.Should().Be(24);
    }

    [Fact]
    public async Task InvalidFreezeRange_IsRejectedWithoutPersisting()
    {
        var settings = new OperationalPolicySettings() with { Freeze = new FreezePolicySettings(MinDays: 10, MaxDays: 2) };
        (await CreateSut().UpdateSettingsAsync(settings, Guid.NewGuid())).IsFailure.Should().BeTrue();
        (await _db.SystemSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StalePublication_IsRejectedAndKeepsFirstRevision()
    {
        var sut = CreateSut();
        (await sut.UpdateSettingsAsync(new OperationalPolicySettings(), Guid.NewGuid())).IsSuccess.Should().BeTrue();
        (await sut.UpdateSettingsAsync(new OperationalPolicySettings(), Guid.NewGuid())).IsFailure.Should().BeTrue();
        (await sut.GetSettingsAsync()).Version.Should().Be(1);
        (await _db.SystemSettings.CountAsync(x => x.Key.StartsWith(OperationalPolicyService.HistoryPrefix))).Should().Be(1);
    }

    [Fact]
    public void OverflowedLegacyHours_AreInvalid()
    {
        OperationalPolicyRules.Validate(new OperationalPolicySettings() with { Cutoff = new CutoffPolicySettings(71582789) }).Should().NotBeNull();
    }

    [Fact]
    public async Task GetSettingsAsync_WhenNoSettingInDb_ReturnsDefaultSettings()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var settings = await sut.GetSettingsAsync();

        // Assert
        settings.Should().NotBeNull();
        settings.Cutoff.CutoffLeadHours.Should().Be(72);
        settings.Cutoff.EnableFixedDailyHour.Should().BeFalse();
        settings.Cutoff.FixedDailyHourKuwait.Should().Be(14);
        settings.Cancellation.FeeType.Should().Be(CancellationFeeType.Percentage);
        settings.Cancellation.FeeValue.Should().Be(10.0m);
        settings.Cancellation.RefundToCashWallet.Should().BeTrue();
        settings.Freeze.LeadDaysNotice.Should().Be(3);
    }

    [Fact]
    public void CalculateSelectionDeadlineUtc_WhenAbsoluteHours_ReturnsCorrectUtcTimestamp()
    {
        // Arrange
        var sut = CreateSut();
        var serviceDate = new DateOnly(2026, 8, 30); // Sunday
        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48, EnableFixedDailyHour: false),
            Cancellation: new CancellationPolicySettings(),
            Freeze: new FreezePolicySettings()
        );

        // Act: 48 hours before Kuwait midnight of 2026-08-30
        // Kuwait midnight of 2026-08-30 is 2026-08-29 21:00 UTC.
        // Subtract 48 hours -> 2026-08-27 21:00 UTC.
        var deadlineUtc = sut.CalculateSelectionDeadlineUtc(serviceDate, policy);

        // Assert
        var expectedUtc = new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);
        deadlineUtc.Should().Be(expectedUtc);
    }

    [Fact]
    public void CalculateSelectionDeadlineUtc_WhenFixedDailyHourEnabled_ReturnsFixedHourUtc()
    {
        // Arrange
        var sut = CreateSut();
        var serviceDate = new DateOnly(2026, 8, 30); // Sunday
        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(
                CutoffLeadHours: 72, // 3 days lead -> Thursday 2026-08-27
                EnableFixedDailyHour: true,
                FixedDailyHourKuwait: 14), // 14:00 Kuwait time (+3) = 11:00 UTC
            Cancellation: new CancellationPolicySettings(),
            Freeze: new FreezePolicySettings()
        );

        // Act
        var deadlineUtc = sut.CalculateSelectionDeadlineUtc(serviceDate, policy);

        // Assert: 2026-08-27 at 11:00:00 UTC
        var expectedUtc = new DateTime(2026, 8, 27, 11, 0, 0, DateTimeKind.Utc);
        deadlineUtc.Should().Be(expectedUtc);
    }

    [Fact]
    public async Task UpdateSettingsAsync_PersistsSettingInDb_AndUpdatesCachedValue()
    {
        // Arrange
        var sut = CreateSut();
        var adminId = Guid.NewGuid();
        var newSettings = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48, EnableFixedDailyHour: true, FixedDailyHourKuwait: 15),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.FixedAmount, FeeValue: 5.0m),
            Freeze: new FreezePolicySettings(LeadDaysNotice: 2, MaxDays: 20)
        );

        // Act
        var result = await sut.UpdateSettingsAsync(newSettings, adminId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Cutoff.CutoffLeadHours.Should().Be(48);
        result.Value.Cancellation.FeeType.Should().Be(CancellationFeeType.FixedAmount);
        result.Value.Cancellation.FeeValue.Should().Be(5.0m);

        // Check DB row was saved
        var dbRow = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == OperationalPolicyService.SettingKey);
        dbRow.Should().NotBeNull();
        dbRow!.IsActive.Should().BeTrue();

        // Check subsequent call uses cached / updated value
        var refreshed = await sut.GetSettingsAsync();
        refreshed.Cutoff.CutoffLeadHours.Should().Be(48);
        refreshed.Cancellation.FeeValue.Should().Be(5.0m);
    }
}
