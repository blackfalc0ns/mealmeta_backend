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

    [Fact]
    public async Task ValidateDiscountCode_ReturnsBilingualMessages_ForFailures()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var now = DateTime.UtcNow;
        db.PromoCodes.Add(new PromoCode
        {
            Code = "INACTIVE_PROMO",
            DiscountPercent = Percentage.FromFraction(0.1m),
            IsActive = false,
            StartsAtUtc = now.AddDays(-5),
        });
        db.PromoCodes.Add(new PromoCode
        {
            Code = "EXPIRED_PROMO",
            DiscountPercent = Percentage.FromFraction(0.1m),
            IsActive = true,
            StartsAtUtc = now.AddDays(-10),
            EndsAtUtc = now.AddDays(-1),
        });
        db.ReferralCodes.Add(new ReferralCode
        {
            Code = "INACTIVE_REF",
            OwnerUserId = Guid.NewGuid(),
            RewardPercent = Percentage.FromFraction(0.1m),
            IsActive = false,
        });
        db.PromoCodes.Add(new PromoCode
        {
            Code = "VALID_PROMO",
            DiscountPercent = Percentage.FromFraction(0.15m),
            IsActive = true,
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(5),
        });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var handler = new FamilyAndOffersHandlers(db, Substitute.For<IAuditLogger>(), clock, Substitute.For<ICurrentUser>());

        // 1. Not Found
        var notFound = await handler.Handle(new ValidateDiscountCodeQuery("NONEXISTENT"), CancellationToken.None);
        notFound.Value.IsValid.Should().BeFalse();
        notFound.Value.FailureMessageAr.Should().Be("كود الخصم غير موجود");
        notFound.Value.FailureMessageEn.Should().Be("This coupon code does not exist");

        // 2. Inactive promo
        var inactivePromo = await handler.Handle(new ValidateDiscountCodeQuery("INACTIVE_PROMO"), CancellationToken.None);
        inactivePromo.Value.IsValid.Should().BeFalse();
        inactivePromo.Value.FailureMessageAr.Should().Be("كود الخصم غير مُفعّل حاليًا");
        inactivePromo.Value.FailureMessageEn.Should().Be("This coupon code is not active");

        // 3. Expired promo
        var expiredPromo = await handler.Handle(new ValidateDiscountCodeQuery("EXPIRED_PROMO"), CancellationToken.None);
        expiredPromo.Value.IsValid.Should().BeFalse();
        expiredPromo.Value.FailureMessageAr.Should().Be("انتهت صلاحية كود الخصم");
        expiredPromo.Value.FailureMessageEn.Should().Be("This coupon code has expired");

        // 4. Inactive referral
        var inactiveRef = await handler.Handle(new ValidateDiscountCodeQuery("INACTIVE_REF"), CancellationToken.None);
        inactiveRef.Value.IsValid.Should().BeFalse();
        inactiveRef.Value.FailureMessageAr.Should().Be("كود الإحالة غير مُفعّل حاليًا");
        inactiveRef.Value.FailureMessageEn.Should().Be("This referral code is not active");

        // 5. Valid promo: Failure messages should be null
        var validPromo = await handler.Handle(new ValidateDiscountCodeQuery("VALID_PROMO"), CancellationToken.None);
        validPromo.Value.IsValid.Should().BeTrue();
        validPromo.Value.FailureMessageAr.Should().BeNull();
        validPromo.Value.FailureMessageEn.Should().BeNull();
    }
}
