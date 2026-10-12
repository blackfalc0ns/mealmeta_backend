using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class FamilyDeliverySnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Profile_changes_and_dispatch_preserve_family_purchase_address(bool synchronize)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var owner = Guid.NewGuid(); var regionA = Guid.NewGuid(); var regionB = Guid.NewGuid();
        var subscription = new Subscription { CustomerUserId = owner, FamilyGroupId = Guid.NewGuid(), FamilyMemberId = Guid.NewGuid(),
            DeliveryAddress = "Purchased address A", RegionId = regionA, Status = SubscriptionStatus.Active,
            ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), PaidAmount = new Money(80m, "KWD") };
        var profile = new CustomerProfile { UserId = owner, ActiveDeliveryAddressLine = "Profile address B", ActiveDeliveryRegionId = regionB };
        var addressB = new CustomerAddress { UserId = owner, Label = "Home", AddressLine = "Profile address B", RegionId = regionB };
        var batch = new FamilyCheckoutBatch { FamilyGroupId = subscription.FamilyGroupId.Value, PayerCustomerUserId = owner,
            IdempotencyKey = "purchase", Status = FamilyCheckoutBatchStatus.Paid };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = subscription.FamilyMemberId.Value, SubscriptionId = subscription.Id,
            DeliveryAddressId = Guid.NewGuid(), AddressLine = subscription.DeliveryAddress, RegionId = regionA });
        var day = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 11, 1), DeliveryAddressLine = subscription.DeliveryAddress, DeliveryRegionId = regionA };
        db.Subscriptions.Add(subscription); db.CustomerProfiles.Add(profile); db.CustomerAddresses.Add(addressB);
        db.FamilyCheckoutBatches.Add(batch); db.SubscriptionDays.Add(day); await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(new DateTime(2026, 10, 9));
        var service = new CustomerDeliveryAddressService(db, clock);
        if (synchronize) await service.InitializeActiveDeliveryAsync(profile, addressB, default);
        else await service.SnapshotDayDeliveryAsync(day, owner, new DateTime(2026, 10, 9), default);
        Assert.Equal("Purchased address A", day.DeliveryAddressLine);
        Assert.Equal(regionA, day.DeliveryRegionId);
        Assert.Equal("Purchased address A", subscription.DeliveryAddress);
    }
}
