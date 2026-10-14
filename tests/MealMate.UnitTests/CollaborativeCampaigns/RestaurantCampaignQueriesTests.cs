using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public sealed class RestaurantCampaignQueriesTests
{
    [Fact]
    public async Task Invitations_ReturnOnlyPersistedInvitedEnrollmentForActingRestaurant()
    {
        await using var context = CreateContext();
        var actorRestaurantId = Guid.NewGuid();
        var otherRestaurantId = Guid.NewGuid();
        var invited = CampaignWithStatus(CampaignStatus.OpenForJoining);
        var notInvited = CampaignWithStatus(CampaignStatus.OpenForJoining);
        context.Campaigns.AddRange(invited, notInvited);
        var enrollment = new CampaignRestaurantEnrollment
        {
            CampaignId = invited.Id,
            RestaurantId = actorRestaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Invited,
            RequestedAtUtc = DateTime.UtcNow
        };
        context.CampaignRestaurantEnrollments.AddRange(
            enrollment,
            new CampaignRestaurantEnrollment
            {
                CampaignId = invited.Id,
                RestaurantId = otherRestaurantId,
                ParticipationStatus = CampaignRestaurantParticipationStatus.Invited,
                RequestedAtUtc = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Restaurant);
        user.RestaurantId.Returns(actorRestaurantId);

        var result = await new GetRestaurantCampaignInvitationsQueryHandler(context, user)
            .Handle(new GetRestaurantCampaignInvitationsQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle();
        result.Value[0].Id.Should().Be(enrollment.Id);
        result.Value[0].CampaignId.Should().Be(invited.Id);
        result.Value[0].DurationPrices.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveCampaigns_RequireEnrollment_AndReportUnavailableValuesWithoutLedger()
    {
        await using var context = CreateContext();
        var actorRestaurantId = Guid.NewGuid();
        var active = CampaignWithStatus(CampaignStatus.Active);
        var unenrolled = CampaignWithStatus(CampaignStatus.Active);
        context.Campaigns.AddRange(active, unenrolled);
        var enrollment = new CampaignRestaurantEnrollment
        {
            CampaignId = active.Id,
            RestaurantId = actorRestaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            RequestedAtUtc = DateTime.UtcNow
        };
        context.CampaignRestaurantEnrollments.AddRange(
            enrollment,
            new CampaignRestaurantEnrollment
            {
                CampaignId = active.Id,
                RestaurantId = actorRestaurantId,
                ParticipationStatus = CampaignRestaurantParticipationStatus.Declined,
                Status = VerificationStatus.Approved,
                RequestedAtUtc = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Restaurant);
        user.RestaurantId.Returns(actorRestaurantId);

        var result = await new GetRestaurantActiveCampaignsQueryHandler(context, user)
            .Handle(new GetRestaurantActiveCampaignsQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle();
        var dto = result.Value[0];
        dto.Id.Should().Be(enrollment.Id);
        dto.CampaignId.Should().Be(active.Id);
        dto.DedicatedDailyCapacity.Should().BeNull();
        dto.ReservedDailyBoxes.Should().BeNull();
        dto.GrossSettlementEarned.Should().BeNull();
        dto.NetSettlementEarned.Should().BeNull();
        dto.PendingPayout.Should().BeNull();
        dto.PlatformWholesaleSpread.Should().BeNull();
        dto.Currency.Should().BeNull();
    }

    [Fact]
    public async Task ActiveCampaigns_NetSettlementsSubtractReversalsAndDoNotUseGlobalCustomerPayments()
    {
        await using var context = CreateContext();
        var actorRestaurantId = Guid.NewGuid();
        var campaign = CampaignWithStatus(CampaignStatus.Active);
        context.Campaigns.Add(campaign);
        var enrollment = new CampaignRestaurantEnrollment
        {
            CampaignId = campaign.Id,
            RestaurantId = actorRestaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            RequestedAtUtc = DateTime.UtcNow
        };
        context.CampaignRestaurantEnrollments.Add(enrollment);
        context.CampaignFinancialLedgers.AddRange(
            new CampaignFinancialLedger
            {
                CampaignId = campaign.Id,
                RestaurantId = actorRestaurantId,
                EntryType = CampaignLedgerEntryType.RestaurantSettlement,
                Amount = 10m,
                Currency = "SAR",
                Description = "settlement"
            },
            new CampaignFinancialLedger
            {
                CampaignId = campaign.Id,
                RestaurantId = actorRestaurantId,
                EntryType = CampaignLedgerEntryType.RestaurantSettlementReversal,
                Amount = 2m,
                Currency = "SAR",
                Description = "reversal"
            },
            new CampaignFinancialLedger
            {
                CampaignId = campaign.Id,
                EntryType = CampaignLedgerEntryType.CustomerPayment,
                Amount = 500m,
                Currency = "SAR",
                Description = "platform-wide customer payment"
            });
        await context.SaveChangesAsync();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Restaurant);
        user.RestaurantId.Returns(actorRestaurantId);

        var result = await new GetRestaurantActiveCampaignsQueryHandler(context, user)
            .Handle(new GetRestaurantActiveCampaignsQuery(), CancellationToken.None);

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.GrossSettlementEarned.Should().Be(10m);
        dto.NetSettlementEarned.Should().Be(8m);
        dto.Currency.Should().Be("SAR");
    }

    [Fact]
    public async Task ActiveCampaigns_WithMixedSettlementCurrenciesDoesNotAddDifferentCurrencies()
    {
        await using var context = CreateContext();
        var actorRestaurantId = Guid.NewGuid();
        var campaign = CampaignWithStatus(CampaignStatus.Active);
        context.Campaigns.Add(campaign);
        context.CampaignRestaurantEnrollments.Add(new CampaignRestaurantEnrollment
        {
            CampaignId = campaign.Id,
            RestaurantId = actorRestaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            RequestedAtUtc = DateTime.UtcNow
        });
        context.CampaignFinancialLedgers.AddRange(
            new CampaignFinancialLedger { CampaignId = campaign.Id, RestaurantId = actorRestaurantId, EntryType = CampaignLedgerEntryType.RestaurantSettlement, Amount = 10m, Currency = "KWD", Description = "settlement" },
            new CampaignFinancialLedger { CampaignId = campaign.Id, RestaurantId = actorRestaurantId, EntryType = CampaignLedgerEntryType.RestaurantSettlement, Amount = 20m, Currency = "SAR", Description = "settlement" });
        await context.SaveChangesAsync();
        var user = RestaurantUser(actorRestaurantId);

        var result = await new GetRestaurantActiveCampaignsQueryHandler(context, user)
            .Handle(new GetRestaurantActiveCampaignsQuery(), CancellationToken.None);

        var dto = result.Value.Should().ContainSingle().Subject;
        dto.GrossSettlementEarned.Should().BeNull();
        dto.NetSettlementEarned.Should().BeNull();
        dto.Currency.Should().BeNull();
    }

    [Fact]
    public async Task QueryWithAnotherRestaurantIdReturnsNoData()
    {
        await using var context = CreateContext();
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Restaurant);
        user.RestaurantId.Returns(Guid.NewGuid());
        var requestedId = Guid.NewGuid();

        var active = await new GetRestaurantActiveCampaignsQueryHandler(context, user)
            .Handle(new GetRestaurantActiveCampaignsQuery(requestedId), CancellationToken.None);
        var invitations = await new GetRestaurantCampaignInvitationsQueryHandler(context, user)
            .Handle(new GetRestaurantCampaignInvitationsQuery(requestedId), CancellationToken.None);

        active.Value.Should().BeEmpty();
        invitations.Value.Should().BeEmpty();

        user.IsAuthenticated.Returns(false);
        var unauthenticated = await new GetRestaurantActiveCampaignsQueryHandler(context, user)
            .Handle(new GetRestaurantActiveCampaignsQuery(), CancellationToken.None);
        var unauthenticatedInvitations = await new GetRestaurantCampaignInvitationsQueryHandler(context, user)
            .Handle(new GetRestaurantCampaignInvitationsQuery(), CancellationToken.None);
        unauthenticated.Value.Should().BeEmpty();
        unauthenticatedInvitations.Value.Should().BeEmpty();
    }

    private static Campaign CampaignWithStatus(CampaignStatus status) => new()
    {
        NameAr = "حملة",
        NameEn = "Campaign",
        Status = status,
        StartsAtUtc = DateTime.UtcNow,
        EndsAtUtc = DateTime.UtcNow.AddDays(1),
        TotalDiscountRate = Percentage.FromFraction(0.20m),
        MealMateShareRate = Percentage.FromFraction(0.10m),
        RestaurantShareRate = Percentage.FromFraction(0.10m)
    };

    private static ICurrentUser RestaurantUser(Guid restaurantId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserType.Returns(UserType.Restaurant);
        user.RestaurantId.Returns(restaurantId);
        return user;
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
