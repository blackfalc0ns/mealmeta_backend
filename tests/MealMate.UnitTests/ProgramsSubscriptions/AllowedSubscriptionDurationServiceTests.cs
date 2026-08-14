using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.AdminDashboard;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class AllowedSubscriptionDurationServiceTests
{
    [Fact]
    public void Normalize_KeepsSingleCustomPicker_WithClampedRange()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        var primaryCustom = settings.Durations.Single(x => x.IsCustom);
        primaryCustom.MinDays = 3;
        primaryCustom.MaxDays = 100;
        settings.Durations.Add(new DurationOptionSettings
        {
            Id = "DUR-CUSTOM-2",
            IsCustom = true,
            Days = 10,
            MinDays = 8,
            MaxDays = 20,
            IsActive = true,
        });
        settings.Durations.Add(new DurationOptionSettings { Id = "DUR-9", Days = 9, IsCustom = false, IsActive = true });

        var normalized = SubscriptionPricingSettingsReader.Normalize(settings);

        normalized.Durations.Count(x => x.IsCustom).Should().Be(1);
        normalized.Durations.Where(x => !x.IsCustom).Select(x => x.Days).Should().BeEquivalentTo([6, 12, 26]);
        var custom = normalized.Durations.Single(x => x.IsCustom);
        custom.Id.Should().Be(DurationOptionSettings.CustomId);
        custom.MinDays.Should().Be(DurationOptionSettings.AbsoluteMinDays);
        custom.MaxDays.Should().Be(DurationOptionSettings.AbsoluteMaxDays);
    }

    [Fact]
    public void Validate_RejectsInvalidCustomRange()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        var custom = settings.Durations.Single(x => x.IsCustom);
        custom.MinDays = 20;
        custom.MaxDays = 10;

        var error = SubscriptionPricingSettingsValidator.Validate(settings);

        error.Should().NotBeNull();
        error!.Code.Should().Be("pricing_settings.custom_duration_range");
    }

    [Fact]
    public void Normalize_KeepsTierLogos_OnlyUnderTierPrefix()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.TierLabels.BasicLogoStorageKey = "uploads/admin/programs/x.webp";
        settings.TierLabels.PlatinumLogoStorageKey = "uploads/admin/tiers/platinum.webp";
        settings.TierLabels.EliteLogoStorageKey = "/api/v1/uploads/content?key=uploads%2Fadmin%2Ftiers%2Felite.webp";

        var normalized = SubscriptionPricingSettingsReader.Normalize(settings);

        normalized.TierLabels.BasicLogoStorageKey.Should().BeNull();
        normalized.TierLabels.PlatinumLogoStorageKey.Should().Be("uploads/admin/tiers/platinum.webp");
        normalized.TierLabels.EliteLogoStorageKey.Should().Be("uploads/admin/tiers/elite.webp");
    }

    [Theory]
    [InlineData(6, false, true)]
    [InlineData(12, false, true)]
    [InlineData(26, false, true)]
    [InlineData(10, false, true)]
    [InlineData(5, false, false)]
    [InlineData(30, false, false)]
    public async Task Evaluate_AllowsActivePresets_AndCustomRange(int days, bool includeFriday, bool expected)
    {
        await using var db = CreateDbWithSettings();
        var service = new AllowedSubscriptionDurationService(db);

        var decision = await service.EvaluateAsync(days, includeFriday);

        decision.IsAllowed.Should().Be(expected);
        if (!expected)
            decision.ErrorCode.Should().Be("subscription_flow.duration_not_available");
    }

    [Fact]
    public async Task Evaluate_RejectsFriday_WhenCustomDisallowsFriday()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.Durations.Single(x => x.IsCustom).AllowsFriday = false;
        await using var db = CreateDbWithSettings(settings);
        var service = new AllowedSubscriptionDurationService(db);

        var decision = await service.EvaluateAsync(10, includeFriday: true);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("subscription_flow.friday_unavailable");
    }

    [Fact]
    public async Task Evaluate_RejectsCustom_WhenCustomInactive()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.Durations.Single(x => x.IsCustom).IsActive = false;
        await using var db = CreateDbWithSettings(settings);
        var service = new AllowedSubscriptionDurationService(db);

        var decision = await service.EvaluateAsync(10, includeFriday: false);

        decision.IsAllowed.Should().BeFalse();
        decision.ErrorCode.Should().Be("subscription_flow.duration_not_available");
    }

    [Fact]
    public async Task Evaluate_UsesCustom_WhenMatchingPresetIsInactive()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.Durations.Single(x => x.Days == 12).IsActive = false;
        settings.Durations.Single(x => x.IsCustom).MinDays = 7;
        settings.Durations.Single(x => x.IsCustom).MaxDays = 25;
        await using var db = CreateDbWithSettings(settings);
        var service = new AllowedSubscriptionDurationService(db);

        var insideCustom = await service.EvaluateAsync(12, false);
        insideCustom.IsAllowed.Should().BeTrue();
        insideCustom.IsCustom.Should().BeTrue();
    }

    [Fact]
    public async Task Evaluate_RejectsDays_OutsideActivePresetsAndCustomRange()
    {
        var settings = SubscriptionPricingSettingsDefaults.Create();
        settings.Durations.Single(x => x.Days == 6).IsActive = false;
        settings.Durations.Single(x => x.IsCustom).MinDays = 7;
        settings.Durations.Single(x => x.IsCustom).MaxDays = 25;
        await using var db = CreateDbWithSettings(settings);
        var service = new AllowedSubscriptionDurationService(db);

        var six = await service.EvaluateAsync(6, false);
        six.IsAllowed.Should().BeFalse();
        six.ErrorCode.Should().Be("subscription_flow.duration_not_available");
    }

    private static AppDbContext CreateDbWithSettings(SubscriptionPricingSettingsDocument? settings = null)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var document = settings ?? SubscriptionPricingSettingsDefaults.Create();
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(document, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        db.SaveChanges();
        return db;
    }
}
