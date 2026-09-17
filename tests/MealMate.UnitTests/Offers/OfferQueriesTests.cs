using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferQueriesTests
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
    public async Task GetOfferByIdQuery_ReturnsOffer_WhenExists()
    {
        using var db = CreateDbContext();
        var offer = new Offer
        {
            NameAr = "عرض خاص",
            NameEn = "Special Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();

        var handler = new GetOfferByIdQueryHandler(db);
        var result = await handler.Handle(new GetOfferByIdQuery(offer.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(offer.Id);
        result.Value.NameEn.Should().Be("Special Offer");
    }

    [Fact]
    public async Task GetOffersListQuery_ReturnsPagedResult_WithFiltering()
    {
        using var db = CreateDbContext();
        db.Offers.Add(new Offer
        {
            NameAr = "عرض 1",
            NameEn = "Active Offer",
            Status = OfferStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        });
        db.Offers.Add(new Offer
        {
            NameAr = "عرض 2",
            NameEn = "Draft Offer",
            Status = OfferStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5)
        });
        await db.SaveChangesAsync();

        var handler = new GetOffersListQueryHandler(db);
        var result = await handler.Handle(new GetOffersListQuery(OfferStatus.Active, new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].NameEn.Should().Be("Active Offer");
    }

    [Fact]
    public async Task GetCustomerAvailableOffersQuery_FiltersByEligibility()
    {
        using var db = CreateDbContext();
        var activeOffer = new Offer
        {
            NameAr = "عرض متاح",
            NameEn = "Available Offer",
            Status = OfferStatus.Active,
            CustomerSegment = CustomerSegmentFilter.All,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(5),
            PerCustomerLimit = 2
        };
        db.Offers.Add(activeOffer);
        await db.SaveChangesAsync();

        var engine = new OfferEligibilityEngine();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new GetCustomerAvailableOffersQueryHandler(db, engine, clock);
        var query = new GetCustomerAvailableOffersQuery(
            CustomerId: Guid.NewGuid(),
            CartAmount: 50.000m,
            DurationDays: 20,
            DeliveryGovernorate: "Hawalli");

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value![0].NameEn.Should().Be("Available Offer");
    }
}
