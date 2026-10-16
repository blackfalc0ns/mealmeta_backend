using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ReceiptPrinting;

public class ReceiptTemplateDomainTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public void ReceiptTemplate_DefaultInitialization_SetsExpectedDefaults()
    {
        var template = new ReceiptTemplate
        {
            Key = "classic-minimal",
            NameAr = "الكلاسيكي البسيط",
            NameEn = "Classic Minimal",
        };

        template.Key.Should().Be("classic-minimal");
        template.IsEnabled.Should().BeTrue();
        template.IsPublished.Should().BeTrue();
        template.IsRestaurantVisible.Should().BeTrue();
        template.IsGlobalDefault.Should().BeFalse();
        template.Version.Should().Be(1);
        template.ConfigurationJson.Should().Be("{}");
    }

    [Fact]
    public void RestaurantReceiptSettings_DefaultValues_AllowSelection()
    {
        var restaurantId = Guid.NewGuid();
        var settings = new RestaurantReceiptSettings
        {
            RestaurantId = restaurantId,
        };

        settings.RestaurantId.Should().Be(restaurantId);
        settings.AllowRestaurantSelection.Should().BeTrue();
        settings.SelectedTemplateId.Should().BeNull();
        settings.AdminAssignedTemplateId.Should().BeNull();
    }

    [Fact]
    public async Task ReceiptTemplatesSeeder_SeedsExactlyThreeApprovedTemplates()
    {
        using var db = CreateDb();

        await ReceiptTemplatesSeeder.SeedAsync(db, NullLogger.Instance);

        var templates = await db.ReceiptTemplates.ToListAsync();
        templates.Should().HaveCount(3);

        var classic = templates.FirstOrDefault(x => x.Key == "classic-minimal");
        classic.Should().NotBeNull();
        classic!.IsGlobalDefault.Should().BeTrue();
        classic.IsEnabled.Should().BeTrue();
        classic.IsPublished.Should().BeTrue();
        classic.IsRestaurantVisible.Should().BeTrue();

        var organic = templates.FirstOrDefault(x => x.Key == "organic-elegant");
        organic.Should().NotBeNull();
        organic!.IsGlobalDefault.Should().BeFalse();
        organic.IsEnabled.Should().BeTrue();

        var premium = templates.FirstOrDefault(x => x.Key == "premium-dark");
        premium.Should().NotBeNull();
        premium!.IsGlobalDefault.Should().BeFalse();
        premium.IsEnabled.Should().BeTrue();

        // Idempotency: Running seeder again does not create duplicates
        await ReceiptTemplatesSeeder.SeedAsync(db, NullLogger.Instance);
        var templatesAfterSecondRun = await db.ReceiptTemplates.ToListAsync();
        templatesAfterSecondRun.Should().HaveCount(3);
    }
}
