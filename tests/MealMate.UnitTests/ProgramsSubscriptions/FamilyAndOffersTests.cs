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

        var handler = new FamilyAndOffersHandlers(db, audit, clock, Substitute.For<ICurrentUser>());

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

        var handler = new FamilyAndOffersHandlers(db, audit, clock, Substitute.For<ICurrentUser>());

        // Public validation returns the code RULE, not an amount derived from a client total.
        var promoQuery = new ValidateDiscountCodeQuery("SAVE20", TotalAmountKwd: 100.000m);
        var promoRes = await handler.Handle(promoQuery, CancellationToken.None);

        promoRes.IsSuccess.Should().BeTrue();
        promoRes.Value.IsValid.Should().BeTrue();
        promoRes.Value.DiscountPercent.Should().Be(0.20m);
        promoRes.Value.CalculatedDiscountKwd.Should().Be(0m);
        promoRes.Value.EligibilityCode.Should().Be("rule_only_amount_set_at_quote");

        // Referral code with an explicit 10% reward policy returns the rule, never a computed amount.
        var refQuery = new ValidateDiscountCodeQuery("REF10", TotalAmountKwd: 100.000m);
        var refRes = await handler.Handle(refQuery, CancellationToken.None);

        refRes.IsSuccess.Should().BeTrue();
        refRes.Value.IsValid.Should().BeTrue();
        refRes.Value.DiscountPercent.Should().Be(0.10m);
        refRes.Value.CalculatedDiscountKwd.Should().Be(0m);
    }

    [Fact]
    public async Task ValidateDiscountCode_TargetedCustomerCannotBeImpersonated()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var target = Guid.NewGuid();
        db.PromoCodes.Add(new PromoCode
        {
            Code = "PRIVATE10", DiscountAmount = new Money(10m, "KWD"), IsActive = true,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(-1),
            TargetingJson = $"{{\"CustomerUserId\":\"{target}\"}}"
        });
        await db.SaveChangesAsync();
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(Guid.NewGuid());
        var handler = new FamilyAndOffersHandlers(db, Substitute.For<IAuditLogger>(), Substitute.For<IDateTimeProvider>(), current);

        var result = await handler.Handle(new ValidateDiscountCodeQuery("PRIVATE10", CustomerUserId: target), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeFalse();
        result.Value.FailureReason.Should().NotBeNullOrWhiteSpace();
    }
}
