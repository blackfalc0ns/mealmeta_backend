using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.PrepDocuments;

public class PrepDocumentsGeneratorTests
{
    private readonly DateTime _now = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _serviceDate = new(2026, 8, 28);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private PrepDocumentsGenerator CreateSut(IAppDbContext db, string? mealMateLogoUrl = "https://cdn.example/mealmate.png")
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_now);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrepDocumentsGenerator.MealMateLogoConfigKey] = mealMateLogoUrl,
            })
            .Build();

        return new PrepDocumentsGenerator(
            db,
            clock,
            configuration,
            NullLogger<PrepDocumentsGenerator>.Instance);
    }

    private async Task<(Guid DayId, Meal Meal1, Meal Meal2)> SeedConfirmedDayWithMealsAsync(AppDbContext db)
    {
        var program = new Program { Code = "PRG", NameAr = "برنامج", NameEn = "Program" };
        var bundle = new Bundle { Code = "BND", NameAr = "باقة", NameEn = "Bundle", ProgramId = program.Id };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        var subscription = new Subscription
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            CustomerUserId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            StartDate = _serviceDate,
            EndDate = _serviceDate.AddDays(25),
            Status = SubscriptionStatus.Active,
        };
        db.Subscriptions.Add(subscription);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "قانوني",
            LegalNameEn = "Legal",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            ContactPhone = "+96550000000",
        };
        db.RestaurantProfiles.Add(restaurant);

        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Restaurant,
            OwnerId = restaurant.Id,
            DocumentType = DocumentType.ProfileImage,
            FileName = "logo.png",
            ContentType = "image/png",
            StoragePath = "uploads/restaurant/logo.png",
            SizeBytes = 1024,
            CreatedAtUtc = _now,
        });

        var address = new CustomerAddress
        {
            UserId = subscription.CustomerUserId,
            Label = "Home",
            AddressLine = "Full street address with phone context",
            Governorate = "Capital",
            Area = "Salmiya",
            Block = "12",
        };
        db.CustomerAddresses.Add(address);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = restaurant.Id,
            ServiceDate = _serviceDate,
            Status = SubscriptionDayStatus.ConfirmedByRestaurant,
            DeliveryAddressId = address.Id,
            DeliveryAddressLine = address.AddressLine,
            ConfirmedAtUtc = _now.AddHours(-2),
            CreatedAtUtc = _now.AddDays(-1),
        };
        db.SubscriptionDays.Add(day);

        var menu = new Menu
        {
            RestaurantId = restaurant.Id,
            NameAr = "منيو",
            NameEn = "Menu",
        };
        db.Menus.Add(menu);

        var meal1 = new Meal
        {
            MenuId = menu.Id,
            RestaurantId = restaurant.Id,
            Code = "M1",
            NameAr = "وجبة 1",
            NameEn = "Meal 1",
            Slot = "main",
            Calories = 400,
            ProteinG = 30,
            CarbsG = 40,
            FatG = 10,
        };
        var meal2 = new Meal
        {
            MenuId = menu.Id,
            RestaurantId = restaurant.Id,
            Code = "M2",
            NameAr = "وجبة 2",
            NameEn = "Meal 2",
            Slot = "side",
            Calories = 250,
            ProteinG = 15,
            CarbsG = 20,
            FatG = 8,
        };
        db.Meals.AddRange(meal1, meal2);

        db.DailyMealSelections.AddRange(
            new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = meal1.Id,
                SortOrder = 1,
                SelectedAtUtc = _now,
            },
            new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = meal2.Id,
                SortOrder = 2,
                SelectedAtUtc = _now,
            });

        await SeedPublishedTemplatesAsync(db);
        await db.SaveChangesAsync();

        return (day.Id, meal1, meal2);
    }

    private static async Task SeedPublishedTemplatesAsync(AppDbContext db)
    {
        db.DocumentTemplates.AddRange(
            CreatePublishedTemplate(DocumentTemplateKind.Invoice),
            CreatePublishedTemplate(DocumentTemplateKind.BoxLabel),
            CreatePublishedTemplate(DocumentTemplateKind.MealLabel));
        await db.SaveChangesAsync();
    }

    private static DocumentTemplate CreatePublishedTemplate(DocumentTemplateKind kind)
        => new()
        {
            Kind = kind,
            Status = DocumentTemplateStatus.Published,
            NameAr = "قالب",
            NameEn = "Template",
            PageWidthPx = 420,
            PageHeightPx = 300,
            LayoutJson = "[{\"kind\":\"logo\",\"id\":\"mealmate_logo\"}]",
            Version = 2,
            PublishedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task Generate_creates_barcode_invoice_box_label_and_meal_labels()
    {
        using var db = CreateDb();
        var (dayId, _, _) = await SeedConfirmedDayWithMealsAsync(db);
        var sut = CreateSut(db);

        var result = await sut.GenerateForDayAsync(dayId);

        result.IsSuccess.Should().BeTrue();

        (await db.MealBarcodes.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(1);
        (await db.MealInvoices.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(1);
        (await db.MealLabels.CountAsync(x => x.SubscriptionDayId == dayId && x.Kind == MealLabelKind.Box)).Should().Be(1);
        (await db.MealLabels.CountAsync(x => x.SubscriptionDayId == dayId && x.Kind == MealLabelKind.Meal)).Should().Be(2);

        var invoice = await db.MealInvoices.SingleAsync(x => x.SubscriptionDayId == dayId);
        var invoiceJson = invoice.SnapshotJson!;
        invoiceJson.Should().Contain("mealMateLogoUrl");
        invoiceJson.Should().Contain("restaurantLogoUrl");
        invoiceJson.Should().Contain("https://cdn.example/mealmate.png");
        invoiceJson.ToLowerInvariant().Should().NotContain("phone");

        using var invoiceDoc = JsonDocument.Parse(invoiceJson);
        invoiceDoc.RootElement.GetProperty("shortAddress").GetString().Should().Be("Capital, Salmiya, 12");

        var boxLabel = await db.MealLabels.SingleAsync(x => x.SubscriptionDayId == dayId && x.Kind == MealLabelKind.Box);
        boxLabel.LabelPayloadJson.Should().Contain("mealMateLogoUrl");
        boxLabel.LabelPayloadJson.Should().Contain("restaurantLogoUrl");
    }

    [Fact]
    public async Task Generate_is_idempotent()
    {
        using var db = CreateDb();
        var (dayId, _, _) = await SeedConfirmedDayWithMealsAsync(db);
        var sut = CreateSut(db);

        await sut.GenerateForDayAsync(dayId);
        await sut.GenerateForDayAsync(dayId);

        (await db.MealBarcodes.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(1);
        (await db.MealInvoices.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(1);
        (await db.MealLabels.CountAsync(x => x.SubscriptionDayId == dayId && x.Kind == MealLabelKind.Box)).Should().Be(1);
    }

    [Fact]
    public async Task Generate_skips_when_published_template_missing()
    {
        using var db = CreateDb();
        var (dayId, _, _) = await SeedConfirmedDayWithMealsAsync(db);

        var mealLabelTemplate = await db.DocumentTemplates
            .SingleAsync(x => x.Kind == DocumentTemplateKind.MealLabel);
        db.DocumentTemplates.Remove(mealLabelTemplate);
        await db.SaveChangesAsync();

        var sut = CreateSut(db);
        var result = await sut.GenerateForDayAsync(dayId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("prep_documents.missing_template");

        (await db.MealBarcodes.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(0);
        (await db.MealInvoices.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(0);
        (await db.MealLabels.CountAsync(x => x.SubscriptionDayId == dayId)).Should().Be(0);
    }
}
