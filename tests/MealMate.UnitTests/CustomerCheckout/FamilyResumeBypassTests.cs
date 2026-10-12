using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using MediatR;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyResumeBypassTests
{
    [Fact]
    public async Task Generic_resume_and_preview_block_manager_access_before_payment_or_mutation()
    {
        var managerId = Guid.NewGuid();
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Active };
        var program = new Program
        {
            Code = "FAM-RESUME", ShortCode = "FR", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Description", KeywordsAr = ["كلمات"], KeywordsEn = ["keywords"],
            IsActive = true, Status = ProgramStatus.Active,
        };
        var bundle = new Bundle
        {
            Program = program, Code = "FAM-BUNDLE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, IsActive = true, Status = ProgramStatus.Active,
        };
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(), FamilyGroupId = group.Id,
            Program = program, Bundle = bundle,
            SelectedTier = RestaurantTier.Basic, DurationDays = 10,
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 11),
            Status = SubscriptionStatus.PaymentFailed, PaidAmount = new Money(20m, "KWD"),
        };
        db.AddRange(group, program, bundle, subscription);
        await db.SaveChangesAsync();
        Assert.True(await db.FamilyGroups.AsNoTracking().AnyAsync(x => x.Id == subscription.FamilyGroupId
            && x.ManagerCustomerUserId == managerId));

        var payments = Substitute.For<IPaymentGatewayService>();
        var transactions = Substitute.For<ICheckoutTransactionExecutor>();
        var sender = Substitute.For<ISender>();
        var service = new CustomerCheckoutService(db, payments, Substitute.For<IDateTimeProvider>(),
            Substitute.For<IUserAccountService>(), Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<ICapacityConcurrencyLock>(), transactions, Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), null, Substitute.For<IAllowedSubscriptionDurationService>(),
            Substitute.For<ISubscriptionPaymentProcessor>(), Substitute.For<ISubscriptionCheckoutReadinessService>(),
            sender, Substitute.For<IRestaurantCapacityReader>());

        var resume = await service.ResumeCheckoutAsync(subscription.Id, null, managerId);
        var preview = await service.GetResumePreviewAsync(subscription.Id, managerId);

        Assert.Equal("family.actual_checkout_required", resume.Error.Code);
        Assert.Equal("family.actual_checkout_required", preview.Error.Code);
        Assert.Equal(SubscriptionStatus.PaymentFailed, (await db.Subscriptions.SingleAsync()).Status);
        Assert.Empty(await db.CapacityHolds.ToListAsync());
        await payments.DidNotReceive().GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>());
        await payments.DidNotReceive().ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await transactions.DidNotReceive().ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>());
    }
}
