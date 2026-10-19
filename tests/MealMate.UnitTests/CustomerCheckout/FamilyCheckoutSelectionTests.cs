using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Explicit checkout selection (subscribe now vs later): selection lives on the
/// open draft batch, is never implied by plan completeness, charges only what
/// was selected, and keeps deferred members' plans untouched.
/// </summary>
public sealed class FamilyCheckoutSelectionTests
{
    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RetryPayment_InvalidRedirectDoesNotCreateDraftOrInvoice()
    {
        using var fx = await Fixture.CreateAsync();
        var old = await fx.Db.FamilyCheckoutBatches.SingleAsync();
        old.Status = FamilyCheckoutBatchStatus.Expired;
        var attempt = new FamilyCheckoutPaymentAttempt { BatchId = old.Id, AttemptKey = old.IdempotencyKey,
            ResultCode = "Initialized", RequestedAtUtc = Now };
        fx.Db.FamilyCheckoutPaymentAttempts.Add(attempt);
        await fx.Db.SaveChangesAsync();
        var result = await CreateRetryService(fx).RetryAsync(new FamilyStartCheckoutRequest("http://untrusted.invalid/callback", "https://localhost/error"));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("checkout.redirect_url_not_allowed");
        (await fx.Db.FamilyCheckoutBatches.CountAsync()).Should().Be(1);
        (await fx.Db.Subscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RetryPayment_PendingAttemptReusesSameUrlAndInvoice()
    {
        using var fx = await Fixture.CreateAsync();
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        var request = new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error", ClientContractVersion: 1);
        var started = await fx.Checkout.StartAsync(request, default);
        started.IsSuccess.Should().BeTrue(started.IsFailure ? started.Error.Code : "");
        var originalAttempt = await fx.Db.FamilyCheckoutPaymentAttempts
            .SingleAsync(x => x.BatchId == started.Value.BatchId);
        var service = CreateRetryService(fx);
        var retry = await service.RetryAsync(request with { IdempotencyKey = "new-client-key", BatchId = Guid.NewGuid() });
        retry.IsSuccess.Should().BeTrue(retry.IsFailure ? retry.Error.Code : "");
        retry.Value.PaymentUrl.Should().Be(started.Value.PaymentUrl);
        retry.Value.BatchId.Should().Be(started.Value.BatchId);
        var repeated = await service.RetryAsync(request);
        repeated.IsSuccess.Should().BeTrue();
        repeated.Value.BatchId.Should().Be(started.Value.BatchId);
        var attempts = await fx.Db.FamilyCheckoutPaymentAttempts
            .Where(x => x.BatchId == started.Value.BatchId).ToListAsync();
        attempts.Should().ContainSingle();
        attempts[0].Id.Should().Be(originalAttempt.Id);
        attempts[0].AttemptKey.Should().Be(originalAttempt.AttemptKey);
        attempts[0].GatewayPaymentUrl.Should().Be(originalAttempt.GatewayPaymentUrl);
        (await fx.Db.Subscriptions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task RetryPayment_DoesNotExposeAnotherManagersHousehold()
    {
        using var fx = await Fixture.CreateAsync();
        var result = await CreateRetryService(fx, Guid.NewGuid()).RetryAsync(new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error"));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.not_found");
        (await fx.Db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RetryPayment_ReviewRequiredDoesNotInitializeAnotherPayment()
    {
        using var fx = await Fixture.CreateAsync();
        (await fx.Db.FamilyCheckoutBatches.SingleAsync()).Status = FamilyCheckoutBatchStatus.ReviewRequired;
        await fx.Db.SaveChangesAsync();
        var result = await CreateRetryService(fx).RetryAsync(new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error"));
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.batch_review_required");
        (await fx.Db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RetryPayment_ExpiredAttemptRetainsOnlyItsSelectedMembers()
    {
        using var fx = await Fixture.CreateAsync();
        var old = await fx.Db.FamilyCheckoutBatches.SingleAsync();
        old.Status = FamilyCheckoutBatchStatus.Expired;
        old.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = fx.ManagerMember.Id, CheckoutSelected = true });
        old.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = fx.MemberA.Id, CheckoutSelected = true });
        old.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = fx.MemberB.Id, CheckoutSelected = false });
        old.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { AttemptKey = old.IdempotencyKey,
            ResultCode = "Initialized", GatewayPaymentUrl = "https://pay.example/expired", RequestedAtUtc = Now.AddMinutes(-20) });
        fx.Db.FamilyCheckoutBatchLines.AddRange(old.Lines);
        fx.Db.FamilyCheckoutPaymentAttempts.AddRange(old.PaymentAttempts);
        await fx.Db.SaveChangesAsync();
        var service = CreateRetryService(fx);
        var retry = await service.RetryAsync(new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error", ClientContractVersion: 1));
        retry.IsSuccess.Should().BeTrue(retry.IsFailure ? retry.Error.Code : "");
        retry.Value.BatchId.Should().NotBe(old.Id);
        var ids = await fx.Db.FamilyCheckoutBatchLines.Where(x => x.BatchId == retry.Value.BatchId && x.CheckoutSelected).Select(x => x.FamilyMemberId).ToListAsync();
        ids.Should().BeEquivalentTo(new[] { fx.ManagerMember.Id, fx.MemberA.Id });
        (await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberB.Id)).MemberSubscriptionId.Should().BeNull();
        (await fx.Db.FamilyCheckoutBatches.SingleAsync(x => x.Id == old.Id)).Status.Should().Be(FamilyCheckoutBatchStatus.Expired);
    }

    private static IFamilyPaymentRetryService CreateRetryService(Fixture fx, Guid? actorId = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(actorId ?? fx.Group.ManagerCustomerUserId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now);
        var flow = Substitute.For<IFamilySubscriptionFlowService>();
        flow.StartCheckoutAsync(Arg.Any<FamilyStartCheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => fx.Checkout.StartAsync(ci.Arg<FamilyStartCheckoutRequest>(), ci.Arg<CancellationToken>()));
        var transactions = Substitute.For<ICheckoutTransactionExecutor>();
        transactions.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result<FamilyStartCheckoutRequest>>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<Result<FamilyStartCheckoutRequest>>>>()(ci.Arg<CancellationToken>()));
        return new FamilyPaymentRetryService(fx.Db, user, clock, flow,
            Substitute.For<ICapacityHoldExpiryService>(), transactions, Substitute.For<IApplicationLockManager>());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task FirstSelectionEdit_PreservesOtherMembersLegacyDisplayedState(bool subscribeNow, int expectedCount)
    {
        using var fx = await Fixture.CreateAsync(explicitDeferredSelection: false);
        var result = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, subscribeNow), default);
        result.IsSuccess.Should().BeTrue();
        var ids = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        ids.Should().Contain(fx.ManagerMember.Id).And.Contain(fx.MemberB.Id);
        ids!.Contains(fx.MemberA.Id).Should().Be(subscribeNow);
        result.Value.SelectedNewSubscriberCount.Should().Be(expectedCount);

        var second = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberB.Id, false), default);
        second.IsSuccess.Should().BeTrue();
        ids = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        ids.Should().Contain(fx.ManagerMember.Id);
        ids!.Contains(fx.MemberA.Id).Should().Be(subscribeNow);
        ids.Contains(fx.MemberB.Id).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingExplicitSelection_EditPreservesDeferredAndSelectedOthers()
    {
        using var fx = await Fixture.CreateAsync(explicitDeferredSelection: false);
        var batch = new FamilyCheckoutBatch { FamilyGroupId = fx.Group.Id, PayerCustomerUserId = fx.Group.ManagerCustomerUserId,
            IdempotencyKey = "explicit-selection", Status = FamilyCheckoutBatchStatus.Draft, CreatedAtUtc = Now };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = fx.ManagerMember.Id, CheckoutSelected = true });
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = fx.MemberB.Id, CheckoutSelected = false });
        fx.Db.FamilyCheckoutBatches.Add(batch);
        await fx.Db.SaveChangesAsync();
        var result = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        result.IsSuccess.Should().BeTrue();
        var ids = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        ids.Should().BeEquivalentTo(new[] { fx.ManagerMember.Id, fx.MemberA.Id });
    }

    [Fact]
    public async Task FirstSelectionBaseline_DoesNotSelectPaidOrRemovedMembers()
    {
        using var fx = await Fixture.CreateAsync(explicitDeferredSelection: false);
        fx.ManagerMember.MemberSubscriptionId = Guid.NewGuid();
        (await fx.Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == fx.MemberB.Id)).RemovedAtUtc = Now;
        await fx.Db.SaveChangesAsync();
        var result = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        result.IsSuccess.Should().BeTrue();
        var ids = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        ids.Should().BeEquivalentTo(new[] { fx.MemberA.Id });
        result.Value.SelectedNewSubscriberCount.Should().Be(1);
    }

    [Fact]
    public async Task Select_DetachedMemberCannotBeAddedToManagerCheckout()
    {
        using var fx = await Fixture.CreateAsync();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == fx.MemberA.Id);
        access.DetachedFromFamilyManagement = true;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.member_detached");
        (await fx.Db.FamilyCheckoutBatchLines.AnyAsync(x => x.FamilyMemberId == fx.MemberA.Id && x.CheckoutSelected))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Select_CompletePlanMember_ChargesOnlySelected_AndKeepsDeferredPlanWithoutHold()
    {
        using var fx = await Fixture.CreateAsync();
        var memberA = fx.MemberA;
        var memberB = fx.MemberB;

        // Manager and member A fund the initial purchase; B stays deferred.
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, SubscribeNow: true), default))
            .IsSuccess.Should().BeTrue();
        var selection = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberA.Id, SubscribeNow: true), default);
        selection.IsSuccess.Should().BeTrue(selection.IsFailure ? selection.Error.Code : "");

        // The scoped quote charges only the two explicitly selected participants.
        var scope = new FamilyQuoteScope(
            await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(fx.Db, fx.Group, default),
            await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default));
        var quoted = await fx.Quote.QuoteAsync(fx.Group, default, scope);
        quoted.IsSuccess.Should().BeTrue();
        quoted.Value.Lines.Should().Contain(x => x.MemberId == memberA.Id);
        quoted.Value.Lines.Should().Contain(x => x.MemberId == fx.ManagerMember.Id);
        quoted.Value.Lines.Should().NotContain(x => x.MemberId == memberB.Id);
        quoted.Value.Subtotal.Should().Be(400m);

        // Checkout reserves only the selected manager and member; B remains deferred.
        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");

        var subscriptions = await fx.Db.Subscriptions.Where(x => x.FamilyGroupId == fx.Group.Id).ToListAsync();
        subscriptions.Should().HaveCount(2);
        subscriptions.Select(x => x.FamilyMemberId).Should().Contain([fx.ManagerMember.Id, memberA.Id]);

        // The deferred member keeps the plan, has no subscription and no capacity hold.
        var deferred = await fx.Db.FamilyMembers.AsNoTracking().SingleAsync(x => x.Id == memberB.Id);
        deferred.ProgramId.Should().Be(memberB.ProgramId);
        deferred.DurationDays.Should().Be(memberB.DurationDays);
        deferred.MemberSubscriptionId.Should().BeNull();
        (await fx.Db.CapacityHolds.CountAsync(x => x.SubscriptionId == deferred.MemberSubscriptionId)).Should().Be(0);
    }

    [Fact]
    public async Task Select_IncompleteSelectedMember_Blocks_While_UnselectedIncomplete_IsIgnored()
    {
        using var fx = await Fixture.CreateAsync(withIncompleteMemberC: true);
        var memberA = fx.MemberA;
        var incomplete = fx.MemberC!;

        // The manager and a complete member provide the two participants; an
        // additional selected incomplete member blocks checkout.
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default))
            .IsSuccess.Should().BeTrue();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberA.Id, true), default))
            .IsSuccess.Should().BeTrue();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(incomplete.Id, true), default))
            .IsSuccess.Should().BeTrue();
        var blocked = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        blocked.IsFailure.Should().BeTrue();
        blocked.Error!.Code.Should().Be("family.member_incomplete");

        // Unselecting the incomplete member restores the path for the rest of the family.
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(incomplete.Id, false), default))
            .IsSuccess.Should().BeTrue();
        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : "");
    }

    [Fact]
    public async Task Selection_CancelAndReselect_KeepsPlanAndIncrementsVersion()
    {
        using var fx = await Fixture.CreateAsync();
        var memberA = fx.MemberA;

        var first = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberA.Id, true), default);
        first.IsSuccess.Should().BeTrue();
        first.Value.CheckoutSelected.Should().BeTrue();
        first.Value.SelectedNewSubscriberCount.Should().Be(1);

        var cancelled = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberA.Id, false), default);
        cancelled.IsSuccess.Should().BeTrue();
        cancelled.Value.CheckoutSelected.Should().BeFalse();
        cancelled.Value.SelectedNewSubscriberCount.Should().Be(0);
        cancelled.Value.BatchVersion.Should().BeGreaterThan(first.Value.BatchVersion);

        // Cancelling selection keeps the member's plan.
        var member = await fx.Db.FamilyMembers.AsNoTracking().SingleAsync(x => x.Id == memberA.Id);
        member.ProgramId.Should().Be(memberA.ProgramId);
        member.BundleId.Should().Be(memberA.BundleId);
        member.SelectedTier.Should().Be(memberA.SelectedTier);
        member.DurationDays.Should().Be(memberA.DurationDays);

        var reselected = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberA.Id, true), default);
        reselected.IsSuccess.Should().BeTrue();
        reselected.Value.SelectedNewSubscriberCount.Should().Be(1);
    }

    [Fact]
    public async Task FirstPurchase_BelowPackageMinimum_BlocksCheckout()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1);
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default))
            .IsSuccess.Should().BeTrue();

        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);

        // The manager is funded but still needs one more qualifying participant.
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.min_members");
    }

    [Fact]
    public async Task FirstPurchase_TwoChildrenWithoutIndividualManager_RequiresManagerSubscription()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1);
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default))
            .IsSuccess.Should().BeTrue();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberB.Id, true), default))
            .IsSuccess.Should().BeTrue();

        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.manager_subscription_required");
    }

    [Fact]
    public async Task Checkout_RejectsRosterOverSelectedPackageCapacity_IncludingDeferredMembers()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1, packageMaxMembers: 2);
        var deferred = ReadyMember(fx.Group.Id, FamilyMemberRole.Member, "Deferred", 12);
        fx.Group.Members.Add(deferred);
        fx.Db.FamilyMembers.Add(deferred);
        await fx.Db.SaveChangesAsync();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default)).IsSuccess.Should().BeTrue();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default)).IsSuccess.Should().BeTrue();

        var selectedIds = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        var scope = new FamilyQuoteScope(
            await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(fx.Db, fx.Group, default), selectedIds);
        var quote = await fx.Quote.QuoteAsync(fx.Group, default, scope);
        quote.IsSuccess.Should().BeTrue();
        quote.Value.Lines.Should().HaveCount(2);
        quote.Value.OccupiedSeatCount.Should().Be(4);
        quote.Value.DeferredMemberCount.Should().Be(2);

        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.max_members");
    }

    [Fact]
    public async Task LiveManagerIndividualPlusOneFamilyMember_AllowsFirstPurchase_AndLeavesIndividualUntouched()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1, packageMaxMembers: 2);
        var personal = new Subscription
        {
            CustomerUserId = fx.Group.ManagerCustomerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Platinum,
            DurationDays = 26,
            IncludeFriday = true,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 25),
            PaidAmount = new Money(375m, "KWD"),
        };
        fx.Group.ManagerIndividualSubscriptionId = personal.Id;
        fx.Group.ManagerIndividualSeatExempt = true;
        fx.ManagerMember.MemberSubscriptionId = personal.Id;
        fx.ManagerMember.MemberSubscription = personal;
        var personalDay = new SubscriptionDay
        {
            SubscriptionId = personal.Id,
            ServiceDate = new DateOnly(2026, 8, 20),
            Status = SubscriptionDayStatus.CustomerSelected,
            RestaurantId = Guid.NewGuid(),
        };
        fx.Db.Subscriptions.Add(personal);
        fx.Db.SubscriptionDays.Add(personalDay);
        await fx.Db.SaveChangesAsync();
        var originalStart = personal.StartDate;
        var originalEnd = personal.EndDate;
        var originalAmount = personal.PaidAmount.Amount;

        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default)).IsSuccess.Should().BeTrue();
        var selectedIds = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        var scope = new FamilyQuoteScope(
            await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(fx.Db, fx.Group, default), selectedIds);
        var quote = await fx.Quote.QuoteAsync(fx.Group, default, scope);

        quote.IsSuccess.Should().BeTrue();
        quote.Value.ParticipantCount.Should().Be(2);
        quote.Value.OccupiedSeatCount.Should().Be(2);
        quote.Value.Lines.Should().ContainSingle(x => x.MemberId == fx.MemberA.Id);

        var checkout = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        checkout.IsSuccess.Should().BeTrue(checkout.IsFailure ? checkout.Error.Code : "");
        var retained = await fx.Db.Subscriptions.SingleAsync(x => x.Id == personal.Id);
        retained.FamilyGroupId.Should().BeNull();
        retained.FamilyMemberId.Should().BeNull();
        retained.StartDate.Should().Be(originalStart);
        retained.EndDate.Should().Be(originalEnd);
        retained.DurationDays.Should().Be(26);
        retained.IncludeFriday.Should().BeTrue();
        retained.Status.Should().Be(SubscriptionStatus.Active);
        retained.PaidAmount.Amount.Should().Be(originalAmount);
        var day = await fx.Db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == personal.Id);
        day.ServiceDate.Should().Be(personalDay.ServiceDate);
        day.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
    }

    [Fact]
    public async Task SelectingExpiredRetainedManager_ForFamilyPurchase_ConsumesItsSeatExactlyOnce()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1);
        var personal = new Subscription
        {
            CustomerUserId = fx.Group.ManagerCustomerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.Expired,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 10),
            PaidAmount = new Money(100m, "KWD"),
        };
        fx.Group.ManagerIndividualSubscriptionId = personal.Id;
        fx.Group.ManagerIndividualSeatExempt = true;
        fx.ManagerMember.MemberSubscriptionId = personal.Id;
        fx.ManagerMember.MemberSubscription = personal;
        fx.Db.Subscriptions.Add(personal);
        await fx.Db.SaveChangesAsync();

        var selected = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);
        selected.IsSuccess.Should().BeTrue(selected.IsFailure ? selected.Error.Code : "");
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default))
            .IsSuccess.Should().BeTrue();

        fx.Group.ManagerIndividualSeatExempt.Should().BeFalse();
        var context = await fx.Participation.GetAsync(fx.Group,
            await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default), default);
        context.OccupiedSeats.Should().Be(3);
        context.CurrentPurchaseCount.Should().Be(1);
        context.QualifyingParticipantCount.Should().Be(1);
    }

    [Fact]
    public async Task SelectingExpiredRetainedManager_OverPackageCapacity_DoesNotConsumeExemption()
    {
        using var fx = await Fixture.CreateAsync(packageMinMembers: 1, packageMaxMembers: 2);
        var personal = new Subscription
        {
            CustomerUserId = fx.Group.ManagerCustomerUserId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            Status = SubscriptionStatus.Expired,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 10),
            PaidAmount = new Money(100m, "KWD"),
        };
        fx.Group.ManagerIndividualSubscriptionId = personal.Id;
        fx.Group.ManagerIndividualSeatExempt = true;
        fx.ManagerMember.MemberSubscriptionId = personal.Id;
        fx.ManagerMember.MemberSubscription = personal;
        fx.Db.Subscriptions.Add(personal);
        await fx.Db.SaveChangesAsync();

        var selected = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);

        selected.IsFailure.Should().BeTrue();
        selected.Error!.Code.Should().Be("family.max_members");
        fx.Group.ManagerIndividualSeatExempt.Should().BeTrue();
    }

    [Fact]
    public async Task SelectionDoesNotChangeActivePackageOrDiscountSnapshot()
    {
        var fx = await Fixture.CreateActiveWithPaidManagerAsync();
        var packageId = fx.Group.SelectedFamilyPackageId;
        var discountSnapshot = fx.Group.DiscountPercentSnapshot;
        var newcomer = ReadyMember(fx.Group.Id, FamilyMemberRole.Member, "New member", 12);
        fx.Group.Members.Add(newcomer);
        fx.Db.FamilyMembers.Add(newcomer);
        await fx.Db.SaveChangesAsync();

        var selected = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(newcomer.Id, true), default);

        selected.IsSuccess.Should().BeTrue(selected.IsFailure ? selected.Error.Code : "");
        fx.Group.SelectedFamilyPackageId.Should().Be(packageId);
        fx.Group.DiscountPercentSnapshot.Should().Be(discountSnapshot);
    }

    [Fact]
    public async Task DeferredAdd_InActiveFamily_HasZeroFees_AndSelection_IsRefusedForPaidMember()
    {
        var fx = await Fixture.CreateActiveWithPaidManagerAsync();

        // The paid manager cannot be selected for a new purchase.
        var refused = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);
        refused.IsFailure.Should().BeTrue();
        refused.Error!.Code.Should().Be("family.already_subscribed");

        // A new deferred member with a complete plan and no selection owes nothing.
        var newcomer = ReadyMember(fx.Group.Id, FamilyMemberRole.Member, "Newcomer", 12);
        fx.Group.Members.Add(newcomer);
        fx.Db.FamilyMembers.Add(newcomer);
        await fx.Db.SaveChangesAsync();

        // The manager explicitly declines subscribe-now for the newcomer: the draft
        // batch is created with an unselected line, so the member owes nothing.
        var deferred = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(newcomer.Id, SubscribeNow: false), default);
        deferred.IsSuccess.Should().BeTrue(deferred.IsFailure ? deferred.Error.Code : "");
        deferred.Value.CheckoutSelected.Should().BeFalse();

        var batchCount = await fx.Db.FamilyCheckoutBatches.CountAsync();
        batchCount.Should().Be(1, "the deferred selection should have created the draft batch");
        var selected = await FamilyCheckoutSelectionService.GetSelectedMemberIdsOrNullAsync(fx.Db, fx.Group.Id, default);
        selected.Should().NotBeNull("the draft batch exists");
        selected!.Should().BeEmpty();
        var scope = new FamilyQuoteScope(
            await FamilyCheckoutGate.ActiveAccessMemberIdsAsync(fx.Db, fx.Group, default),
            selected);
        var quoted = await fx.Quote.QuoteAsync(fx.Group, default, scope);
        quoted.IsSuccess.Should().BeTrue(quoted.IsFailure ? quoted.Error.Code : "");
        quoted.Value.Lines.Should().BeEmpty();
        quoted.Value.Subtotal.Should().Be(0m);
        quoted.Value.FinalPrice.Should().Be(0m);
    }

    [Fact]
    public async Task Checkout_WithoutExplicitSelection_IsRefused_NotAutoPriced()
    {
        // B01: completing plans alone never implies buying — checkout with no
        // subscribe-now selection is refused instead of pricing every member.
        using var fx = await Fixture.CreateAsync();
        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.checkout_no_selection");
        (await fx.Db.Subscriptions.CountAsync(x => x.FamilyGroupId == fx.Group.Id)).Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Selection_SubscribeNow_ResumesPendingPaymentWithoutChangingInvoice(bool alreadySelected)
    {
        // B02: the frozen PaymentPending batch may not change participants.
        using var fx = await Fixture.CreateAsync();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default))
            .IsSuccess.Should().BeTrue();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default))
            .IsSuccess.Should().BeTrue();
        var checkout = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        checkout.IsSuccess.Should().BeTrue(checkout.IsFailure ? checkout.Error.Code : "");

        var memberId = alreadySelected ? fx.MemberA.Id : fx.MemberB.Id;
        var before = await fx.Db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.BatchId == checkout.Value.BatchId)
            .Select(x => new { x.FamilyMemberId, x.CheckoutSelected }).ToListAsync();
        var resumed = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberId, true), default);
        resumed.IsSuccess.Should().BeTrue(resumed.IsFailure ? resumed.Error.Code : "");
        resumed.Value.BatchId.Should().Be(checkout.Value.BatchId!.Value);
        resumed.Value.CheckoutSelected.Should().Be(alreadySelected);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(resumed.Value));
        json.RootElement.GetProperty("PendingCheckout").GetProperty("PaymentUrl").GetString().Should().Be(checkout.Value.PaymentUrl);
        json.RootElement.GetProperty("NextScreen").GetString().Should().Be("family_payment");
        json.RootElement.GetProperty("SelectionApplied").GetBoolean().Should().BeFalse();
        var repeated = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(memberId, true), default);
        repeated.IsSuccess.Should().BeTrue();
        (await fx.Db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.BatchId == checkout.Value.BatchId)
            .Select(x => new { x.FamilyMemberId, x.CheckoutSelected }).ToListAsync()).Should().BeEquivalentTo(before);
        (await fx.Db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(1);
        (await fx.Db.Subscriptions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task PendingSelection_DeselectAndForeignMemberCannotAlterOrReadPayment()
    {
        using var fx = await Fixture.CreateAsync();
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        (await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default)).IsSuccess.Should().BeTrue();
        var deselect = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, false), default);
        deselect.IsFailure.Should().BeTrue();
        deselect.Error.Code.Should().Be("family.checkout_pending");
        var foreign = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(Guid.NewGuid(), true), default);
        foreign.IsFailure.Should().BeTrue();
        foreign.Error.Code.Should().Be("family.not_found");
        (await fx.Db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Selection_ExpiredPendingPaymentIsRecoveredBeforeEditing()
    {
        using var fx = await Fixture.CreateAsync();
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.ManagerMember.Id, true), default);
        await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        var checkout = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error", ClientContractVersion: 1), default);
        checkout.IsSuccess.Should().BeTrue();
        var old = await fx.Db.FamilyCheckoutBatches.SingleAsync(x => x.Id == checkout.Value.BatchId);
        old.ExpiresAtUtc = Now.AddMinutes(-1);
        foreach (var hold in await fx.Db.CapacityHolds.ToListAsync()) hold.ExpiresAtUtc = Now.AddMinutes(-1);
        foreach (var sub in await fx.Db.Subscriptions.ToListAsync()) sub.LastOperationAtUtc = Now.AddMinutes(-20);
        await fx.Db.SaveChangesAsync();
        var selected = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberB.Id, true), default);
        selected.IsSuccess.Should().BeTrue(selected.IsFailure ? selected.Error.Code : "");
        selected.Value.CheckoutSelected.Should().BeTrue();
        selected.Value.PendingCheckout.Should().BeNull();
        selected.Value.SelectionApplied.Should().BeTrue();
        selected.Value.RetryPaymentEndpoint.Should().Be(FamilyCheckoutResumeReader.RetryEndpoint);
        old.Status.Should().Be(FamilyCheckoutBatchStatus.Expired);
        (await fx.Db.FamilyCheckoutPaymentAttempts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReviewRequired_HidesRetryEvenWithAnOlderExpiredInvoice()
    {
        using var fx = await Fixture.CreateAsync();
        var old = await fx.Db.FamilyCheckoutBatches.SingleAsync();
        old.Status = FamilyCheckoutBatchStatus.Expired;
        fx.Db.FamilyCheckoutPaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { BatchId = old.Id,
            AttemptKey = old.IdempotencyKey, ResultCode = "Initialized", RequestedAtUtc = Now });
        fx.Db.FamilyCheckoutBatches.Add(new FamilyCheckoutBatch { FamilyGroupId = fx.Group.Id,
            PayerCustomerUserId = fx.Group.ManagerCustomerUserId, IdempotencyKey = "review", Status = FamilyCheckoutBatchStatus.ReviewRequired });
        await fx.Db.SaveChangesAsync();
        (await FamilyCheckoutResumeReader.CanRetryAsync(fx.Db, fx.Group.Id, default)).Should().BeFalse();
        var result = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.batch_review_required");
    }

    [Fact]
    public async Task New_Selection_After_Expired_Batch_Starts_Fresh_Cycle()
    {
        // B03: the closed cycle's unique idempotency key never blocks a new draft.
        using var fx = await Fixture.CreateAsync();
        (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default))
            .IsSuccess.Should().BeTrue();
        var firstBatchId = (await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberB.Id, false), default)).Value.BatchId;

        var batch = await fx.Db.FamilyCheckoutBatches.SingleAsync(x => x.Id == firstBatchId);
        batch.Status = FamilyCheckoutBatchStatus.Expired;
        await fx.Db.SaveChangesAsync();

        var next = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        next.IsSuccess.Should().BeTrue(next.IsFailure ? next.Error.Code : "");
        next.Value.BatchId.Should().NotBe(firstBatchId);
        (await fx.Db.FamilyCheckoutBatches.CountAsync(x => x.FamilyGroupId == fx.Group.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Plan_Change_Bumps_Batch_Version_And_Stale_Quote_Is_Refused()
    {
        // B06: a checkout quoting the pre-change version is refused.
        using var fx = await Fixture.CreateAsync();
        var selection = await fx.Selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fx.MemberA.Id, true), default);
        selection.IsSuccess.Should().BeTrue();
        var staleVersion = selection.Value.BatchVersion;

        // A draft mutation (member add) bumps the version.
        var newcomer = ReadyMember(fx.Group.Id, FamilyMemberRole.Member, "Newcomer", 12, addressId: null);
        fx.Group.Members.Add(newcomer);
        fx.Db.FamilyMembers.Add(newcomer);
        await fx.Db.SaveChangesAsync();
        await FamilyCheckoutBatchVersioning.BumpDraftVersionAsync(fx.Db, fx.Group.Id, default);

        var result = await fx.Checkout.StartAsync(new FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error",
            BatchId: selection.Value.BatchId, BatchVersion: staleVersion, ClientContractVersion: 1), default);
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.quote_changed");
    }

    private static FamilyMember ReadyMember(Guid groupId, FamilyMemberRole role, string name, int days, bool complete = true, Guid? addressId = null)
        => new()
        {
            FamilyGroupId = groupId,
            Role = role,
            DisplayName = name,
            RelationshipCode = role == FamilyMemberRole.Manager ? "self" : "spouse",
            Gender = FamilyMemberGender.Male,
            AgeYears = 30,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            DeliveryAddressId = addressId,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = complete ? Guid.NewGuid() : null,
            BundleId = complete ? Guid.NewGuid() : null,
            SelectedTier = complete ? RestaurantTier.Basic : null,
            DurationDays = complete ? days : null,
            IncludeFriday = false,
            StartDate = DateOnly.FromDateTime(Now),
        };

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db = null!;
        public FamilyGroup Group = null!;
        public FamilyMember ManagerMember = null!;
        public FamilyMember MemberA = null!;
        public FamilyMember MemberB = null!;
        public FamilyMember? MemberC;
        public FamilyCheckoutSelectionService Selection = null!;
        public FamilyQuoteService Quote = null!;
        public FamilyParticipationContextReader Participation = null!;
        public FamilyCheckoutService Checkout = null!;

        public static async Task<Fixture> CreateAsync(bool withIncompleteMemberC = false, int packageMinMembers = 1, int packageMaxMembers = 12, bool explicitDeferredSelection = true)
        {
            var fx = new Fixture();
            await fx.BuildAsync(withIncompleteMemberC, packageMinMembers, packageMaxMembers, explicitDeferredSelection);
            return fx;
        }

        public static async Task<Fixture> CreateActiveWithPaidManagerAsync()
        {
            var fx = new Fixture();
            await fx.BuildActiveAsync();
            return fx;
        }

        private async Task BuildAsync(bool withIncompleteMemberC, int packageMinMembers, int packageMaxMembers, bool explicitDeferredSelection)
        {
            Db = CreateDb();
            var managerId = Guid.NewGuid();
            var memberAUserId = Guid.NewGuid();
            var address = new CustomerAddress
            {
                UserId = managerId,
                Label = "Home",
                AddressLine = "Kuwait City",
                RegionId = Guid.NewGuid(),
                IsDefault = true,
            };
            Db.CustomerAddresses.Add(address);
            Db.CustomerProfiles.Add(new CustomerProfile { UserId = managerId, AllergiesCompletedAtUtc = Now });
            Db.CustomerProfiles.Add(new CustomerProfile { UserId = memberAUserId, AllergiesCompletedAtUtc = Now });

            Group = new FamilyGroup
            {
                ManagerCustomerUserId = managerId,
                Name = "Family",
                Status = FamilyGroupStatus.Draft,
                SelectedFamilyPackageId = Guid.NewGuid(),
                DiscountPercentSnapshot = 0m,
            };
            ManagerMember = ReadyMember(Group.Id, FamilyMemberRole.Manager, "Manager", 26, addressId: address.Id);
            MemberA = ReadyMember(Group.Id, FamilyMemberRole.Member, "A", 26, addressId: address.Id);
            MemberB = ReadyMember(Group.Id, FamilyMemberRole.Member, "B", 12, addressId: address.Id);
            Group.Members.Add(ManagerMember);
            Group.Members.Add(MemberA);
            Group.Members.Add(MemberB);
            if (withIncompleteMemberC)
            {
                MemberC = ReadyMember(Group.Id, FamilyMemberRole.Member, "C", 12, complete: false);
                Group.Members.Add(MemberC);
            }
            Db.FamilyGroups.Add(Group);
            Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = ManagerMember.Id,
                HasSeparateAccount = false,
                LinkedCustomerUserId = managerId,
            });
            Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = MemberA.Id,
                HasSeparateAccount = true,
                LinkedCustomerUserId = memberAUserId,
            });
            Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = MemberB.Id,
                HasSeparateAccount = true,
                LinkedCustomerUserId = Guid.NewGuid(),
            });
            if (MemberC is not null)
            {
                Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
                {
                    FamilyMemberId = MemberC.Id,
                    HasSeparateAccount = true,
                    LinkedCustomerUserId = Guid.NewGuid(),
                });
            }
            await Db.SaveChangesAsync();

            // These scenarios start with an explicit subscribe-later choice, rather
            // than the legacy no-batch state advertised as selected by current/summary.
            if (explicitDeferredSelection)
            {
                Db.FamilyCheckoutBatches.Add(new FamilyCheckoutBatch
                {
                    FamilyGroupId = Group.Id, PayerCustomerUserId = managerId,
                    IdempotencyKey = "explicit-deferred", Status = FamilyCheckoutBatchStatus.Draft, CreatedAtUtc = Now,
                });
                await Db.SaveChangesAsync();
            }

            BuildServices(managerId, packageMinMembers, packageMaxMembers);
            await Task.CompletedTask;
        }

        private async Task BuildActiveAsync()
        {
            Db = CreateDb();
            var managerId = Guid.NewGuid();
            Group = new FamilyGroup
            {
                ManagerCustomerUserId = managerId,
                Name = "Active Family",
                Status = FamilyGroupStatus.Active,
                SelectedFamilyPackageId = Guid.NewGuid(),
                DiscountPercentSnapshot = 0.10m,
            };
            ManagerMember = ReadyMember(Group.Id, FamilyMemberRole.Manager, "Manager", 26);
            var managerSub = new Subscription
            {
                CustomerUserId = managerId,
                ProgramId = ManagerMember.ProgramId!.Value,
                BundleId = ManagerMember.BundleId!.Value,
                SelectedTier = RestaurantTier.Basic,
                DurationDays = 26,
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 26),
                Status = SubscriptionStatus.Active,
                PaidAmount = new Money(200m, "KWD"),
            };
            ManagerMember.MemberSubscriptionId = managerSub.Id;
            ManagerMember.MemberSubscription = managerSub;
            Db.Subscriptions.Add(managerSub);
            Group.Members.Add(ManagerMember);
            Db.FamilyGroups.Add(Group);
            Db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = ManagerMember.Id,
                HasSeparateAccount = false,
                LinkedCustomerUserId = managerId,
            });
            await Db.SaveChangesAsync();

            BuildServices(managerId, packageMinMembers: 1, packageMaxMembers: 12);
            await Task.CompletedTask;
        }

        private void BuildServices(Guid managerId, int packageMinMembers, int packageMaxMembers)
        {
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(managerId);
            currentUser.UserType.Returns(UserType.Customer);

            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(Now);
            clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

            var pricingSender = Substitute.For<ISender>();
            pricingSender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Result<PricingEngineResult>.Success(new PricingEngineResult(
                    1, 26, 0.15m, 200m, 0, 0, 0, 200m, 0, 200m, 0, 0, 0, false))));
            var settings = new TestSettingsReader(new FamilyPricingSettings
            {
                DiscountPercent = 0m,
                MinMembersForDiscount = packageMinMembers,
                MaxMembers = packageMaxMembers,
                Packages =
                [
                    new FamilySizePackageSettings
                    {
                        Id = Group.SelectedFamilyPackageId ?? Guid.NewGuid(),
                        NameAr = "ع",
                        NameEn = "P",
                        MinMembers = packageMinMembers,
                        MaxMembers = packageMaxMembers,
                        DiscountPercent = 0m,
                        IsActive = true,
                    },
                ],
            });
            var schedule = Substitute.For<ISubscriptionScheduleService>();
            schedule.GetFridayAddonDays(Arg.Any<int>()).Returns(0);
            var participation = new FamilyParticipationContextReader(Db, clock, settings);
            Participation = participation;
            Selection = new FamilyCheckoutSelectionService(Db, currentUser, clock, new InlineTransactions(), Substitute.For<IApplicationLockManager>(), participation,
                new CapacityHoldExpiryService(Db, new InlineTransactions(), Substitute.For<ICapacityConcurrencyLock>(), clock));
            Quote = new FamilyQuoteService(pricingSender, settings, schedule, Db, participation);

            var checkoutSchedule = Substitute.For<ISubscriptionScheduleService>();
            var serviceDates = new List<DateOnly> { new(2026, 8, 19), new(2026, 8, 20) };
            checkoutSchedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(serviceDates);
            var restaurantId = Guid.NewGuid();
            var menuVersionId = Guid.NewGuid();
            var eligibility = Substitute.For<IRestaurantEligibilityService>();
            eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
                .Returns([
                    new RestaurantEligibilityDto(
                        restaurantId, "مطعم", "Restaurant", true, [],
                        serviceDates.Select(d => new EligibleMenuDateDto(d, menuVersionId)).ToList()),
                ]);
            var pool = Substitute.For<IRestaurantPoolSnapshotService>();
            pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));

            var checkoutSender = Substitute.For<ISender>();
            checkoutSender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var cmd = ci.Arg<CreateSubscriptionCommand>();
                    var sub = new Subscription
                    {
                        CustomerUserId = cmd.CustomerUserId,
                        ProgramId = cmd.ProgramId,
                        BundleId = cmd.BundleId,
                        SelectedTier = cmd.SelectedTier,
                        DurationDays = cmd.DurationDays,
                        IncludeFriday = cmd.IncludeFriday,
                        StartDate = cmd.StartDate,
                        EndDate = cmd.StartDate.AddDays(cmd.DurationDays - 1),
                        Status = SubscriptionStatus.PendingPayment,
                        RegionId = cmd.RegionId,
                        DeliveryAddress = cmd.DeliveryAddress,
                        PaidAmount = new Money(200m, "KWD"),
                    };
                    Db.Subscriptions.Add(sub);
                    foreach (var date in serviceDates)
                    {
                        Db.CapacityHolds.Add(new CapacityHold
                        {
                            RestaurantId = restaurantId,
                            SubscriptionId = sub.Id,
                            MenuPlanVersionId = menuVersionId,
                            ServiceDate = date,
                            Quantity = 1,
                            Status = CapacityHoldStatus.PendingPayment,
                            ExpiresAtUtc = Now.AddMinutes(15),
                        });
                    }
                    Db.SaveChanges();
                    return Task.FromResult(Result<SubscriptionDto>.Success(new SubscriptionDto(
                        sub.Id, sub.CustomerUserId, sub.ProgramId, "", "", sub.BundleId, "", "",
                        sub.SelectedTier, sub.DurationDays, sub.Status, sub.StartDate, sub.EndDate,
                        sub.PaidAmount.Amount, sub.PaidAmount.CurrencyCode, sub.DeliveryAddress, sub.CorrelationId,
                        DateTime.UtcNow, [], [], sub.IncludeFriday)));
                });

            var payments = Substitute.For<IPaymentGatewayService>();
            payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<PaymentGatewayMethod>)[
                    new(1, "visa", "فيزا", "Visa", null, null, "KWD", false),
                    new(2, "mastercard", "ماستركارد", "Mastercard", null, null, "KWD", false),
                    new(3, "knet", "كي نت", "KNET", null, null, "KWD", false),
                ]);
            payments.ExecutePaymentAsync(
                    Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Group.Id.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new PaymentCreationReceipt("provider-id", "https://pay.example/invoice?paymentId=PAY-SELECTION", "test-track"));

            var users = Substitute.For<IUserAccountService>();
            users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
                .Returns(new UserAccountDto(
                    managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                    null, true, true, DateTime.UtcNow, null));

            Checkout = new FamilyCheckoutService(
                Db, currentUser, users, Quote, checkoutSchedule, eligibility,
                Substitute.For<ICapacityConcurrencyLock>(), new InlineTransactions(), pool,
                payments, checkoutSender, clock, new UnboundedCapacityReader(), Substitute.For<IApplicationLockManager>());
        }

        public void Dispose() => Db.Dispose();

        private static AppDbContext CreateDb()
            => new(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                Substitute.For<IDomainEventDispatcher>());

        private sealed class InlineTransactions : ICheckoutTransactionExecutor
        {
            public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
                => operation(cancellationToken);
        }

        private sealed class UnboundedCapacityReader : IRestaurantCapacityReader
        {
            public Task<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>> ReadAsync(
                IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)> keys, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>>(
                    keys.ToDictionary(
                        key => key,
                        key => new RestaurantCapacityUnits(key.RestaurantId, key.ServiceDate, int.MaxValue, 0, 0, 0, 0, 0, 0, 0, 0)));
        }

        private sealed class TestSettingsReader : IFamilyPricingSettingsReader
        {
            private readonly FamilyPricingSettings _settings;
            public TestSettingsReader(FamilyPricingSettings settings) => _settings = settings;
            public Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(_settings);
        }

    }
}
