using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class FamilyAndOffersTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task AddFamilyMember_Succeeds_AndEnforcesLimitOfFourMembers()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var primarySub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(26)),
            PaidAmount = new Money(100.000m, "KWD"),
        };
        db.Subscriptions.Add(primarySub);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new FamilyAndOffersHandlers(db, audit, clock);

        // Add 4 members
        for (int i = 1; i <= 4; i++)
        {
            var addCmd = new AddFamilyMemberCommand(primarySub.Id, Guid.NewGuid(), $"Member {i}", "Family");
            var res = await handler.Handle(addCmd, CancellationToken.None);
            res.IsSuccess.Should().BeTrue();
        }

        // Attempt 5th member -> Should fail with limit error
        var fifthCmd = new AddFamilyMemberCommand(primarySub.Id, Guid.NewGuid(), "Member 5", "Family");
        var fifthRes = await handler.Handle(fifthCmd, CancellationToken.None);

        fifthRes.IsFailure.Should().BeTrue();
        fifthRes.Error.Description.Should().Contain("limit reached");
    }

    [Fact]
    public async Task ValidateDiscountCode_CalculatesPromoAndReferralDiscountsCorrectly()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        db.PromoCodes.Add(new PromoCode
        {
            Code = "SAVE20",
            DiscountPercent = Percentage.FromFraction(0.20m),
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10),
            IsActive = true,
        });

        db.ReferralCodes.Add(new ReferralCode
        {
            Code = "REF10",
            OwnerUserId = Guid.NewGuid(),
            RewardPercent = Percentage.FromFraction(0.10m),
            IsActive = true,
        });

        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new FamilyAndOffersHandlers(db, audit, clock);

        // Validate promo code (20% of 100 KWD = 20 KWD)
        var promoQuery = new ValidateDiscountCodeQuery("SAVE20", TotalAmountKwd: 100.000m);
        var promoRes = await handler.Handle(promoQuery, CancellationToken.None);

        promoRes.IsSuccess.Should().BeTrue();
        promoRes.Value.IsValid.Should().BeTrue();
        promoRes.Value.CalculatedDiscountKwd.Should().Be(20.000m);

        // Validate referral code (10% of 100 KWD = 10 KWD)
        var refQuery = new ValidateDiscountCodeQuery("REF10", TotalAmountKwd: 100.000m);
        var refRes = await handler.Handle(refQuery, CancellationToken.None);

        refRes.IsSuccess.Should().BeTrue();
        refRes.Value.IsValid.Should().BeTrue();
        refRes.Value.CalculatedDiscountKwd.Should().Be(10.000m);
    }
}
