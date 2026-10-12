using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Application.Features.ProgramsSubscriptions.Renewal;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FamilyLifecycleOperationsTests
{
    [Theory]
    [InlineData("freeze")]
    [InlineData("renewal")]
    [InlineData("upgrade")]
    public async Task Manager_can_read_lifecycle_of_paused_subordinate(string operation)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var subscription = new Subscription { CustomerUserId = owner, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Active, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), DurationDays = 26,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31), PaidAmount = new Money(100m, "KWD") };
        member.MemberSubscriptionId = subscription.Id;
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(subscription);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = owner, IsPaused = true, HasSeparateAccount = false });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc)); clock.TodayUtc.Returns(new DateOnly(2026, 10, 20));
        var quotes = Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>();
        quotes.BuildAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>()).Returns(new SubscriptionTierUpgradeQuoteBundle(26, 26, Array.Empty<SubscriptionTierUpgradeOptionQuote>(), null));
        var success = operation switch {
            "freeze" => (await new SubscriptionFreezeService(db, clock).GetFreezeDetailsAsync(subscription.Id, manager)).IsSuccess,
            "renewal" => (await new SubscriptionRenewalService(db, clock).RequestRenewalAsync(subscription.Id, manager)).IsSuccess,
            _ => (await new SubscriptionUpgradeService(db, clock, quotes,
                Substitute.For<IPaymentGatewayService>()).GetUpgradeOptionsAsync(subscription.Id, manager)).IsSuccess
        };
        Assert.True(success);
    }

    [Fact]
    public async Task Family_renewal_intent_exposes_only_the_family_checkout_route_and_keeps_the_previous_subscription()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var previous = new Subscription { CustomerUserId = owner, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Active, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, IncludeFriday = true,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PaidAmount = new Money(100m, "KWD") };
        member.MemberSubscriptionId = previous.Id;
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(previous);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = owner });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 20));

        var result = await new SubscriptionRenewalService(db, clock).RequestRenewalAsync(previous.Id, manager);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(group.Id, result.Value.FamilyGroupId);
        Assert.Equal(member.Id, result.Value.FamilyMemberId);
        Assert.Equal(previous.Id, result.Value.PreviousSubscriptionId);
        Assert.Null(result.Value.CheckoutEndpoint);
        Assert.Equal($"/api/v1/customer/family-subscription-flow/renewals/{result.Value.RequestId}/checkout",
            result.Value.FamilyRenewalCheckoutEndpoint);
        Assert.Equal(previous.Id, member.MemberSubscriptionId);
        var intent = await db.SubscriptionRenewals.SingleAsync();
        Assert.Null(intent.NewSubscriptionId);
        Assert.Null(intent.CompletedAtUtc);
    }

    [Fact]
    public async Task Family_renewal_request_rediscovery_returns_existing_pending_batch_and_new_subscription()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid(); var owner = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var previous = new Subscription { CustomerUserId = owner, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Completed, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30), PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc) };
        member.MemberSubscriptionId = previous.Id;
        var next = new Subscription { CustomerUserId = owner, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.PendingPayment, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = previous.DurationDays, StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 1), PaidAmount = new Money(90m, "KWD") };
        var renewal = new SubscriptionRenewal { PreviousSubscriptionId = previous.Id, FamilyGroupId = group.Id,
            FamilyMemberId = member.Id, Status = "PaymentPending", RequestedStartDate = next.StartDate,
            RequestedAddressId = Guid.NewGuid(), BatchVersion = 4,
            RequestedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc) };
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id,
            PayerCustomerUserId = manager, Status = FamilyCheckoutBatchStatus.PaymentPending, Purpose = "Renewal",
            RenewalRequestId = renewal.Id, IdempotencyKey = "renewal", FinalPrice = 90, CurrencyCode = "KWD",
            ExpiresAtUtc = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc) };
        renewal.BatchId = batch.Id;
        batch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, Batch = batch, FamilyMemberId = member.Id,
            SubscriptionId = next.Id, PreviousSubscriptionId = previous.Id, RenewalRequestId = renewal.Id, CheckoutSelected = true,
            StartDate = next.StartDate, DeliveryAddressId = renewal.RequestedAddressId });
        batch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { BatchId = batch.Id, Batch = batch,
            AttemptKey = "renewal", ResultCode = "Initialized", GatewayPaymentUrl = "https://pay.example/pending" });
        db.AddRange(group, member, previous, next, renewal, batch,
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = owner });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 20));

        var result = await new SubscriptionRenewalService(db, clock).RequestRenewalAsync(previous.Id, manager);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(renewal.Id, result.Value.RequestId);
        Assert.Equal("PaymentPending", result.Value.Status);
        Assert.Equal(batch.Id, result.Value.BatchId);
        Assert.Equal(renewal.BatchVersion, result.Value.BatchVersion);
        Assert.Equal(next.Id, result.Value.NewSubscriptionId);
        Assert.Equal("PaymentPending", result.Value.CheckoutStatus);
        Assert.Equal("https://pay.example/pending", result.Value.PaymentUrl);
        Assert.Single(await db.SubscriptionRenewals.ToListAsync());
        previous.EndDate = new DateOnly(2026, 10, 2);
        await db.SaveChangesAsync();
        var staleLink = await new SubscriptionRenewalService(db, clock).RequestRenewalAsync(previous.Id, manager);
        Assert.True(staleLink.IsSuccess, staleLink.IsFailure ? staleLink.Error.Code : null);
        Assert.Equal(renewal.Id, staleLink.Value.RequestId);
        Assert.Null(staleLink.Value.PaymentUrl);

    }

    [Fact]
    public async Task Family_renewal_retry_requires_verified_unpaid_terminal_and_review_stays_blocked()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var previous = new Subscription { CustomerUserId = owner, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Completed, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 19), PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc) };
        member.MemberSubscriptionId = previous.Id;
        var oldRequest = new SubscriptionRenewal { PreviousSubscriptionId = previous.Id, FamilyGroupId = group.Id,
            FamilyMemberId = member.Id, Status = "PaymentPending", RequestedAtUtc = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc) };
        var failedBatch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            Status = FamilyCheckoutBatchStatus.Failed, Purpose = "Renewal", RenewalRequestId = oldRequest.Id,
            IdempotencyKey = "old-renewal", CurrencyCode = "KWD", FinalPrice = 90m };
        failedBatch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { Batch = failedBatch, BatchId = failedBatch.Id,
            AttemptKey = "old-attempt", ResultCode = "Failed", VerifiedAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc) });
        oldRequest.BatchId = failedBatch.Id;
        oldRequest.BatchVersion = failedBatch.Version;
        db.AddRange(group, member, previous, oldRequest, failedBatch);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 20));
        var transactions = Substitute.For<IApplicationTransactionExecutor>();
        transactions.ExecuteResultAsync<CustomerRenewalRequestDto>(
                Arg.Any<Func<CancellationToken, Task<Result<CustomerRenewalRequestDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Result<CustomerRenewalRequestDto>>>>()(ci.Arg<CancellationToken>()));
        var service = new SubscriptionRenewalService(db, clock, transactions: transactions,
            locks: Substitute.For<IApplicationLockManager>());

        Assert.Equal(failedBatch.Id, await db.SubscriptionRenewals.Where(x => x.Id == oldRequest.Id)
            .Select(x => x.BatchId).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.Failed, await db.FamilyCheckoutBatches.Where(x => x.Id == failedBatch.Id)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal("Failed", await db.FamilyCheckoutPaymentAttempts.Where(x => x.BatchId == failedBatch.Id)
            .Select(x => x.ResultCode).SingleAsync());

        var retry = await service.RequestRenewalAsync(previous.Id, manager);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error.Code : null);
        Assert.NotEqual(oldRequest.Id, retry.Value.RequestId);
        Assert.Equal("Requested", retry.Value.Status);
        Assert.Equal(FamilyCheckoutBatchStatus.Failed,
            await db.FamilyCheckoutBatches.Where(x => x.Id == failedBatch.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(previous.Id, await db.FamilyMembers.Where(x => x.Id == member.Id).Select(x => x.MemberSubscriptionId).SingleAsync());

        var reviewRequest = db.SubscriptionRenewals.Single(x => x.Id == retry.Value.RequestId);
        reviewRequest.Status = "ReviewRequired";
        var reviewBatch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            Status = FamilyCheckoutBatchStatus.ReviewRequired, Purpose = "Renewal", RenewalRequestId = reviewRequest.Id,
            IdempotencyKey = "uncertain-renewal", CurrencyCode = "KWD", FinalPrice = 90m };
        reviewRequest.BatchId = reviewBatch.Id;
        reviewRequest.BatchVersion = reviewBatch.Version;
        reviewBatch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { Batch = reviewBatch, BatchId = reviewBatch.Id,
            AttemptKey = "uncertain", ResultCode = "InitializationUncertain" });
        db.FamilyCheckoutBatches.Add(reviewBatch);
        await db.SaveChangesAsync();

        var unresolved = await service.RequestRenewalAsync(previous.Id, manager);
        Assert.True(unresolved.IsSuccess, unresolved.IsFailure ? unresolved.Error.Code : null);
        Assert.Equal(reviewRequest.Id, unresolved.Value.RequestId);
        Assert.Equal("ReviewRequired", unresolved.Value.Status);
        Assert.Null(unresolved.Value.PaymentUrl);
        Assert.Equal(2, await db.SubscriptionRenewals.CountAsync());
    }
}
