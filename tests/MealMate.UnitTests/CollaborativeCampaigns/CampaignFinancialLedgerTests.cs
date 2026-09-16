using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignFinancialLedgerTests
{
    private readonly AppDbContext _context;

    public CampaignFinancialLedgerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public void Model_ContainsCampaignFinancialLedger_WithCorrectTable()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignFinancialLedger));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CampaignFinancialLedgers");
    }

    [Fact]
    public void Model_CampaignFinancialLedger_HasPrecision18Scale3OnAmount()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignFinancialLedger));
        entityType.Should().NotBeNull();

        var amountProp = entityType!.FindProperty("Amount");
        amountProp.Should().NotBeNull();
        amountProp!.GetPrecision().Should().Be(18);
        amountProp.GetScale().Should().Be(3);
    }

    [Fact]
    public void Model_CampaignFinancialLedger_HasIndexOnJournalIdAndCampaignId()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignFinancialLedger));
        entityType.Should().NotBeNull();

        var journalIndex = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "JournalId"));
        journalIndex.Should().NotBeNull();

        var campaignIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "CampaignId"));
        campaignIndex.Should().NotBeNull();
    }
}
