using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyDeliveryAmendmentResolutionTests
{
    [Fact]
    public async Task Accepted_amendment_drives_affected_day_while_protected_day_keeps_paid_snapshot()
    {
        await using var db = NewDb();
        var subscription = new Subscription { FamilyGroupId = Guid.NewGuid(), FamilyMemberId = Guid.NewGuid(),
            CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active, StartDate = new DateOnly(2026, 10, 9), EndDate = new DateOnly(2026, 10, 10), DurationDays = 2 };
        var affected = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 10, 9), Status = SubscriptionDayStatus.Open };
        var protectedDay = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 10, 10), Status = SubscriptionDayStatus.ConfirmedByRestaurant };
        var amendment = new FamilySubscriptionDeliveryAmendment { SubscriptionId = subscription.Id, Version = 1,
            ActorUserId = Guid.NewGuid(), EffectiveFromDate = affected.ServiceDate, AddressId = Guid.NewGuid(),
            AddressLabel = "B", AddressLine = "Accepted address B", RegionId = Guid.NewGuid(),
            AffectedDayIdsJson = System.Text.Json.JsonSerializer.Serialize(new[] { affected.Id }), CreatedAtUtc = DateTime.UtcNow };
        affected.SourceFamilyDeliveryAmendmentId = amendment.Id;
        var batch = new FamilyCheckoutBatch { FamilyGroupId = subscription.FamilyGroupId!.Value,
            PayerCustomerUserId = subscription.CustomerUserId, Status = FamilyCheckoutBatchStatus.Paid,
            IdempotencyKey = "paid", FinalPrice = 90, CurrencyCode = "KWD" };
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, Batch = batch, SubscriptionId = subscription.Id,
            FamilyMemberId = subscription.FamilyMemberId!.Value, CheckoutSelected = true,
            DeliveryAddressId = Guid.NewGuid(), AddressLabel = "A", AddressLine = "Original address A", RegionId = Guid.NewGuid(),
            MenuPlanVersionsJson = "[]" });
        db.AddRange(subscription, affected, protectedDay, amendment, batch);
        await db.SaveChangesAsync();

        var affectedAddress = await FamilySubscriptionDeliveryAmendments.ResolveAsync(db, subscription, affected, default);
        var protectedAddress = await FamilySubscriptionDeliveryAmendments.ResolveAsync(db, subscription, protectedDay, default);

        Assert.True(affectedAddress.IsSuccess);
        Assert.Equal("Accepted address B", affectedAddress.Value.AddressLine);
        Assert.True(protectedAddress.IsSuccess);
        Assert.Equal("Original address A", protectedAddress.Value.AddressLine);
    }

    [Fact]
    public async Task Malformed_amendment_lineage_returns_semantic_review_conflict()
    {
        await using var db = NewDb();
        var subscription = new Subscription { FamilyGroupId = Guid.NewGuid(), FamilyMemberId = Guid.NewGuid(),
            CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active, StartDate = new DateOnly(2026, 10, 9), EndDate = new DateOnly(2026, 10, 9), DurationDays = 1 };
        var day = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 10, 9), Status = SubscriptionDayStatus.Open };
        var amendment = new FamilySubscriptionDeliveryAmendment { SubscriptionId = subscription.Id, Version = 1,
            ActorUserId = Guid.NewGuid(), EffectiveFromDate = day.ServiceDate, AddressId = Guid.NewGuid(),
            AddressLabel = "B", AddressLine = "Accepted address B", RegionId = Guid.NewGuid(),
            AffectedDayIdsJson = "{invalid", CreatedAtUtc = DateTime.UtcNow };
        db.AddRange(subscription, day, amendment);
        await db.SaveChangesAsync();

        var result = await FamilySubscriptionDeliveryAmendments.ResolveAsync(db, subscription, day, default);

        Assert.True(result.IsFailure);
        Assert.Equal("family.delivery_snapshot_review_required", result.Error.Code);
    }

    [Fact]
    public async Task Amend_changes_only_mutable_covered_days_and_returns_protected_ids()
    {
        var fixture = await CreateAmendmentFixtureAsync(covered: true, priorVersion: 0);
        var result = await fixture.Service.AmendAsync(fixture.Subscription.Id, fixture.Actor,
            new FamilyDeliveryAddressRequest(fixture.Address.Id, new DateOnly(2026, 10, 9), 0), default);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, result.Value.Version);
        Assert.Equal(new[] { fixture.OpenDay.Id }, result.Value.AffectedDayIds);
        Assert.Equal(new[] { fixture.ProtectedDay.Id }, result.Value.SkippedDayIds);
        Assert.Equal(fixture.Address.AddressLine, fixture.OpenDay.DeliveryAddressLine);
        Assert.Equal("Original paid address", fixture.ProtectedDay.DeliveryAddressLine);
        Assert.NotNull(fixture.OpenDay.SourceFamilyDeliveryAmendmentId);
        Assert.Null(fixture.ProtectedDay.SourceFamilyDeliveryAmendmentId);
    }

    [Fact]
    public async Task Amend_returns_current_version_and_rejects_uncovered_address_without_mutating_days()
    {
        var stale = await CreateAmendmentFixtureAsync(covered: true, priorVersion: 2);
        var staleResult = await stale.Service.AmendAsync(stale.Subscription.Id, stale.Actor,
            new FamilyDeliveryAddressRequest(stale.Address.Id, new DateOnly(2026, 10, 9), 1), default);
        Assert.True(staleResult.IsFailure);
        Assert.Equal("family.delivery_version_changed", staleResult.Error.Code);
        Assert.Equal(2L, staleResult.Error.Metadata!["currentVersion"]);

        var uncovered = await CreateAmendmentFixtureAsync(covered: false, priorVersion: 0);
        var coverageResult = await uncovered.Service.AmendAsync(uncovered.Subscription.Id, uncovered.Actor,
            new FamilyDeliveryAddressRequest(uncovered.Address.Id, new DateOnly(2026, 10, 9), 0), default);
        Assert.True(coverageResult.IsFailure);
        Assert.Equal("address.restaurant_does_not_service_region", coverageResult.Error.Code);
        Assert.Null(uncovered.OpenDay.DeliveryAddressLine);
        Assert.Null(uncovered.OpenDay.SourceFamilyDeliveryAmendmentId);
    }

    private sealed record AmendmentFixture(FamilySubscriptionDeliveryAmendments Service, Subscription Subscription,
        CustomerAddress Address, SubscriptionDay OpenDay, SubscriptionDay ProtectedDay, Guid Actor);

    private static async Task<AmendmentFixture> CreateAmendmentFixtureAsync(bool covered, int priorVersion)
    {
        var db = NewDb();
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var actor = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var region = new Region { Code = "R1", NameAr = "منطقة", NameEn = "Region", IsActive = true };
        var regionId = region.Id;
        var restaurantId = Guid.NewGuid();
        var subscription = new Subscription { FamilyGroupId = groupId, FamilyMemberId = memberId,
            CustomerUserId = customer, ProgramId = programId, BundleId = bundleId,
            Status = SubscriptionStatus.Active, StartDate = new DateOnly(2026, 10, 9), EndDate = new DateOnly(2026, 10, 10), DurationDays = 2 };
        var openDay = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 10, 9),
            Status = SubscriptionDayStatus.Open, RestaurantId = restaurantId,
            SelectionDeadlineUtc = now.AddDays(1) };
        var protectedDay = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 10, 10),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant, RestaurantId = restaurantId,
            DeliveryAddressLine = "Original paid address", DeliveryAddressId = Guid.NewGuid(), DeliveryRegionId = regionId };
        var address = new CustomerAddress { UserId = actor, Label = "New", AddressLine = "Accepted address B", RegionId = regionId };
        var batch = new FamilyCheckoutBatch { FamilyGroupId = groupId, PayerCustomerUserId = actor,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = Guid.NewGuid().ToString(), FinalPrice = 90m, CurrencyCode = "KWD" };
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, Batch = batch, SubscriptionId = subscription.Id,
            FamilyMemberId = memberId, CheckoutSelected = true, DeliveryAddressId = Guid.NewGuid(),
            AddressLine = "Original paid address", RegionId = regionId, ResolvedRestaurantId = restaurantId, MenuPlanVersionsJson = "[]" });
        db.AddRange(subscription, openDay, protectedDay, address, region, batch);
        if (priorVersion > 0)
            db.FamilySubscriptionDeliveryAmendments.Add(new FamilySubscriptionDeliveryAmendment
            {
                SubscriptionId = subscription.Id, Version = priorVersion, ActorUserId = actor,
                EffectiveFromDate = new DateOnly(2026, 10, 9), AddressId = address.Id,
                AddressLabel = address.Label, AddressLine = address.AddressLine, RegionId = regionId,
                AffectedDayIdsJson = "[]", CreatedAtUtc = now,
            });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var transactions = Substitute.For<IApplicationTransactionExecutor>();
        transactions.ExecuteResultAsync<FamilyDeliveryAddressResult>(
                Arg.Any<Func<CancellationToken, Task<Result<FamilyDeliveryAddressResult>>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Result<FamilyDeliveryAddressResult>>>>()(ci.Arg<CancellationToken>()));
        var locks = Substitute.For<IApplicationLockManager>();
        var access = Substitute.For<IFamilySubscriptionAccess>();
        access.AuthorizeAsync(Arg.Any<Subscription>(), actor, FamilyAction.EditAddress, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new FamilySubscriptionAccess(true, memberId)));
        var policies = Substitute.For<IOperationalPolicyService>();
        policies.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new OperationalPolicySettings());
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(
            covered ? [new RestaurantEligibilityDto(restaurantId, "مطعم", "Restaurant", true, [], [])] : []);
        var capacity = Substitute.For<IRestaurantCapacityReader>();
        capacity.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>)ci
                .Arg<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>().ToDictionary(x => x,
                    x => new RestaurantCapacityUnits(x.RestaurantId, x.ServiceDate, 5, 1, 1, 0, 1, 0, 0, 0, 0)));
        var service = new FamilySubscriptionDeliveryAmendments(db, clock, transactions, locks, access, policies, eligibility, capacity);
        return new AmendmentFixture(service, subscription, address, openDay, protectedDay, actor);
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
}
