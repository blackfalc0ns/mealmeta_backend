using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CreateCampaignDraftCommandTests
{
    private readonly AppDbContext _context;
    private readonly CreateCampaignDraftCommandHandler _createHandler;
    private readonly UpdateCampaignDraftCommandHandler _updateHandler;

    public CreateCampaignDraftCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _createHandler = new CreateCampaignDraftCommandHandler(_context);
        _updateHandler = new UpdateCampaignDraftCommandHandler(_context);
    }

    [Fact]
    public async Task Handle_WithValidDraftCommand_CreatesCampaignSuccessfully()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629 \u0627\u0644\u0635\u064A\u0641",
            NameEn: "Summer Campaign",
            StartsAtUtc: now.AddDays(10),
            EndsAtUtc: now.AddDays(30),
            TotalDiscountRate: 0.25m,
            MealMateShareRate: 0.15m,
            RestaurantShareRate: 0.10m,
            FundingMode: CampaignFundingMode.Shared,
            MaxSubscribers: 500,
            EnrollmentOpensAtUtc: now.AddDays(1),
            EnrollmentClosesAtUtc: now.AddDays(8)
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.NameEn.Should().Be("Summer Campaign");
        result.Value.Status.Should().Be(CampaignStatus.Draft);
        result.Value.TotalDiscountRate.Should().Be(0.25m);
        result.Value.MealMateShareRate.Should().Be(0.15m);
        result.Value.RestaurantShareRate.Should().Be(0.10m);

        var entity = await _context.Campaigns.FindAsync(result.Value.Id);
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(CampaignStatus.Draft);
    }

    [Fact]
    public async Task Handle_WhenStartsAtAfterEndsAt_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629",
            NameEn: "Campaign",
            StartsAtUtc: now.AddDays(30),
            EndsAtUtc: now.AddDays(10),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("StartsAtUtc must be before EndsAtUtc");
    }

    [Fact]
    public async Task Handle_WhenEnrollmentClosesAfterCampaignStarts_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629",
            NameEn: "Campaign",
            StartsAtUtc: now.AddDays(10),
            EndsAtUtc: now.AddDays(20),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            EnrollmentOpensAtUtc: now.AddDays(1),
            EnrollmentClosesAtUtc: now.AddDays(15)
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("EnrollmentClosesAtUtc must be before or equal to StartsAtUtc");
    }

    [Fact]
    public async Task Handle_WhenDiscountRatesDoNotSumUp_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629",
            NameEn: "Campaign",
            StartsAtUtc: now.AddDays(5),
            EndsAtUtc: now.AddDays(25),
            TotalDiscountRate: 0.30m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("TotalDiscountRate must equal the sum of MealMateShareRate and RestaurantShareRate");
    }

    [Fact]
    public async Task Handle_WhenDiscountRateInvalid_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629",
            NameEn: "Campaign",
            StartsAtUtc: now.AddDays(5),
            EndsAtUtc: now.AddDays(25),
            TotalDiscountRate: -0.10m,
            MealMateShareRate: -0.10m,
            RestaurantShareRate: 0.00m
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenNameIsEmpty_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var command = new CreateCampaignDraftCommand(
            NameAr: "",
            NameEn: "Summer Campaign",
            StartsAtUtc: now.AddDays(5),
            EndsAtUtc: now.AddDays(25),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Name");
    }

    [Fact]
    public async Task Handle_WithProgramsAndBundles_AttachesBothSuccessfully()
    {
        var now = DateTime.UtcNow;
        var pId1 = Guid.NewGuid();
        var pId2 = Guid.NewGuid();
        var bId1 = Guid.NewGuid();

        var command = new CreateCampaignDraftCommand(
            NameAr: "\u062D\u0645\u0644\u0629",
            NameEn: "Campaign",
            StartsAtUtc: now.AddDays(5),
            EndsAtUtc: now.AddDays(25),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            ProgramIds: new List<Guid> { pId1, pId2 },
            BundleIds: new List<Guid> { bId1 }
        );

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ProgramIds.Should().HaveCount(2);
        result.Value.BundleIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_WhenCampaignIsDraft_UpdatesSuccessfully()
    {
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            NameAr = "\u0642\u062F\u064A\u0645",
            NameEn = "Old",
            Status = CampaignStatus.Draft,
            StartsAtUtc = now.AddDays(10),
            EndsAtUtc = now.AddDays(30),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var updateCommand = new UpdateCampaignDraftCommand(
            CampaignId: campaign.Id,
            NameAr: "\u0645\u062D\u062F\u062B",
            NameEn: "Updated",
            StartsAtUtc: now.AddDays(12),
            EndsAtUtc: now.AddDays(35),
            TotalDiscountRate: 0.30m,
            MealMateShareRate: 0.20m,
            RestaurantShareRate: 0.10m
        );

        var result = await _updateHandler.Handle(updateCommand, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NameEn.Should().Be("Updated");
        result.Value.TotalDiscountRate.Should().Be(0.30m);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.NameEn.Should().Be("Updated");
        updated.TotalDiscountRate.Value.Should().Be(0.30m);
    }

    [Fact]
    public async Task Update_WhenCampaignIsNotDraft_ReturnsFailure()
    {
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            NameAr = "\u0646\u0634\u0637",
            NameEn = "Active",
            Status = CampaignStatus.Active,
            StartsAtUtc = now.AddDays(1),
            EndsAtUtc = now.AddDays(30),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var updateCommand = new UpdateCampaignDraftCommand(
            CampaignId: campaign.Id,
            NameAr: "\u062A\u0639\u062F\u064A\u0644",
            NameEn: "Attempted Update",
            StartsAtUtc: now.AddDays(1),
            EndsAtUtc: now.AddDays(30),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m
        );

        var result = await _updateHandler.Handle(updateCommand, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Only campaigns in Draft status can be edited");
    }

    [Fact]
    public async Task Update_WhenCampaignNotFound_ReturnsFailure()
    {
        var updateCommand = new UpdateCampaignDraftCommand(
            CampaignId: Guid.NewGuid(),
            NameAr: "\u062A\u0639\u062F\u064A\u0644",
            NameEn: "Non Existent",
            StartsAtUtc: DateTime.UtcNow.AddDays(1),
            EndsAtUtc: DateTime.UtcNow.AddDays(30),
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m
        );

        var result = await _updateHandler.Handle(updateCommand, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("not found");
    }
}
