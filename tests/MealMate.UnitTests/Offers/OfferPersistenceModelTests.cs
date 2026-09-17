using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Offers;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Offers;

public class OfferPersistenceModelTests
{
    [Fact]
    public void AppDbContext_RegistersOfferDbSets()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        using var context = new AppDbContext(options, dispatcher);

        context.Offers.Should().NotBeNull();
        context.OfferPrograms.Should().NotBeNull();
        context.OfferBundles.Should().NotBeNull();
        context.OfferRestaurants.Should().NotBeNull();
        context.OfferRedemptions.Should().NotBeNull();
        context.OfferAuditLogs.Should().NotBeNull();
    }
}
