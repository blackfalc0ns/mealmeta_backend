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

    [Fact]
    public async Task ListSubscriptions_EnrichesCustomerNameAreaAndUsedDays_ForIndividualRows()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var today = new DateOnly(2026, 8, 14);
        var country = new MealMate.Domain.Foundation.Country
        {
            Code = "KW",
            NameAr = "الكويت",
            NameEn = "Kuwait",
            CurrencyCode = "KWD",
            DialCode = "+965",
            FlagEmoji = "🇰🇼",
        };
        var region = new MealMate.Domain.Foundation.Region
        {
            CountryId = country.Id,
            Code = "KWI",
            NameAr = "مدينة الكويت",
            NameEn = "Kuwait City",
        };
        var prog = new Program { Code = "PROG_LIST", NameAr = "رشاقة", NameEn = "Fitness", IsActive = true, Status = ProgramStatus.Active };
        var bnd = new Bundle { ProgramId = prog.Id, Code = "BND_LIST", NameAr = "باقة 2", NameEn = "Bundle 2", IsActive = true, Status = ProgramStatus.Active, MealsPerDay = 2 };
        var customerId = Guid.NewGuid();
        var familyCustomerId = Guid.NewGuid();
        var individual = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = prog.Id,
            BundleId = bnd.Id,
            SelectedTier = RestaurantTier.Elite,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = today.AddDays(-10),
            EndDate = today.AddDays(16),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(49.9m, "KWD"),
            RegionId = region.Id,
            CreatedAtUtc = new DateTime(2026, 8, 4, 8, 0, 0, DateTimeKind.Utc),
        };
        var familyPrimary = new Subscription
        {
            CustomerUserId = familyCustomerId,
            ProgramId = prog.Id,
            BundleId = bnd.Id,
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = today.AddDays(-3),
            EndDate = today.AddDays(23),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(79.9m, "KWD"),
            CreatedAtUtc = new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc),
        };
        db.Countries.Add(country);
        db.Regions.Add(region);
        db.Programs.Add(prog);
        db.Bundles.Add(bnd);
        db.Subscriptions.AddRange(individual, familyPrimary);
        db.FamilySubscriptions.Add(new FamilySubscription
        {
            PrimarySubscriptionId = familyPrimary.Id,
            MemberCustomerUserId = Guid.NewGuid(),
            DisplayName = "ابن",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var ids = call.Arg<IReadOnlyCollection<Guid>>();
                return ids.Select(id => new UserIdentityDto(id, "سارة أحمد", "sara@test.com", "90001111")).ToList();
            });

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var handler = new SubscriptionCommandHandlers(db, audit, clock, users: users);
        var result = await handler.Handle(new GetSubscriptionsListQuery(IsFamily: false, Page: 1, PageSize: 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        var row = result.Value.Items[0];
        row.Id.Should().Be(individual.Id);
        row.CustomerFullName.Should().Be("سارة أحمد");
        row.CustomerPhone.Should().Be("90001111");
        row.RegionNameAr.Should().Be("مدينة الكويت");
        row.RegionNameEn.Should().Be("Kuwait City");
        row.Status.Should().Be(SubscriptionStatus.Active);
        row.UsedDays.Should().BeGreaterThan(0);
        row.IsFamily.Should().BeFalse();

        var stats = await handler.Handle(new GetSubscriptionListStatsQuery(), CancellationToken.None);
        stats.IsSuccess.Should().BeTrue();
        stats.Value.IndividualActive.Should().Be(1);
        stats.Value.FamilyGroups.Should().Be(1);
        stats.Value.FamilyMembers.Should().Be(1);
    }
}
