using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ReceiptPrinting;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ReceiptPrinting;

public class ReceiptResolutionServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static (ReceiptPrintingService Service, AppDbContext Db) CreateService(AppDbContext? db = null)
    {
        var context = db ?? CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc));

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Branding:MealMateLogoUrl"] = "/assets/branding/mealmate-logo.png",
            ["Branding:PublicReceiptBaseUrl"] = "https://app.mealmate.kw/receipt/"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var userAccounts = Substitute.For<IUserAccountService>();
        var service = new ReceiptPrintingService(context, clock, config, userAccounts, NullLogger<ReceiptPrintingService>.Instance);
        return (service, context);
    }

    private static async Task SeedTemplatesAsync(AppDbContext db)
    {
        var classic = new ReceiptTemplate
        {
            Key = "classic-minimal",
            NameAr = "الكلاسيكي البسيط",
            NameEn = "Classic Minimal",
            IsEnabled = true,
            IsPublished = true,
            IsRestaurantVisible = true,
            IsGlobalDefault = true,
            Version = 1,
            ConfigurationJson = "{}"
        };

        var organic = new ReceiptTemplate
        {
            Key = "organic-elegant",
            NameAr = "الأنيق الطبيعي",
            NameEn = "Organic Elegant",
            IsEnabled = true,
            IsPublished = true,
            IsRestaurantVisible = true,
            IsGlobalDefault = false,
            Version = 1,
            ConfigurationJson = "{}"
        };

        var premium = new ReceiptTemplate
        {
            Key = "premium-dark",
            NameAr = "الداكن الفاخر",
            NameEn = "Premium Dark",
            IsEnabled = true,
            IsPublished = true,
            IsRestaurantVisible = false, // e.g. hidden from restaurants by default
            IsGlobalDefault = false,
            Version = 1,
            ConfigurationJson = "{}"
        };

        db.ReceiptTemplates.AddRange(classic, organic, premium);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetRestaurantSettings_WhenNoCustomSetting_ResolvesGlobalDefault()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var restaurantId = Guid.NewGuid();
        var settings = await service.GetRestaurantSettingsAsync(restaurantId);

        settings.EffectiveTemplateKey.Should().Be("classic-minimal");
        settings.SelectedTemplateId.Should().BeNull();
        settings.AdminAssignedTemplateId.Should().BeNull();
        settings.AllowRestaurantSelection.Should().BeTrue();
    }

    [Fact]
    public async Task RestaurantSelectTemplate_WhenAllowed_UpdatesSelectedAndEffective()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var restaurantId = Guid.NewGuid();
        var organic = await db.ReceiptTemplates.FirstAsync(x => x.Key == "organic-elegant");

        var result = await service.RestaurantSelectTemplateAsync(restaurantId, organic.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.SelectedTemplateKey.Should().Be("organic-elegant");
        result.Value.EffectiveTemplateKey.Should().Be("organic-elegant");
    }

    [Fact]
    public async Task RestaurantSelectTemplate_WhenSelectionDisallowed_ReturnsForbidden()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var restaurantId = Guid.NewGuid();
        var organic = await db.ReceiptTemplates.FirstAsync(x => x.Key == "organic-elegant");

        // Admin disables restaurant selection
        await service.AdminUpdateRestaurantSettingsAsync(restaurantId, null, allowRestaurantSelection: false);

        var result = await service.RestaurantSelectTemplateAsync(restaurantId, organic.Id);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("receipt_settings.selection_locked");
    }

    [Fact]
    public async Task AdminAssignedTemplate_OverridesRestaurantSelection()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var restaurantId = Guid.NewGuid();
        var organic = await db.ReceiptTemplates.FirstAsync(x => x.Key == "organic-elegant");
        var classic = await db.ReceiptTemplates.FirstAsync(x => x.Key == "classic-minimal");

        // Restaurant selected organic
        await service.RestaurantSelectTemplateAsync(restaurantId, organic.Id);

        // Admin forces classic
        var adminResult = await service.AdminUpdateRestaurantSettingsAsync(restaurantId, classic.Id, allowRestaurantSelection: false);
        adminResult.IsSuccess.Should().BeTrue();
        adminResult.Value.EffectiveTemplateKey.Should().Be("classic-minimal");
        adminResult.Value.AdminAssignedTemplateKey.Should().Be("classic-minimal");
    }

    [Fact]
    public async Task GetAllowedTemplatesForRestaurant_ExcludesNonVisibleOrDisabled()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var restaurantId = Guid.NewGuid();
        var allowed = await service.GetAllowedTemplatesForRestaurantAsync(restaurantId);

        // classic-minimal (visible) and organic-elegant (visible), premium-dark is hidden
        allowed.Should().HaveCount(2);
        allowed.Select(x => x.Key).Should().Contain("classic-minimal");
        allowed.Select(x => x.Key).Should().Contain("organic-elegant");
        allowed.Select(x => x.Key).Should().NotContain("premium-dark");
    }

    [Fact]
    public async Task UpdateVisibility_CannotDisableGlobalDefault()
    {
        var (service, db) = CreateService();
        await SeedTemplatesAsync(db);

        var classic = await db.ReceiptTemplates.FirstAsync(x => x.Key == "classic-minimal");

        var result = await service.UpdateVisibilityAsync(classic.Id, isEnabled: false, isRestaurantVisible: true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("receipt_template.cannot_disable_default");
    }

    [Theory]
    [InlineData("Health & Wellness Kitchen", "مطبخ الصحة والعافية", "HW")]
    [InlineData("Diet Care", "دايت كير", "DC")]
    [InlineData("Green Bowl Healthy Meals", "جرين بول", "GB")]
    [InlineData("", "مطبخ القبلة", "مق")]
    public void GenerateInitials_ComputesCorrectFallback(string nameEn, string nameAr, string expected)
    {
        var initials = ReceiptPrintingService.GenerateInitials(nameEn, nameAr);
        initials.Should().Be(expected);
    }
}
