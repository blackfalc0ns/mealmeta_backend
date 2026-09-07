using System.Text.Json;
using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerSubscriptionManagementControllerTests
{
    [Fact]
    public async Task Management_ReturnsOnlyCurrentCustomersSubscriptionsWithProgressAndReceipts()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription, NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active));
        db.SubscriptionDays.AddRange(
            new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 8, 10), Status = SubscriptionDayStatus.Delivered },
            new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 8, 15), Status = SubscriptionDayStatus.Open });
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "MyFatoorah", EventKey = "evt-1", SubscriptionId = subscription.Id, Status = "Paid", PaymentId = "pay-1",
            InvoiceId = "inv-1", Amount = 49.99m, CurrencyCode = "KWD", ProcessedAtUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
            VerifiedAtUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = Assert.IsType<OkObjectResult>(await controller.Management(true, CancellationToken.None));
        var screen = Assert.IsType<CustomerSubscriptionManagementScreenDto>(result.Value);

        Assert.Single(screen.Subscriptions);
        Assert.NotNull(screen.CurrentSubscription);
        Assert.Equal(1, screen.CurrentSubscription.CompletedServiceDays);
        Assert.Equal(1, screen.CurrentSubscription.RemainingServiceDays);
        Assert.Equal(50m, screen.CurrentSubscription.ProgressPercentage);
        Assert.Single(screen.Invoices);
        Assert.Null(screen.Invoices[0].MaskedCardNumber);
        Assert.False(screen.Invoices[0].CanDownload);
    }

    [Fact]
    public async Task CancellationQuote_ReturnsCorrectCalculationsBasedOnKitchenLeadWindow()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        // 10-day subscription, paid 50 KWD (5 KWD per day)
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        subscription.DurationDays = 10;
        subscription.PaidAmount = new Money(50m, "KWD");
        db.AddRange(program, bundle, subscription);

        var today = new DateOnly(2026, 8, 10);
        // Day 1: Delivered
        // Day 2 (today): Locked
        // Day 3 (today+1): Locked
        // Day 4 (today+2): Locked
        // Day 5-10: Open (6 days refundable)
        db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(-1), Status = SubscriptionDayStatus.Delivered });
        for (var i = 0; i <= 2; i++)
        {
            db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Locked72h });
        }
        for (var i = 3; i < 9; i++)
        {
            db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var controller = CreateController(db, customerId, clock: clock);
        var result = Assert.IsType<OkObjectResult>(await controller.GetCancellationQuote(subscription.Id, CancellationToken.None));
        var quote = Assert.IsType<CustomerCancellationQuoteDto>(result.Value);

        Assert.Equal(10, quote.TotalServiceDays);
        Assert.Equal(1, quote.DeliveredDays);
        Assert.Equal(3, quote.LockedKitchenDays);
        Assert.Equal(6, quote.RefundableDays);
        Assert.Equal(5.0m, quote.DailyBoxPrice);
        Assert.Equal(30.0m, quote.RefundAmount);
        Assert.Equal("KWD", quote.CurrencyCode);
    }

    [Fact]
    public async Task CancellationRequest_CancelsSubscriptionAndRefundsRemainingDaysBeyondKitchenWindow()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        subscription.DurationDays = 10;
        subscription.PaidAmount = new Money(50m, "KWD");
        db.AddRange(program, bundle, subscription);

        var today = new DateOnly(2026, 8, 10);
        // Day 1: Delivered
        db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(-1), Status = SubscriptionDayStatus.Delivered });
        // Day 2 (today): Locked
        db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today, Status = SubscriptionDayStatus.Locked72h });
        // Day 3 (today+1): Locked
        db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(1), Status = SubscriptionDayStatus.Locked72h });
        // Day 4-10: Open (7 days refundable)
        for (var i = 3; i <= 9; i++)
        {
            db.SubscriptionDays.Add(new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var controller = CreateController(db, customerId, clock: clock);
        var response = Assert.IsType<OkObjectResult>(await controller.RequestCancellation(subscription.Id, new CustomerCancellationRequest("Moving abroad", null), CancellationToken.None));
        var result = Assert.IsType<CustomerCancellationResultDto>(response.Value);

        Assert.Equal(SubscriptionStatus.CancellationPending.ToString(), result.Status);
        Assert.Equal(7, result.RefundableDays);
        Assert.Equal(35m, result.RefundAmount);

        var updatedSub = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(SubscriptionStatus.CancellationPending, updatedSub!.Status);

        // Check that refundable days are marked as Cancelled
        var cancelledDays = await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id && x.Status == SubscriptionDayStatus.Cancelled).CountAsync();
        Assert.Equal(7, cancelledDays);
    }

    [Fact]
    public async Task ManagementDetails_DoesNotExposeAnotherCustomersSubscription()
    {
        await using var db = CreateDb();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(Guid.NewGuid(), program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = await CreateController(db, Guid.NewGuid()).ManagementDetails(subscription.Id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Management_PrefersActiveSubscriptionOverNewPendingCheckout()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, active, pending);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Management(true, CancellationToken.None));
        var screen = Assert.IsType<CustomerSubscriptionManagementScreenDto>(result.Value);
        Assert.Equal(active.Id, screen.CurrentSubscription!.SubscriptionId);
    }

    [Fact]
    public async Task Current_ReturnsActiveSubscriptionWithoutInvoices()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.NotNull(screen.Subscription);
        Assert.Equal(subscription.Id, screen.Subscription.SubscriptionId);
        Assert.Equal(nameof(SubscriptionStatus.Active), screen.Subscription.Status);
        Assert.Null(screen.EmptyStateCode);
    }

    [Fact]
    public async Task UpgradeOptions_DoesNotIncludePaymentMethods()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = Assert.IsType<OkObjectResult>(await controller.UpgradeOptions(subscription.Id, CancellationToken.None));
        var dto = Assert.IsType<CustomerUpgradeOptionsDto>(result.Value);

        Assert.Null(dto.PaymentMethods);
    }

    [Fact]
    public async Task UpgradeSummary_ReturnsFullComparison_72hCutoff_Repetition_AndPaymentMethods()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active, RestaurantTier.Basic);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new List<MyFatoorahPaymentMethod>
            {
                new(1, "KN", "كي نت", "KNET", 1.16m, 16m, "KWD", true, "https://portal.myfatoorah.com/imgs/payment-methods/kn.png"),
                new(2, "VM", "فيزا / ماستر", "VISA / MASTER", 0.5m, 16m, "KWD", true, "https://portal.myfatoorah.com/imgs/payment-methods/vm.png")
            });

        var quotes = Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>();
        quotes.BuildAsync(Arg.Any<Subscription>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionTierUpgradeQuoteBundle(
                RemainingServiceDays: 12,
                TotalServiceDays: 26,
                Options: new List<SubscriptionTierUpgradeOptionQuote>
                {
                    new(RestaurantTier.Platinum, "بلاتينيوم", "Platinum", null, false, true, 5,
                        70.5m, 86.5m, 0m, 16.0m, "KWD", 12, 26)
                },
                PricingUnavailableReason: null));

        var controller = CreateController(db, customerId, payments, upgradeQuotes: quotes);
        var result = Assert.IsType<OkObjectResult>(await controller.UpgradeSummary(subscription.Id, RestaurantTier.Platinum, CancellationToken.None));
        var summary = Assert.IsType<CustomerUpgradeSummaryDto>(result.Value);

        Assert.Equal(subscription.Id, summary.SubscriptionId);
        Assert.Equal(RestaurantTier.Basic, summary.CurrentTier.Tier);
        Assert.Equal(RestaurantTier.Platinum, summary.TargetTier.Tier);
        Assert.NotNull(summary.EffectiveNoticeAr);
        Assert.Contains("72", summary.EffectiveNoticeAr);
        Assert.Equal(new DateOnly(2026, 8, 16), summary.EffectiveFromDate);
        Assert.NotNull(summary.RestaurantRepetition);
        Assert.Equal(16.0m, summary.PriceDifference);
        Assert.Equal(70.5m, summary.CurrentRemainingCredit);
        Assert.Equal(86.5m, summary.TargetRemainingPrice);
        Assert.NotNull(summary.PaymentMethods);
        Assert.Equal(2, summary.PaymentMethods.Count);
        Assert.Equal(0m, summary.PaymentMethods[0].ServiceCharge);
        Assert.Equal(16.0m, summary.PaymentMethods[0].TotalAmount);
    }

    [Fact]
    public async Task Current_PrefersActiveOverPendingPayment()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, active, pending);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);
        Assert.Equal(active.Id, screen.Subscription!.SubscriptionId);
    }

    [Fact]
    public async Task Current_MapsSuspendedForReviewToPaidForCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.SuspendedForReview);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.NotNull(screen.Subscription);
        Assert.Equal(nameof(SubscriptionStatus.Paid), screen.Subscription.Status);
        Assert.Equal("تم الدفع", screen.Subscription.StatusNameAr);
        Assert.False(screen.Subscription.Actions.CanUpgrade);
        Assert.False(screen.Subscription.Actions.CanFreeze);
        Assert.Equal(SubscriptionStatus.SuspendedForReview, (await db.Subscriptions.FindAsync(subscription.Id))!.Status);
    }

    [Fact]
    public async Task Current_DoesNotExposeAnotherCustomersSubscription()
    {
        await using var db = CreateDb();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(Guid.NewGuid(), program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, Guid.NewGuid()).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);
        Assert.Null(screen.Subscription);
        Assert.Equal("no_current_subscription", screen.EmptyStateCode);
    }

    [Fact]
    public async Task FreezeDetails_ReturnsActiveFreezeWithCompensationDatesAndHistory()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var sub = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        sub.StartDate = DateOnly.FromDateTime(DateTime.UtcNow);
        sub.EndDate = sub.StartDate.AddDays(20);
        db.AddRange(program, bundle, sub);

        var freezeStart = sub.StartDate.AddDays(5);
        var freezeEnd = sub.StartDate.AddDays(9);

        db.SubscriptionFreezeRequests.Add(new SubscriptionFreezeRequest
        {
            SubscriptionId = sub.Id,
            FreezeStartDate = freezeStart,
            FreezeEndDate = freezeEnd,
            FreezeDays = 5,
            Status = VerificationStatus.Approved,
            Reason = "Travel",
            ProcessedAtUtc = DateTime.UtcNow
        });

        var frozenDay = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            ServiceDate = freezeStart,
            Status = SubscriptionDayStatus.Frozen
        };
        var compDay = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            ServiceDate = sub.EndDate,
            Status = SubscriptionDayStatus.Open
        };
        db.SubscriptionDays.AddRange(frozenDay, compDay);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);

        // 1. Check {id}/freeze-details
        var result = Assert.IsType<OkObjectResult>(await controller.FreezeDetails(sub.Id, CancellationToken.None));
        var details = Assert.IsType<CustomerSubscriptionFreezeDetailsDto>(result.Value);

        Assert.True(details.HasFreeze);
        Assert.NotNull(details.CurrentFreeze);
        Assert.Equal(freezeStart, details.CurrentFreeze.StartDate);
        Assert.Equal(freezeEnd, details.CurrentFreeze.EndDate);
        Assert.Equal("Approved", details.CurrentFreeze.Status);
        Assert.Single(details.CurrentFreeze.FrozenServiceDates);
        Assert.Equal(frozenDay.ServiceDate, details.CurrentFreeze.FrozenServiceDates[0]);
        Assert.Single(details.FreezeHistory);

        // 2. Check current/freeze-details
        var curResult = Assert.IsType<OkObjectResult>(await controller.CurrentFreezeDetails(CancellationToken.None));
        var curDetails = Assert.IsType<CustomerSubscriptionFreezeDetailsDto>(curResult.Value);
        Assert.Equal(sub.Id, curDetails.SubscriptionId);
        Assert.True(curDetails.HasFreeze);
    }

    [Fact]
    public async Task List_FiltersToCurrentCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var first = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var second = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Completed);
        var other = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, first, second, other);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).List(true, CancellationToken.None));
        var list = Assert.IsAssignableFrom<IReadOnlyList<CustomerSubscriptionSlimDto>>(result.Value);
        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.Equal(customerId, db.Subscriptions.Find(x.SubscriptionId)!.CustomerUserId));
    }

    [Fact]
    public async Task Invoices_ReturnsOnlyOwnedReceiptsNewestFirst()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var ownedSubscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var otherSubscription = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, ownedSubscription, otherSubscription);
        db.PaymentWebhookReceipts.AddRange(
            NewReceipt(ownedSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc), "inv-owned-1"),
            NewReceipt(ownedSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-owned-2"),
            NewReceipt(otherSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc), "inv-other"));
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Invoices(CancellationToken.None));
        var invoices = Assert.IsAssignableFrom<IReadOnlyList<CustomerSubscriptionInvoiceListDto>>(result.Value);
        Assert.Equal(2, invoices.Count);
        Assert.Equal("inv-owned-2", invoices[0].InvoiceNumber);
        Assert.Equal("inv-owned-1", invoices[1].InvoiceNumber);
    }

    [Fact]
    public async Task InvoiceDetail_ReturnsNotFoundForOtherCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var ownedSubscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var otherSubscription = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, ownedSubscription, otherSubscription);
        var receipt = NewReceipt(otherSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-other");
        db.PaymentWebhookReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var result = await CreateController(db, customerId).InvoiceDetail(receipt.Id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DefaultPaymentMethod_DoesNotInventCardDigits()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        db.PaymentWebhookReceipts.Add(NewReceipt(subscription.Id, "Tap", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-1"));
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).DefaultPaymentMethod(CancellationToken.None));
        var method = Assert.IsType<CustomerDefaultPaymentMethodDto>(result.Value);

        Assert.False(method.HasSavedMethod);
        Assert.Null(method.Last4);
        Assert.Null(method.Expiry);
        Assert.Null(method.Brand);
        Assert.Equal("saved_card_not_available", method.EmptyStateCode);
        Assert.Equal("Tap", method.Gateway);
    }

    [Fact]
    public async Task FreezePolicy_ReturnsDefaultMessages()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, Guid.NewGuid());

        var result = Assert.IsType<OkObjectResult>(await controller.FreezePolicy(CancellationToken.None));
        var policy = Assert.IsType<CustomerSubscriptionFreezePolicyDto>(result.Value);

        Assert.Equal(7, policy.DefaultDurationDays);
        Assert.Equal(3, policy.StartDelayDays);
        Assert.True(policy.ExtendSubscriptionEnd);
        Assert.Contains("7", policy.MessageAr);
        Assert.Contains("3", policy.MessageAr);
        Assert.Contains("7", policy.MessageEn);
        Assert.Contains("3", policy.MessageEn);
    }

    [Fact]
    public async Task UpgradeOptions_OnlyHigherTiers()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewUpgradeableSubscription(customerId, program.Id, bundle.Id);
        db.AddRange(program, bundle, subscription);
        SeedUpgradePricing(db, program.Id, bundle.Id);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).UpgradeOptions(subscription.Id, CancellationToken.None));
        var options = Assert.IsType<CustomerUpgradeOptionsDto>(result.Value);

        Assert.True(options.CanUpgrade);
        Assert.Null(options.UnavailableReason);
        Assert.Equal(RestaurantTier.Basic, options.CurrentTier);
        Assert.Equal(26, options.RemainingServiceDays);
        Assert.Equal(26, options.TotalServiceDays);
        Assert.Equal(3, options.Options.Count);
        Assert.Equal(RestaurantTier.Basic, options.Options[0].TargetTier);
        Assert.True(options.Options[0].IsCurrent);
        Assert.False(options.Options[0].Selectable);
        Assert.Equal(RestaurantTier.Platinum, options.Options[1].TargetTier);
        Assert.True(options.Options[1].Selectable);
        Assert.True(options.Options[1].AccessibleRestaurantCount >= 1);
        Assert.Equal(RestaurantTier.Elite, options.Options[2].TargetTier);
        Assert.True(options.Options[2].Selectable);
        Assert.Equal(0m, options.Options[0].PriceDifference);
        Assert.True(options.Options[1].PriceDifference > 0);
        Assert.True(options.Options[2].PriceDifference > options.Options[1].PriceDifference);
        Assert.Equal("KWD", options.Options[1].CurrencyCode);
        Assert.Equal($"/api/v1/customer/subscriptions/{subscription.Id}/upgrade-requests", options.UpgradeEndpoint);
    }

    [Fact]
    public async Task UpgradeOptions_AllowsScheduledSubscription()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewUpgradeableSubscription(customerId, program.Id, bundle.Id);
        subscription.Status = SubscriptionStatus.Scheduled;
        subscription.StartDate = new DateOnly(2026, 8, 30);
        subscription.EndDate = new DateOnly(2026, 9, 12);
        db.AddRange(program, bundle, subscription);
        SeedUpgradePricing(db, program.Id, bundle.Id);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).UpgradeOptions(subscription.Id, CancellationToken.None));
        var options = Assert.IsType<CustomerUpgradeOptionsDto>(result.Value);

        Assert.True(options.CanUpgrade);
        Assert.Null(options.UnavailableReason);
        Assert.Contains(options.Options, x => x.Selectable && x.TargetTier == RestaurantTier.Platinum);
    }

    [Fact]
    public async Task RequestUpgrade_StartsPaymentAndReturnsGatewayUrl()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewUpgradeableSubscription(customerId, program.Id, bundle.Id);
        db.AddRange(program, bundle, subscription);
        SeedUpgradePricing(db, program.Id, bundle.Id);
        await db.SaveChangesAsync();

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                subscription.Id.ToString(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=UPG-001");

        var controller = CreateController(db, customerId, payments);
        var result = Assert.IsType<ObjectResult>(await controller.RequestUpgrade(
            subscription.Id,
            new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error"),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        var started = Assert.IsType<CustomerUpgradeStartedDto>(result.Value);
        Assert.False(started.IsPaid);
        Assert.Equal("https://pay.example/invoice?paymentId=UPG-001", started.PaymentUrl);
        Assert.Equal(RestaurantTier.Platinum, started.ToTier);
        Assert.True(started.Amount > 0);

        var upgrade = await db.SubscriptionTierUpgrades.SingleAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal("UPG-001", upgrade.PaymentId);
        Assert.Equal(VerificationStatus.Submitted, upgrade.Status);
    }

    [Fact]
    public async Task RequestUpgrade_RejectsWhenPendingUpgradeExists()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewUpgradeableSubscription(customerId, program.Id, bundle.Id);
        db.AddRange(program, bundle, subscription);
        SeedUpgradePricing(db, program.Id, bundle.Id);
        db.SubscriptionTierUpgrades.Add(new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(5m, "KWD"),
            QuotedRemainingDays = 26,
            QuotedTotalDue = new Money(5m, "KWD"),
            Status = VerificationStatus.Submitted,
        });
        await db.SaveChangesAsync();

        var result = await CreateController(db, customerId).RequestUpgrade(
            subscription.Id,
            new CustomerUpgradeRequest(RestaurantTier.Elite, "https://app/callback", "https://app/error"),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task RequestUpgrade_ReturnsBadGatewayWhenPaymentInitializationFails()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewUpgradeableSubscription(customerId, program.Id, bundle.Id);
        db.AddRange(program, bundle, subscription);
        SeedUpgradePricing(db, program.Id, bundle.Id);
        await db.SaveChangesAsync();

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                Arg.Any<decimal>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("gateway down"));

        var result = await CreateController(db, customerId, payments).RequestUpgrade(
            subscription.Id,
            new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error"),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        var upgrade = await db.SubscriptionTierUpgrades.SingleAsync(x => x.SubscriptionId == subscription.Id);
        Assert.Equal(VerificationStatus.Rejected, upgrade.Status);
    }

    [Fact]
    public async Task RequestUpgrade_AppliesZeroDueUpgradeImmediately()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active, RestaurantTier.Basic);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var quotes = Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>();
        quotes.BuildForTargetAsync(Arg.Any<Subscription>(), RestaurantTier.Platinum, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionTierUpgradeOptionQuote(
                RestaurantTier.Platinum, "بلاتينيوم", "Platinum", null, false, true, 2,
                10m, 10m, 0m, 0m, "KWD", 2, 2));

        var result = Assert.IsType<ObjectResult>(await CreateController(db, customerId, upgradeQuotes: quotes).RequestUpgrade(
            subscription.Id,
            new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error"),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        var started = Assert.IsType<CustomerUpgradeStartedDto>(result.Value);
        Assert.True(started.IsPaid);
        Assert.Null(started.PaymentUrl);

        var tracked = await db.Subscriptions.FindAsync(subscription.Id);
        Assert.Equal(RestaurantTier.Platinum, tracked!.SelectedTier);
        Assert.Equal(SubscriptionStatus.Active, tracked.Status);
    }

    [Fact]
    public async Task UpgradeRequestStatus_ReturnsPaidStateAfterApproval()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active, RestaurantTier.Basic);
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(8m, "KWD"),
            QuotedRemainingDays = 2,
            QuotedTotalDue = new Money(8m, "KWD"),
            PaymentId = "UPG-002",
            Status = VerificationStatus.Approved,
            EffectiveAtUtc = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc),
        };
        db.AddRange(program, bundle, subscription, upgrade);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId)
            .UpgradeRequestStatus(subscription.Id, upgrade.Id, CancellationToken.None));
        var status = Assert.IsType<CustomerUpgradeStatusDto>(result.Value);

        Assert.True(status.IsPaid);
        Assert.False(status.IsFailed);
        Assert.Equal(8m, status.Amount);
        Assert.Equal(RestaurantTier.Platinum, status.ToTier);
    }

    [Fact]
    public async Task UncompletedSubscriptions_ReturnsDraftPendingAndFailedWithHoldStatus()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);

        var draft = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Draft);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        var failed = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PaymentFailed);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var otherDraft = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Draft);

        db.AddRange(program, bundle, draft, pending, failed, active, otherDraft);

        var now = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
        db.CapacityHolds.Add(new MealMate.Domain.RestaurantOperations.CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = pending.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 8, 15),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = now.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 13));

        var controller = CreateController(db, customerId, clock: clock);
        var result = Assert.IsType<OkObjectResult>(await controller.UncompletedSubscriptions(CancellationToken.None));
        var list = Assert.IsAssignableFrom<IReadOnlyList<CustomerUncompletedSubscriptionDto>>(result.Value);

        Assert.Equal(3, list.Count);
        Assert.DoesNotContain(list, x => x.SubscriptionId == active.Id);
        Assert.DoesNotContain(list, x => x.SubscriptionId == otherDraft.Id);

        var pendingItem = Assert.Single(list, x => x.SubscriptionId == pending.Id);
        Assert.True(pendingItem.IsHoldActive);
        Assert.Equal(600, pendingItem.HoldRemainingSeconds);
        Assert.NotNull(pendingItem.HoldExpiresAtUtc);
        Assert.True(pendingItem.Actions.CanDelete);
        Assert.True(pendingItem.Actions.CanResume);
        Assert.Equal($"/api/v1/customer/subscriptions/{pending.Id}", pendingItem.Actions.DeleteEndpoint);
        Assert.Equal($"/api/v1/customer/subscriptions/{pending.Id}/resume", pendingItem.Actions.ResumeEndpoint);
        Assert.Equal("checkout", pendingItem.Actions.NextScreen);
        Assert.Equal($"/api/v1/customer/subscriptions/{pending.Id}/resume", pendingItem.Actions.PayEndpoint);
    }

    [Fact]
    public async Task Current_DoesNotReturnUncompletedOrPendingPaymentAsCurrent()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, pending);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = Assert.IsType<OkObjectResult>(await controller.Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.Null(screen.Subscription);
        Assert.Equal("no_current_subscription", screen.EmptyStateCode);
    }

    [Fact]
    public async Task DeleteUncompletedSubscription_ReleasesHoldsAndSetsStatusToCancelled()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, pending);

        var hold = new MealMate.Domain.RestaurantOperations.CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = pending.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 8, 15),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = await controller.DeleteUncompletedSubscription(pending.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        var updatedSub = await db.Subscriptions.AsNoTracking().FirstAsync(x => x.Id == pending.Id);
        Assert.Equal(SubscriptionStatus.Cancelled, updatedSub.Status);

        var updatedHold = await db.CapacityHolds.AsNoTracking().FirstAsync(x => x.Id == hold.Id);
        Assert.Equal(CapacityHoldStatus.Released, updatedHold.Status);
        Assert.NotNull(updatedHold.ReleasedAtUtc);
    }

    [Fact]
    public async Task DeleteUncompletedSubscription_RejectsActiveSubscription()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, active);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = await controller.DeleteUncompletedSubscription(active.Id, CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("cannot_delete", conflict.Value?.ToString());
    }

    private static CustomerSubscriptionManagementController CreateController(
        AppDbContext db,
        Guid customerId,
        IMyFatoorahPaymentService? payments = null,
        ISubscriptionTierUpgradeQuoteBuilder? upgradeQuotes = null,
        IDateTimeProvider? clock = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        if (clock is null)
        {
            clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc));
            clock.TodayUtc.Returns(new DateOnly(2026, 8, 13));
        }

        upgradeQuotes ??= new SubscriptionTierUpgradeQuoteBuilder(
            db,
            clock,
            new RestaurantTierClassificationService(db, clock),
            new SubscriptionScheduleService(clock));

        payments ??= Substitute.For<IMyFatoorahPaymentService>();
        var users = CreateUserManager(customerId);

        return new CustomerSubscriptionManagementController(db, user, clock, upgradeQuotes, payments, users)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static UserManager<ApplicationUser> CreateUserManager(Guid customerId)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var userManager = Substitute.For<UserManager<ApplicationUser>>(
            store, null, null, null, null, null, null, null, null);
        userManager.FindByIdAsync(customerId.ToString()).Returns(new ApplicationUser
        {
            Id = customerId,
            FullName = "Test Customer",
            Email = "customer@example.com",
            PhoneNumber = "50000000",
        });
        return userManager;
    }

    private static void SeedUpgradePricing(AppDbContext db, Guid programId, Guid bundleId)
    {
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(SubscriptionPricingSettingsDefaults.Create(), SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromFraction(.3m),
            RMin = Percentage.FromFraction(.15m),
            DShort = 6,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = .1m,
            IsActive = true,
        });

        foreach (var (reference26, _) in new (decimal, RestaurantTier)[]
        {
            (104m, RestaurantTier.Basic),
            (143m, RestaurantTier.Platinum),
            (169m, RestaurantTier.Elite),
        })
        {
            var restaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "مطعم",
                LegalNameEn = "Restaurant",
                TradeNameAr = "مطعم",
                TradeNameEn = "Restaurant",
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true,
            };
            db.RestaurantProfiles.Add(restaurant);
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurant.Id,
                ProgramId = programId,
                BundleId = bundleId,
                IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                RestaurantId = restaurant.Id,
                ProgramId = programId,
                BundleId = bundleId,
                ReferencePrice26 = new Money(reference26, "KWD"),
                ContractedSupplyPrice26 = new Money(reference26 * 0.8m, "KWD"),
                LockStatus = PriceLockStatus.Locked,
                EffectiveFromUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        }
    }

    [Fact]
    public async Task Current_WhenPendingUpgradeExists_ReturnsPendingUpgradeNoticeAndDisablesUpgrade()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active, RestaurantTier.Basic);
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = subscription.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(15.5m, "KWD"),
            QuotedRemainingDays = 12,
            QuotedTotalDue = new Money(15.5m, "KWD"),
            QuoteJson = "{}",
            Status = VerificationStatus.Submitted,
        };
        db.AddRange(program, bundle, subscription, upgrade);
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = Assert.IsType<OkObjectResult>(await controller.Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.NotNull(screen.Subscription);
        Assert.NotNull(screen.Subscription.PendingUpgrade);
        Assert.Equal(upgrade.Id, screen.Subscription.PendingUpgrade.RequestId);
        Assert.Equal(RestaurantTier.Platinum, screen.Subscription.PendingUpgrade.ToTier);
        Assert.Equal(15.5m, screen.Subscription.PendingUpgrade.PriceDifference);
        Assert.False(screen.Subscription.Actions.CanUpgrade);
        Assert.Equal("upgrade_request_pending", screen.Subscription.Actions.UpgradeUnavailableReason);
    }

    private static MealMate.Domain.ProgramsSubscriptions.Program NewProgram() => new()
    {
        Code = "WEIGHT", NameAr = "خسارة الوزن", NameEn = "Weight loss", Status = ProgramStatus.Active, IsActive = true
    };

    private static Bundle NewBundle(Guid programId) => new()
    {
        ProgramId = programId, Code = "FOUR", NameAr = "باقة 4 وجبات", NameEn = "4 meals", MealsPerDay = 4,
        BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 1, Status = ProgramStatus.Active, IsActive = true
    };

    private static Subscription NewUpgradeableSubscription(Guid customerId, Guid programId, Guid bundleId) => new()
    {
        CustomerUserId = customerId,
        ProgramId = programId,
        BundleId = bundleId,
        SelectedTier = RestaurantTier.Basic,
        DurationDays = 26,
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 27),
        Status = SubscriptionStatus.Active,
        PaidAmount = new Money(80m, "KWD"),
    };

    private static Subscription NewSubscription(Guid customerId, Guid programId, Guid bundleId, SubscriptionStatus status, RestaurantTier tier = RestaurantTier.Platinum) => new()
    {
        CustomerUserId = customerId, ProgramId = programId, BundleId = bundleId, SelectedTier = tier,
        DurationDays = 2, StartDate = new DateOnly(2026, 8, 10), EndDate = new DateOnly(2026, 8, 15), Status = status,
        PaidAmount = new Money(49.99m, "KWD")
    };

    private static PaymentWebhookReceipt NewReceipt(Guid subscriptionId, string gateway, DateTime processedAtUtc, string invoiceId, string status = "PaymentVerified") => new()
    {
        Gateway = gateway,
        EventKey = $"evt-{Guid.NewGuid()}",
        SubscriptionId = subscriptionId,
        Status = status,
        PaymentId = $"pay-{Guid.NewGuid()}",
        InvoiceId = invoiceId,
        Amount = 49.99m,
        CurrencyCode = "KWD",
        ProcessedAtUtc = processedAtUtc,
        VerifiedAtUtc = processedAtUtc
    };

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());
}
