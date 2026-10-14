using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignListAndDetailsQueriesTests
{
    private readonly AppDbContext _context;

    public CampaignListAndDetailsQueriesTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
    }

    [Fact]
    public async Task GetCampaignsListQuery_ReturnsPagedAndFilteredCampaigns()
    {
        var c1 = new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-5),
            EndsAtUtc = DateTime.UtcNow.AddDays(20),
            TotalDiscountRate = Percentage.FromFraction(0.15m),
            MealMateShareRate = Percentage.FromFraction(0.08m),
            RestaurantShareRate = Percentage.FromFraction(0.07m),
            FundingMode = CampaignFundingMode.Shared
        };
        var c2 = new Campaign
        {
            NameAr = "حملة الشتاء",
            NameEn = "Winter Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(10),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            FundingMode = CampaignFundingMode.Shared
        };
        _context.Campaigns.AddRange(c1, c2);
        await _context.SaveChangesAsync();

        var handler = new GetCampaignsListQueryHandler(_context);

        // 1. Fetch all
        var allResult = await handler.Handle(new GetCampaignsListQuery(), CancellationToken.None);
        allResult.IsSuccess.Should().BeTrue();
        allResult.Value!.TotalCount.Should().Be(2);

        // 2. Filter by status
        var activeResult = await handler.Handle(new GetCampaignsListQuery(Status: CampaignStatus.Active), CancellationToken.None);
        activeResult.IsSuccess.Should().BeTrue();
        activeResult.Value!.TotalCount.Should().Be(1);
        activeResult.Value.Items[0].NameAr.Should().Be("حملة الصيف");

        // 3. Filter by search
        var searchResult = await handler.Handle(new GetCampaignsListQuery(Search: "winter"), CancellationToken.None);
        searchResult.IsSuccess.Should().BeTrue();
        searchResult.Value!.TotalCount.Should().Be(1);
        searchResult.Value.Items[0].NameEn.Should().Be("Winter Campaign");
    }

    [Fact]
    public async Task GetCampaignByIdQuery_ReturnsCampaign_WhenFound()
    {
        var c = new Campaign
        {
            NameAr = "حملة تجريبية",
            NameEn = "Test Campaign",
            Status = CampaignStatus.OpenForJoining,
            StartsAtUtc = DateTime.UtcNow.AddDays(2),
            EndsAtUtc = DateTime.UtcNow.AddDays(15),
            TotalDiscountRate = Percentage.FromFraction(0.10m),
            MealMateShareRate = Percentage.FromFraction(0.05m),
            RestaurantShareRate = Percentage.FromFraction(0.05m),
            FundingMode = CampaignFundingMode.Shared
        };
        _context.Campaigns.Add(c);
        await _context.SaveChangesAsync();

        var handler = new GetCampaignByIdQueryHandler(_context);

        var result = await handler.Handle(new GetCampaignByIdQuery(c.Id), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value!.NameAr.Should().Be("حملة تجريبية");
        result.Value.Status.Should().Be(CampaignStatus.OpenForJoining);
    }

    [Fact]
    public async Task GetCampaignByIdQuery_ReturnsNotFound_WhenDoesNotExist()
    {
        var handler = new GetCampaignByIdQueryHandler(_context);

        var result = await handler.Handle(new GetCampaignByIdQuery(Guid.NewGuid()), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetCampaignsListQuery_And_GetCampaignByIdQuery_WithSavedReviewWindow_ReturnsBothDates()
    {
        var reviewStarts = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc);

        var c = new Campaign
        {
            NameAr = "حملة بنافذة مراجعة",
            NameEn = "Campaign With Review Window",
            Status = CampaignStatus.UnderReview,
            StartsAtUtc = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc),
            TotalDiscountRate = Percentage.FromFraction(0.15m),
            MealMateShareRate = Percentage.FromFraction(0.08m),
            RestaurantShareRate = Percentage.FromFraction(0.07m),
            FundingMode = CampaignFundingMode.Shared,
            ReviewStartsAtUtc = reviewStarts,
            ReviewEndsAtUtc = reviewEnds
        };
        _context.Campaigns.Add(c);
        await _context.SaveChangesAsync();

        var listHandler = new GetCampaignsListQueryHandler(_context);
        var listResult = await listHandler.Handle(new GetCampaignsListQuery(), CancellationToken.None);

        listResult.IsSuccess.Should().BeTrue();
        var listItem = listResult.Value!.Items.First(x => x.Id == c.Id);
        listItem.ReviewStartsAtUtc.Should().Be(reviewStarts);
        listItem.ReviewEndsAtUtc.Should().Be(reviewEnds);

        var byIdHandler = new GetCampaignByIdQueryHandler(_context);
        var byIdResult = await byIdHandler.Handle(new GetCampaignByIdQuery(c.Id), CancellationToken.None);

        byIdResult.IsSuccess.Should().BeTrue();
        byIdResult.Value!.ReviewStartsAtUtc.Should().Be(reviewStarts);
        byIdResult.Value.ReviewEndsAtUtc.Should().Be(reviewEnds);
    }

    [Fact]
    public async Task GetCampaignsListQuery_And_GetCampaignByIdQuery_ForLegacyCampaignWithoutReviewWindow_ReturnsNullDatesWithoutFabrication()
    {
        var legacy = new Campaign
        {
            NameAr = "حملة قديمة",
            NameEn = "Legacy Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            FundingMode = CampaignFundingMode.Shared,
            ReviewStartsAtUtc = null,
            ReviewEndsAtUtc = null
        };
        _context.Campaigns.Add(legacy);
        await _context.SaveChangesAsync();

        var listHandler = new GetCampaignsListQueryHandler(_context);
        var listResult = await listHandler.Handle(new GetCampaignsListQuery(), CancellationToken.None);

        listResult.IsSuccess.Should().BeTrue();
        var listItem = listResult.Value!.Items.First(x => x.Id == legacy.Id);
        listItem.ReviewStartsAtUtc.Should().BeNull();
        listItem.ReviewEndsAtUtc.Should().BeNull();

        var byIdHandler = new GetCampaignByIdQueryHandler(_context);
        var byIdResult = await byIdHandler.Handle(new GetCampaignByIdQuery(legacy.Id), CancellationToken.None);

        byIdResult.IsSuccess.Should().BeTrue();
        byIdResult.Value!.ReviewStartsAtUtc.Should().BeNull();
        byIdResult.Value.ReviewEndsAtUtc.Should().BeNull();
    }
}
