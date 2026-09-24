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

    private OperationalPolicyService CreateSut()
    {
        return new OperationalPolicyService(_db, _cache, _audit);
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
