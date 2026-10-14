using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class ReferralAttributionTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly AttributeReferralCommandHandler _handler;

    private static readonly DateTime Now = new(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);

    public ReferralAttributionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.UserType.Returns(UserType.Customer);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);

        _handler = new AttributeReferralCommandHandler(_context, _currentUser, _clock);
    }

    private ReferralCode SeedCode(string code, Guid ownerUserId, bool isActive = true)
    {
        var referral = new ReferralCode
        {
            Code = code,
            OwnerUserId = ownerUserId,
            RewardPercent = Percentage.FromFraction(0.10m),
            IsActive = isActive
        };
        _context.ReferralCodes.Add(referral);
        _context.SaveChanges();
        return referral;
    }

    [Fact]
    public async Task Attribute_WhenCustomerUsesOwnCode_RejectsWithSelfReferral()
    {
        var ownerId = Guid.NewGuid();
        SeedCode("MINE1", ownerId);
        _currentUser.UserId.Returns(ownerId);

        var result = await _handler.Handle(new AttributeReferralCommand("MINE1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Referral.SelfReferral");
        _context.ReferralAttributions.Should().BeEmpty();
    }

    [Fact]
    public async Task Attribute_WhenTwoReferrersRaceForSameCustomer_SecondIsConflict()
    {
        SeedCode("REF-A", Guid.NewGuid());
        SeedCode("REF-B", Guid.NewGuid());

        var first = await _handler.Handle(new AttributeReferralCommand("REF-A"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        var second = await _handler.Handle(new AttributeReferralCommand("REF-B"), CancellationToken.None);

        second.IsFailure.Should().BeTrue();
        second.Error!.Code.Should().Be("Referral.AlreadyAttributed");
    }

    [Fact]
    public async Task Attribute_WhenCodeUnknown_ReturnsNotFoundWithoutPersisting()
    {
        var result = await _handler.Handle(new AttributeReferralCommand("GHOST"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Referral.CodeNotFound");
        _context.ReferralAttributions.Should().BeEmpty();
    }

    [Fact]
    public async Task Attribute_WithValidCode_PersistsImmutableAttribution()
    {
        var referrerId = Guid.NewGuid();
        var referral = SeedCode("VALID1", referrerId);

        var result = await _handler.Handle(new AttributeReferralCommand("valid1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReferrerUserId.Should().Be(referrerId);
        result.Value.ReferredCustomerUserId.Should().Be(_currentUser.UserId!.Value);
        result.Value.AttributedAtUtc.Should().Be(Now);

        var persisted = await _context.ReferralAttributions.SingleAsync();
        persisted.Code.Should().Be("VALID1");
        persisted.ReferralCodeId.Should().Be(referral.Id);
    }

    [Fact]
    public async Task Summary_ReturnsOwnCodeAndCounts_WithoutOtherPeoplesData()
    {
        var referrerId = Guid.NewGuid();
        SeedCode("SHARE1", referrerId);
        _currentUser.UserId.Returns(referrerId);

        _context.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "SHARE1",
            ReferrerUserId = referrerId,
            ReferredCustomerUserId = Guid.NewGuid(),
            AttributedAtUtc = Now
        });
        _context.SaveChanges();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://mealmate.app" })
            .Build();

        var handler = new GetReferralSummaryQueryHandler(_context, _currentUser, configuration);
        var result = await handler.Handle(new GetReferralSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Code.Should().Be("SHARE1");
        result.Value.ShareUrl.Should().Be("https://mealmate.app/r/SHARE1");
        result.Value.AttributedCount.Should().Be(1);
        result.Value.HasRewardPolicy.Should().BeTrue();
        result.Value.RewardPercent.Should().Be(0.10m);
    }
}
