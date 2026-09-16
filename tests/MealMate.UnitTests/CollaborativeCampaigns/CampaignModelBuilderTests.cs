using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignModelBuilderTests
{
    private readonly AppDbContext _context;

    public CampaignModelBuilderTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public void Model_ContainsCampaignBundleDurationPrice_WithCorrectTableAndKeys()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignBundleDurationPrice));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CampaignBundleDurationPrices");

        // Key should be Id
        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(p => p.Name).Should().Contain("Id");
    }

    [Fact]
    public void Model_CampaignBundleDurationPrice_HasUniqueIndexOnBundleIdAndDurationDays()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignBundleDurationPrice));
        entityType.Should().NotBeNull();

        var index = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "CampaignBundleId")
                              && i.Properties.Any(p => p.Name == "DurationDays"));

        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Model_CampaignBundleDurationPrice_HasPrecision18Scale3OnAmount()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignBundleDurationPrice));
        entityType.Should().NotBeNull();

        // Owned ReferenceCustomerPrice has ReferenceCustomerPrice_Amount
        var ownedType = _context.Model.FindEntityType(typeof(MealMate.Domain.ValueObjects.Money));
        var amountProp = entityType!.FindProperty("ReferenceCustomerPrice_Amount")
                         ?? entityType.GetNavigations().FirstOrDefault(n => n.Name == "ReferenceCustomerPrice")?
                             .TargetEntityType.FindProperty("Amount");

        amountProp.Should().NotBeNull();
        amountProp!.GetPrecision().Should().Be(18);
        amountProp.GetScale().Should().Be(3);
    }

    [Fact]
    public void Model_CampaignBundle_NavigatesToDurationPrices()
    {
        var bundleType = _context.Model.FindEntityType(typeof(CampaignBundle));
        bundleType.Should().NotBeNull();

        var navigation = bundleType!.FindNavigation(nameof(CampaignBundle.DurationPrices));
        navigation.Should().NotBeNull();
        navigation!.IsCollection.Should().BeTrue();
    }
}
