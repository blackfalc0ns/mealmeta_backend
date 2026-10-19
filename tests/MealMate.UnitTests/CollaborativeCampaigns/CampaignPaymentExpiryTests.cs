using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public sealed class CampaignPaymentExpiryTests
{
    [Fact]
    public async Task Expiry_ReleasesThisSessionsCapacityOnceAndMarksItsRelease()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var session = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending, ExpiresAtUtc = now.AddMinutes(-1),
            FinalAmount = 10m, CurrencyCode = "KWD", PaymentIdempotencyKey = "payment-1"
        };
        var restaurantId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 20);
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id, CampaignId = session.CampaignId, BundleId = session.BundleId,
            RestaurantId = restaurantId, ServiceDate = date, Units = 1
        });
        db.CampaignCheckoutSessions.Add(session);
        db.CampaignRestaurantCapacities.Add(new CampaignRestaurantCapacity
        {
            CampaignId = session.CampaignId, RestaurantId = restaurantId, ServiceDate = date,
            MaxBoxes = 10, ReservedBoxes = 3
        });
        await db.SaveChangesAsync();
        var service = new CampaignReservationExpirationService(db, clock);

        Assert.Equal(1, await service.ExpireStaleReservationsAsync());
        Assert.Equal(now, session.ReservationsReleasedAtUtc);
        Assert.Equal(2, (await db.CampaignRestaurantCapacities.SingleAsync()).ReservedBoxes);
        Assert.Equal(0, await service.ExpireStaleReservationsAsync());
        Assert.Equal(2, (await db.CampaignRestaurantCapacities.SingleAsync()).ReservedBoxes);
    }
}
