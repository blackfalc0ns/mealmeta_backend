using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignAuditLogTests
{
    private readonly AppDbContext _context;

    public CampaignAuditLogTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public void Model_ContainsCampaignAuditLog_WithCorrectTable()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignAuditLog));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be("CampaignAuditLogs");

        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.Should().NotBeNull();
        primaryKey!.Properties.Select(p => p.Name).Should().Contain("Id");
    }

    [Fact]
    public void Model_CampaignAuditLog_HasRequiredAuditIndexes()
    {
        var entityType = _context.Model.FindEntityType(typeof(CampaignAuditLog));
        entityType.Should().NotBeNull();

        var campaignIndex = entityType!.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "CampaignId"));
        campaignIndex.Should().NotBeNull();

        var actorIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "ActorId"));
        actorIndex.Should().NotBeNull();

        var actionIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Action"));
        actionIndex.Should().NotBeNull();

        var createdAtIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "CreatedAtUtc"));
        createdAtIndex.Should().NotBeNull();
    }
}
