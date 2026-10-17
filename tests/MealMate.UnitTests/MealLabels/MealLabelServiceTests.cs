using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MealLabels;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.MealLabels;

public sealed class MealLabelServiceTests
{
    private static (MealLabelService Service, AppDbContext Db) CreateService()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Branding:MealMateLogoUrl"] = "/assets/images/logo/mealmate-logo.webp"
            })
            .Build();

        var service = new MealLabelService(context, clock, config, NullLogger<MealLabelService>.Instance);
        return (service, context);
    }

    [Fact]
    public async Task Seeder_Should_Seed_Exactly_Five_Templates()
    {
        var (service, db) = CreateService();
        await MealBoxLabelTemplatesSeeder.SeedAsync(db, NullLogger.Instance);

        var templates = await service.GetAllTemplatesForAdminAsync();

        templates.Should().HaveCount(5);
        templates.Select(x => x.Key).Should().BeEquivalentTo(new[]
        {
            "classic-clean",
            "organic-nature",
            "luxury-dark",
            "fresh-modern",
            "eco-kraft"
        });

        var classicClean = templates.First(x => x.Key == "classic-clean");
        classicClean.IsGlobalDefault.Should().BeTrue();
        classicClean.DefaultWidthMm.Should().Be(70);
        classicClean.DefaultHeightMm.Should().Be(100);
        classicClean.DefaultOrientation.Should().Be("portrait");
    }

    [Fact]
    public async Task Preview_Should_Contain_No_Barcode_And_No_QrCode()
    {
        var (service, db) = CreateService();
        await MealBoxLabelTemplatesSeeder.SeedAsync(db, NullLogger.Instance);

        var classic = await db.MealBoxLabelTemplates.FirstAsync(x => x.Key == "classic-clean");
        var result = await service.GetPreviewMealLabelAsync(classic.Id);

        result.IsSuccess.Should().BeTrue();
        var label = result.Value;

        label.EffectiveTemplateKey.Should().Be("classic-clean");
        label.Dimensions.WidthMm.Should().Be(70);
        label.Dimensions.HeightMm.Should().Be(100);
        label.Dimensions.Orientation.Should().Be("portrait");
        label.Meal.NameAr.Should().NotBeNullOrWhiteSpace();
        label.Meal.NameEn.Should().NotBeNullOrWhiteSpace();
        label.Meal.Nutrition.Calories.Should().BeGreaterThan(0);
        label.Meal.IngredientsAr.Should().NotBeNullOrWhiteSpace();
        label.Meal.AllergensAr.Should().NotBeNullOrWhiteSpace();
        label.Meal.PrepNotesAr.Should().NotBeNullOrWhiteSpace();

        label.AppBrand.LogoUrl.Should().Contain("mealmate-logo");
        label.RestaurantBrand.NameAr.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Admin_Can_Set_Global_Default_Template()
    {
        var (service, db) = CreateService();
        await MealBoxLabelTemplatesSeeder.SeedAsync(db, NullLogger.Instance);

        var luxury = await db.MealBoxLabelTemplates.FirstAsync(x => x.Key == "luxury-dark");
        var result = await service.SetGlobalDefaultTemplateAsync(luxury.Id);

        result.IsSuccess.Should().BeTrue();

        var templates = await service.GetAllTemplatesForAdminAsync();
        templates.First(x => x.Key == "luxury-dark").IsGlobalDefault.Should().BeTrue();
        templates.First(x => x.Key == "classic-clean").IsGlobalDefault.Should().BeFalse();
    }
    }

