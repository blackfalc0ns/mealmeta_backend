using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class SubscriptionPurchaseAndSnapshotTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task CreateSubscription_CreatesPendingPaymentSubscription_AndImmutableSnapshot()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var prog = new Program { Code = "PROG_SUB", NameAr = "برنامج اشتراك", NameEn = "Sub Program", IsActive = true };
        var bnd = new Bundle { ProgramId = prog.Id, Code = "BND_SUB", NameAr = "باقة اشتراك", NameEn = "Sub Bundle", IsActive = true, MealsPerDay = 2 };
        db.Programs.Add(prog);
        db.Bundles.Add(bnd);
        prog.Status = ProgramStatus.Active;
        bnd.Status = ProgramStatus.Active;
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing { RestaurantId = Guid.NewGuid(), ProgramId = prog.Id, BundleId = bnd.Id, ReferencePrice26 = new MealMate.Domain.ValueObjects.Money(130m, "KWD"), ContractedSupplyPrice26 = new MealMate.Domain.ValueObjects.Money(100m, "KWD"), LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1) });
        db.PricingRuleVersions.Add(new PricingRuleVersion { RMax = MealMate.Domain.ValueObjects.Percentage.FromFraction(.3m), RMin = MealMate.Domain.ValueObjects.Percentage.FromFraction(.05m), DShort = 6, DLong = 26, Gamma = 1m, RoundingStepKwd = .1m, IsActive = true });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new SubscriptionCommandHandlers(db, audit, clock);
        var custId = Guid.NewGuid();
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var command = new CreateSubscriptionCommand(
            CustomerUserId: custId,
            ProgramId: prog.Id,
            BundleId: bnd.Id,
            SelectedTier: RestaurantTier.Platinum,
            DurationDays: 26,
            StartDate: startDate,
            DeliveryAddress: "Kuwait City, Block 1");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionStatus.PendingPayment);
        result.Value.DurationDays.Should().Be(26);
        result.Value.Snapshots.Should().HaveCount(1);
        result.Value.Snapshots[0].SnapshotJson.Should().Contain("FinalCustomerPrice");
        result.Value.Operations.Should().HaveCount(1);
        result.Value.Operations[0].OperationType.Should().Be("CREATE");
    }

    [Fact]
    public async Task ConfirmPayment_TransitionsStatusToScheduled_WhenStartDateIsInFuture()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var prog = new Program { Code = "PROG_PAY", NameAr = "برنامج دفع", NameEn = "Pay Program", IsActive = true };
        var bnd = new Bundle { ProgramId = prog.Id, Code = "BND_PAY", NameAr = "باقة دفع", NameEn = "Pay Bundle", IsActive = true, MealsPerDay = 2 };
        db.Programs.Add(prog);
        db.Bundles.Add(bnd);
        prog.Status = ProgramStatus.Active;
        bnd.Status = ProgramStatus.Active;
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing { RestaurantId = Guid.NewGuid(), ProgramId = prog.Id, BundleId = bnd.Id, ReferencePrice26 = new MealMate.Domain.ValueObjects.Money(130m, "KWD"), ContractedSupplyPrice26 = new MealMate.Domain.ValueObjects.Money(100m, "KWD"), LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1) });
        db.PricingRuleVersions.Add(new PricingRuleVersion { RMax = MealMate.Domain.ValueObjects.Percentage.FromFraction(.3m), RMin = MealMate.Domain.ValueObjects.Percentage.FromFraction(.05m), DShort = 6, DLong = 26, Gamma = 1m, RoundingStepKwd = .1m, IsActive = true });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new SubscriptionCommandHandlers(db, audit, clock);
        var custId = Guid.NewGuid();
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        var createCmd = new CreateSubscriptionCommand(custId, prog.Id, bnd.Id, RestaurantTier.Platinum, 26, futureDate);
        var createdResult = await handler.Handle(createCmd, CancellationToken.None);

        var confirmCmd = new ConfirmSubscriptionPaymentCommand(createdResult.Value.Id, "PAY-REF-998877");
        var confirmResult = await handler.Handle(confirmCmd, CancellationToken.None);

        confirmResult.IsSuccess.Should().BeTrue();
        confirmResult.Value.Status.Should().Be(SubscriptionStatus.Scheduled);
        confirmResult.Value.Operations.Should().HaveCount(2);
        confirmResult.Value.Operations.Should().Contain(x => x.OperationType == "PAYMENT_CONFIRMED");

        var duplicateResult = await handler.Handle(confirmCmd, CancellationToken.None);
        duplicateResult.IsSuccess.Should().BeTrue();
        duplicateResult.Value.Operations.Should().HaveCount(2);
        (await db.PaymentWebhookReceipts.CountAsync()).Should().Be(1);
    }
}
