using System.Globalization;
using FluentAssertions;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Realtime;

public sealed class DriverTripResumeLocalizationTests
{
    [Fact]
    public async Task Resume_WithSameOperationKey_UsesCurrentLanguage_AndStoredStartTime()
    {
        using var services = new ServiceCollection().AddLogging().AddMealMateLocalization().BuildServiceProvider();
        using var scope = services.CreateScope();
        var messages = scope.ServiceProvider.GetRequiredService<ILocalizedMessageProvider>();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var driver = new DriverProfile { UserId = Guid.NewGuid(), FullNameAr = "سائق", FullNameEn = "Driver" };
        db.DriverProfiles.Add(driver);
        var startedAt = DateTime.UtcNow.AddHours(-1);
        var trip = new Trip { Code = "RESUME", Status = TripStatus.InProgress, StartedAtUtc = startedAt };
        trip.DriverAssignments.Add(new DriverAssignment { DriverProfileId = driver.Id, Status = "Active" });
        trip.Stops.Add(new TripStop
        {
            OrderCode = "BOX-1", CustomerMaskedId = "CUSTOMER", ZoneNameAr = "حولي", ZoneNameEn = "Hawally",
            MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "Lunch", Status = TripStopStatus.PickedUp
        });
        db.Trips.Add(trip);
        var session = new DriverWorkSession { DriverProfileId = driver.Id, Status = "Active", InDeliveryOrdersCount = 3 };
        db.DriverWorkSessions.Add(session);
        await db.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(driver.UserId);
        var handler = new StartTripCommandHandler(db, user, messages);
        var command = new StartTripCommand(trip.Id, 29.3375m, 48.0281m, Guid.NewGuid().ToString());
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in new[] { "ar", "en", "ar" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var result = await handler.Handle(command, CancellationToken.None);
                result.IsSuccess.Should().BeTrue();
                result.Value.StartedAtUtc.Should().Be(startedAt);
                result.Value.Message.Should().Be(language == "ar"
                    ? "تم بدء رحلة التوصيل بنجاح بحمولة 1 صندوق. رافقتك السلامة!"
                    : "Trip delivery started successfully with 1 boxes. Have a safe drive!");
            }
            session.InDeliveryOrdersCount.Should().Be(3);
            (await db.IdempotencyRecords.CountAsync()).Should().Be(1);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }
}
