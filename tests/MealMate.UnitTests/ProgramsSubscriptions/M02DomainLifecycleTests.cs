using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class M02DomainLifecycleTests
{
    [Fact]
    public void Program_DefaultStatus_IsDraft()
    {
        var program = new Program
        {
            Code = "PROG_LITE",
            NameAr = "برنامج لايت",
            NameEn = "Lite Program",
        };

        Assert.Equal(ProgramStatus.Draft, program.Status);
        Assert.True(program.IsActive);
        Assert.NotNull(program.Bundles);
        Assert.NotNull(program.ContentVersions);
    }

    [Fact]
    public void Bundle_DefaultStatus_IsDraft()
    {
        var bundle = new Bundle
        {
            ProgramId = Guid.NewGuid(),
            Code = "BND_LUNCH_ONLY",
            NameAr = "باقة الغداء",
            NameEn = "Lunch Only Bundle",
            MealsPerDay = 1,
        };

        Assert.Equal(ProgramStatus.Draft, bundle.Status);
        Assert.True(bundle.IsActive);
    }

    [Fact]
    public void Subscription_Initialization_SetsUpAuditCollectionsAndDefaultStatus()
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(26)),
            PaidAmount = new Money(150.000m, "KWD"),
        };

        Assert.Equal(SubscriptionStatus.Draft, subscription.Status);
        Assert.NotNull(subscription.Snapshots);
        Assert.NotNull(subscription.Operations);
        Assert.NotNull(subscription.DiscountsAndReferralLogs);
    }

    [Fact]
    public void SubscriptionOperation_Creation_SetsTimestampAndProperties()
    {
        var subId = Guid.NewGuid();
        var operation = new SubscriptionOperation
        {
            SubscriptionId = subId,
            OperationType = "CREATE",
            PreviousStatus = null,
            NewStatus = SubscriptionStatus.Draft,
            PerformedByUserId = Guid.NewGuid(),
            PerformedByUserType = UserType.Customer,
            CorrelationId = "corr-test-123",
        };

        Assert.Equal(subId, operation.SubscriptionId);
        Assert.Equal("CREATE", operation.OperationType);
        Assert.Equal(SubscriptionStatus.Draft, operation.NewStatus);
        Assert.True(operation.TimestampUtc <= DateTime.UtcNow);
    }
}
