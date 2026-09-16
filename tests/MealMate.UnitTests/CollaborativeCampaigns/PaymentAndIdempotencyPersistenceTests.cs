using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class PaymentAndIdempotencyPersistenceTests
{
    private readonly AppDbContext _context;

    public PaymentAndIdempotencyPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public void Model_ContainsCampaignPaymentTransaction_WithCorrectTableAndKeys()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignPaymentTransaction));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CampaignPaymentTransactions");

        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(p => p.Name).Should().Contain("Id");
    }

    [Fact]
    public void Model_CampaignPaymentTransaction_HasUniqueIndexOnProviderAndTransactionId()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignPaymentTransaction));
        entityType.Should().NotBeNull();

        var index = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "PaymentProvider")
                              && i.Properties.Any(p => p.Name == "ProviderTransactionId"));

        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Model_CampaignPaymentTransaction_HasAmountPrecision18Scale3()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignPaymentTransaction));
        entityType.Should().NotBeNull();

        var amountProp = entityType!.FindProperty("Amount");
        amountProp.Should().NotBeNull();
        amountProp!.GetPrecision().Should().Be(18);
        amountProp.GetScale().Should().Be(3);
    }

    [Fact]
    public void Model_IdempotencyRecord_ContainsScopeAndCompoundIndex()
    {
        var entityType = _context.Model.FindEntityType(typeof(MealMate.Domain.Foundation.IdempotencyRecord));
        entityType.Should().NotBeNull();

        var scopeProp = entityType!.FindProperty("Scope");
        scopeProp.Should().NotBeNull();

        var index = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Scope")
                              && i.Properties.Any(p => p.Name == "Key"));

        index.Should().NotBeNull();
        index!.IsUnique.Should().BeTrue();
    }
}
