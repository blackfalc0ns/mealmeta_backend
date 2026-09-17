using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferLifecycleSchedulerTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_ActivatesScheduledOffers()
    {
        using var db = CreateDbContext();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = DateTime.UtcNow;
        clock.UtcNow.Returns(now);

        var offer = new Offer
        {
            NameAr = "عرض مجدول",
            NameEn = "Scheduled Offer",
            Status = OfferStatus.Scheduled,
            StartsAtUtc = now.AddMinutes(-5),
            EndsAtUtc = now.AddDays(5)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var service = new OfferLifecycleSchedulerService(db, clock);
        var result = await service.ProcessLifecycleTransitionsAsync();

        result.ActivatedCount.Should().Be(1);
        offer.Status.Should().Be(OfferStatus.Active);
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_EndsExpiredOffers()
    {
        using var db = CreateDbContext();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = DateTime.UtcNow;
        clock.UtcNow.Returns(now);

        var offer = new Offer
        {
            NameAr = "عرض منتهي",
            NameEn = "Ended Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = now.AddDays(-10),
            EndsAtUtc = now.AddMinutes(-5)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var service = new OfferLifecycleSchedulerService(db, clock);
        var result = await service.ProcessLifecycleTransitionsAsync();

        result.EndedCount.Should().Be(1);
        offer.Status.Should().Be(OfferStatus.Ended);
    }
}
