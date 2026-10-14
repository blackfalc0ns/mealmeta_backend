using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using FamilyMembersQuery = MealMate.Application.Features.FamilyManager.GetFamilyMembersQuery;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.FamilyManager;

public sealed class FamilyManagerHandlerTests
{
    [Fact]
    public async Task Permissions_update_requires_version_and_rejects_stale_write_without_changing_grants()
    {
        var fx = await SeedAsync();
        var perms = await fx.Db.FamilyMemberPermissions.SingleAsync(x => x.FamilyMemberId == fx.MemberId);
        perms.Version = 4;
        perms.ViewCalendar = true;
        await fx.Db.SaveChangesAsync();
        var request = new UpdateFamilyMemberPermissionsRequest(false, false, false, false, false, false, false, false);
        var missing = await fx.Handlers.Handle(new UpdateFamilyMemberPermissionsCommand(fx.MemberId, request), CancellationToken.None);
        missing.IsFailure.Should().BeTrue();
        var stale = await fx.Handlers.Handle(new UpdateFamilyMemberPermissionsCommand(fx.MemberId, request with { ExpectedVersion = 3 }), CancellationToken.None);
        stale.IsFailure.Should().BeTrue();
        perms.ViewCalendar.Should().BeTrue();
        perms.Version.Should().Be(4);
        var saved = await fx.Handlers.Handle(new UpdateFamilyMemberPermissionsCommand(fx.MemberId,
            request with { ExpectedVersion = 4, ViewCalendar = false, ViewInvoices = true, ViewFamilyPlans = true, ConfirmMeals = true }), CancellationToken.None);
        saved.IsSuccess.Should().BeTrue();
        saved.Value.Version.Should().Be(5);
        saved.Value.ViewInvoices.Should().BeTrue();
        saved.Value.ViewFamilyPlans.Should().BeTrue();
        saved.Value.ConfirmMeals.Should().BeTrue();
        saved.Value.ViewCalendar.Should().BeFalse();
    }
    [Theory]
    [InlineData(FamilyGroupStatus.Draft)]
    [InlineData(FamilyGroupStatus.CheckoutPending)]
    public async Task Before_purchase_roster_and_management_remain_available(FamilyGroupStatus status)
    {
        var fx = await SeedAsync();
        var group = await fx.Db.FamilyGroups.SingleAsync();
        group.Status = status;
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = null;
        var former = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        former.Status = SubscriptionStatus.PaymentFailed;
        fx.Db.SubscriptionDays.RemoveRange(await fx.Db.SubscriptionDays.Where(x => x.SubscriptionId == former.Id).ToListAsync());
        await fx.Db.SaveChangesAsync();
        var home = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        home.IsSuccess.Should().BeTrue();
        home.Value.TodaySchedule.Should().NotContain(x => x.MemberId == fx.MemberId);
        var list = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);
        list.IsSuccess.Should().BeTrue();
        var detail = await fx.Handlers.Handle(new GetFamilyMemberQuery(fx.MemberId), CancellationToken.None);
        detail.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Today_schedule_uses_Kuwait_date_and_only_actual_service_days()
    {
        var fx = await SeedAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 24));
        fx.Db.SubscriptionDays.RemoveRange(await fx.Db.SubscriptionDays.ToListAsync());
        fx.Db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = fx.MemberSubscriptionId,
            ServiceDate = new DateOnly(2026, 8, 25), Status = SubscriptionDayStatus.Open });
        await fx.Db.SaveChangesAsync();
        var pricing = Substitute.For<IFamilyPricingSettingsReader>();
        pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyManagerHandlers(fx.Db, fx.User, clock, pricing, fx.Users);
        var home = await handler.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        home.Value.TodaySchedule.Should().ContainSingle(x => x.MemberId == fx.MemberId);
        home.Value.Members.Single(x => x.MemberId == fx.MemberId).RemainingDays.Should().Be(1);
    }

    [Fact]
    public async Task Members_list_exposes_real_plan_usage_and_today_selection_summary()
    {
        var fx = await SeedAsync();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == fx.MemberId);
        access.HasSeparateAccount = true;
        access.IsPaused = true;
        access.LinkedCustomerUserId = fx.LinkedUserId;
        fx.Db.CustomerProfiles.AddRange(
            new MealMate.Domain.IdentityAccess.CustomerProfile { UserId = fx.LinkedUserId, ProfileImageStorageKey = "uploads/customers/profile/member.png" },
            new MealMate.Domain.IdentityAccess.CustomerProfile { UserId = fx.User.UserId!.Value, ProfileImageStorageKey = "uploads/customers/profile/manager.png" });
        await fx.Db.SaveChangesAsync();
        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var member = result.Value.Single(x => x.MemberId == fx.MemberId);
        member.ProfileImageUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fcustomers%2Fprofile%2Fmember.png");
        member.HasSeparateAccount.Should().BeTrue();
        member.IsPaused.Should().BeTrue();
        member.PlanSummary.Should().BeEquivalentTo(new FamilyPlanSummaryDto(
            3, 26, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 1),
            7, 0, 0m));
        member.TodayMealSelection.Should().BeEquivalentTo(new FamilyTodayMealSelectionDto(
            new DateOnly(2026, 8, 25), nameof(SubscriptionDayStatus.Open), 0, 3, false));
        result.Value.Should().NotContain(x => x.MemberId == fx.ManagerMemberId);

        var switcher = await fx.Handlers.Handle(new GetFamilySwitcherQuery(), CancellationToken.None);
        switcher.IsSuccess.Should().BeTrue();
        var manager = switcher.Value.Single(x => x.MemberId == fx.ManagerMemberId);
        manager.ProfileImageUrl.Should().Be("/api/v1/uploads/content?key=uploads%2Fcustomers%2Fprofile%2Fmanager.png");
        manager.IsManager.Should().BeTrue();
    }

    [Fact]
    public async Task Deferred_member_plan_keeps_progress_and_unpersisted_dates_null()
    {
        var fx = await SeedAsync();
        var member = new FamilyMember
        {
            FamilyGroupId = fx.GroupId, Role = FamilyMemberRole.Member, DisplayName = "Deferred",
            RelationshipCode = "son", Gender = FamilyMemberGender.Male, AgeYears = 9,
            NutritionalGoalCode = "balanced", ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.InProgress, ProgramId = (await fx.Db.Programs.SingleAsync()).Id,
            BundleId = (await fx.Db.Bundles.SingleAsync()).Id, DurationDays = 21,
            StartDate = new DateOnly(2026, 11, 1)
        };
        fx.Db.FamilyMembers.Add(member);
        await fx.Db.SaveChangesAsync();
        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);

        var summary = result.Value.Single(x => x.MemberId == member.Id).PlanSummary;
        summary.Should().BeEquivalentTo(new FamilyPlanSummaryDto(3, 21,
            new DateOnly(2026, 11, 1), null, null, null, null, null));
        result.Value.Single(x => x.MemberId == member.Id).TodayMealSelection.Should().BeNull();
    }

    [Fact]
    public async Task Delivered_usage_counts_only_actual_delivered_calendar_days()
    {
        var fx = await SeedAsync();
        var days = await fx.Db.SubscriptionDays.Where(x => x.SubscriptionId == fx.MemberSubscriptionId)
            .OrderBy(x => x.ServiceDate).ToListAsync();
        days[0].Status = SubscriptionDayStatus.Delivered;
        days[1].Status = SubscriptionDayStatus.Cancelled;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);
        result.Value.Single(x => x.MemberId == fx.MemberId).PlanSummary.Should().BeEquivalentTo(
            new FamilyPlanSummaryDto(3, 26, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
                new DateOnly(2026, 9, 1), 6, 1, 16.67m));
    }

    [Fact]
    public async Task Funded_service_bundle_is_used_when_saved_plan_pointer_has_advanced()
    {
        var fx = await SeedAsync();
        var originalBundle = await fx.Db.Bundles.SingleAsync();
        var draftBundle = new Bundle
        {
            ProgramId = originalBundle.ProgramId, Code = "DRAFT-PLAN", NameAr = "خطة", NameEn = "Plan",
            BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 1,
            MealsPerDay = 4, Status = ProgramStatus.Active, IsActive = true
        };
        fx.Db.Bundles.Add(draftBundle);
        (await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId)).BundleId = draftBundle.Id;
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);
        result.Value.Single(x => x.MemberId == fx.MemberId).PlanSummary!.MealsPerDay.Should().Be(3);
    }

    [Fact]
    public async Task Old_client_permission_update_preserves_omitted_new_grants()
    {
        var fx = await SeedAsync();
        var grants = await fx.Db.FamilyMemberPermissions.SingleAsync(x => x.FamilyMemberId == fx.MemberId);
        grants.ViewCalendar = true;
        grants.ViewFamilyInvoices = true;
        await fx.Db.SaveChangesAsync();
        var result = await fx.Handlers.Handle(new UpdateFamilyMemberPermissionsCommand(fx.MemberId,
            new(false, false, false, false, false, false, false, false, ExpectedVersion: 0)), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.ViewCalendar.Should().BeTrue();
        result.Value.ViewFamilyInvoices.Should().BeTrue();
    }

    [Fact]
    public async Task Permissions_GET_does_not_create_default_rows()
    {
        var fx = await SeedAsync();
        fx.Db.FamilyMemberPermissions.RemoveRange(await fx.Db.FamilyMemberPermissions.ToListAsync());
        await fx.Db.SaveChangesAsync();
        var result = await fx.Handlers.Handle(new GetFamilyMemberPermissionsQuery(fx.MemberId), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(0);
        result.Value.ViewCalendar.Should().BeFalse();
        (await fx.Db.FamilyMemberPermissions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Home_returns_enabled_shortcuts_and_unavailable_wallet()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.GroupId.Should().Be(fx.GroupId);
        result.Value.Members.Should().HaveCount(1);
        result.Value.Wallet.Available.Should().BeFalse();
        result.Value.Wallet.UnavailableReason.Should().Be("wallet_not_implemented");
        result.Value.Shortcuts.Select(s => s.Code).Should().Equal("calendar", "meals", "subscriptions", "wallet");
        result.Value.Shortcuts.Should().Contain(s => s.Code == "calendar" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "meals" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "subscriptions" && s.Enabled);
        result.Value.Shortcuts.Should().Contain(s => s.Code == "wallet" && !s.Enabled && s.UnavailableReason == "wallet_not_implemented");
        result.Value.Members.Should().Contain(m => m.MemberId == fx.MemberId && m.RemainingDays == 7);
    }

    [Fact]
    public async Task Home_forbidden_for_non_manager()
    {
        var fx = await SeedAsync();
        fx.AsLinkedMember();
        var result = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.NotManager);
    }

    [Fact]
    public async Task Paid_future_renewal_pointer_keeps_old_today_service_visible_and_exposes_upcoming()
    {
        var fx = await SeedAsync();
        var previous = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        var next = new Subscription
        {
            CustomerUserId = previous.CustomerUserId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = 26, IncludeFriday = false,
            Status = SubscriptionStatus.Scheduled, StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30), PaidAmount = new Money(17.5m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
            FamilyGroupId = fx.GroupId, FamilyMemberId = fx.MemberId,
        };
        fx.Db.Subscriptions.Add(next);
        fx.Db.SubscriptionDays.Add(new SubscriptionDay
        {
            SubscriptionId = next.Id, ServiceDate = next.StartDate, Status = SubscriptionDayStatus.Open,
        });
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = next.Id;
        await fx.Db.SaveChangesAsync();

        var home = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        home.IsSuccess.Should().BeTrue();
        var item = home.Value.Members.Single(x => x.MemberId == fx.MemberId);
        item.MemberSubscriptionId.Should().Be(previous.Id);
        item.TodayUiStatus.Should().Be(nameof(SubscriptionDayStatus.Open));
        item.UpcomingSubscriptionId.Should().Be(next.Id);
        item.UpcomingStartDate.Should().Be(next.StartDate);
        home.Value.TodaySchedule.Should().ContainSingle(x => x.MemberId == fx.MemberId);
    }

    [Fact]
    public async Task Detail_read_does_not_rewrite_advanced_pointer_and_reports_projected_current_and_upcoming()
    {
        var fx = await SeedAsync();
        var previous = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        var next = new Subscription
        {
            CustomerUserId = previous.CustomerUserId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = 26, Status = SubscriptionStatus.Scheduled,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PaidAmount = new Money(17.5m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
            FamilyGroupId = fx.GroupId, FamilyMemberId = fx.MemberId,
        };
        fx.Db.Subscriptions.Add(next);
        fx.Db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = next.Id, ServiceDate = next.StartDate });
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = next.Id;
        await fx.Db.SaveChangesAsync();

        var detail = await fx.Handlers.Handle(new GetFamilyMemberQuery(fx.MemberId), CancellationToken.None);
        detail.IsSuccess.Should().BeTrue();
        detail.Value.MemberSubscriptionId.Should().Be(previous.Id);
        detail.Value.UpcomingSubscriptionId.Should().Be(next.Id);
        await fx.Db.SaveChangesAsync();
        (await fx.Db.FamilyMembers.AsNoTracking().Where(x => x.Id == fx.MemberId)
            .Select(x => x.MemberSubscriptionId).SingleAsync()).Should().Be(next.Id);
    }

    [Fact]
    public async Task Paid_preparing_calendar_remains_visible_without_fabricating_today_day()
    {
        var fx = await SeedAsync();
        var subscription = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        subscription.Status = SubscriptionStatus.Paid;
        subscription.PaymentVerifiedAtUtc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        subscription.StartDate = new DateOnly(2026, 8, 25);
        fx.Db.SubscriptionDays.RemoveRange(await fx.Db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
        await fx.Db.SaveChangesAsync();

        var home = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        home.IsSuccess.Should().BeTrue();
        var item = home.Value.Members.Single(x => x.MemberId == fx.MemberId);
        item.MemberSubscriptionId.Should().Be(subscription.Id);
        item.TodayUiStatus.Should().BeNull();
        home.Value.TodaySchedule.Should().NotContain(x => x.MemberId == fx.MemberId);
    }

    [Theory]
    [InlineData(FamilyMemberSetupStatus.Ready, false)]
    [InlineData(FamilyMemberSetupStatus.Ready, true)]
    [InlineData(FamilyMemberSetupStatus.InProgress, false)]
    [InlineData(FamilyMemberSetupStatus.InProgress, true)]
    public async Task Paid_future_only_member_uses_upcoming_service_lifecycle_across_management_reads(
        FamilyMemberSetupStatus setupStatus, bool fulfillmentCompleted)
    {
        var fx = await SeedAsync();
        var previous = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        previous.Status = SubscriptionStatus.Cancelled;
        fx.Db.SubscriptionDays.RemoveRange(await fx.Db.SubscriptionDays.Where(x => x.SubscriptionId == previous.Id).ToListAsync());
        var upcoming = new Subscription
        {
            CustomerUserId = previous.CustomerUserId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = 2, Status = SubscriptionStatus.Paid,
            StartDate = new DateOnly(2026, 10, 17), EndDate = new DateOnly(2026, 10, 18),
            PaidAmount = new Money(17.5m, "KWD"), PaymentVerifiedAtUtc = DateTime.UtcNow,
            FamilyGroupId = fx.GroupId, FamilyMemberId = fx.MemberId,
            FulfillmentCompletedAtUtc = fulfillmentCompleted ? DateTime.UtcNow : null,
        };
        fx.Db.Subscriptions.Add(upcoming);
        fx.Db.SubscriptionDays.AddRange(
            new SubscriptionDay { SubscriptionId = upcoming.Id, ServiceDate = upcoming.StartDate },
            new SubscriptionDay { SubscriptionId = upcoming.Id, ServiceDate = upcoming.EndDate });
        fx.Db.FamilyCheckoutBatches.Add(new FamilyCheckoutBatch
        {
            FamilyGroupId = fx.GroupId, PayerCustomerUserId = previous.CustomerUserId,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "paid-future-only", FinalPrice = 17.5m,
            Lines = { new FamilyCheckoutBatchLine { FamilyMemberId = fx.MemberId, SubscriptionId = upcoming.Id,
                CheckoutSelected = true, NetPaidAmount = 17.5m } }
        });
        fx.Db.FamilyCheckoutBatches.Add(new FamilyCheckoutBatch
        {
            FamilyGroupId = fx.GroupId, PayerCustomerUserId = previous.CustomerUserId,
            Status = FamilyCheckoutBatchStatus.PaymentPending, IdempotencyKey = "newer-unpaid-batch",
            FinalPrice = 0m, CreatedAtUtc = DateTime.UtcNow.AddMinutes(1),
            Lines = { new FamilyCheckoutBatchLine { FamilyMemberId = fx.MemberId, SubscriptionId = previous.Id,
                CheckoutSelected = true, NetPaidAmount = 0m } }
        });
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = upcoming.Id;
        member.SetupStatus = setupStatus;
        await fx.Db.SaveChangesAsync();

        var home = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        var list = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);
        var detail = await fx.Handlers.Handle(new GetFamilyMemberQuery(fx.MemberId), CancellationToken.None);

        home.IsSuccess.Should().BeTrue();
        list.IsSuccess.Should().BeTrue();
        detail.IsSuccess.Should().BeTrue();
        var homeMember = home.Value.Members.Single(x => x.MemberId == fx.MemberId);
        var listMember = list.Value.Single(x => x.MemberId == fx.MemberId);
        foreach (var lifecycle in new[] { homeMember.Lifecycle, listMember.Lifecycle, detail.Value.Lifecycle })
        {
            lifecycle.PaymentStatus.Should().Be(nameof(FamilyCheckoutBatchStatus.Paid));
            lifecycle.DisplayStatus.Should().Be(fulfillmentCompleted ? nameof(SubscriptionStatus.Paid) : "PaidPreparingCalendar");
            lifecycle.FulfillmentStatus.Should().Be(fulfillmentCompleted ? "Completed" : "Pending");
            lifecycle.ServiceStatus.Should().Be(nameof(SubscriptionStatus.Paid));
        }
        homeMember.MemberSubscriptionId.Should().BeNull();
        homeMember.UpcomingSubscriptionId.Should().Be(upcoming.Id);
        homeMember.RemainingDays.Should().Be(2);
        listMember.MemberSubscriptionId.Should().BeNull();
        listMember.UpcomingSubscriptionId.Should().Be(upcoming.Id);
        listMember.RemainingDays.Should().Be(2);
        listMember.PlanSummary.Should().BeEquivalentTo(new FamilyPlanSummaryDto(
            3, 2, upcoming.StartDate, upcoming.EndDate, new DateOnly(2026, 10, 19), 2, 0, 0m));
        listMember.TodayMealSelection.Should().BeNull();
        detail.Value.MemberSubscriptionId.Should().BeNull();
        detail.Value.UpcomingSubscriptionId.Should().Be(upcoming.Id);
        detail.Value.RemainingDays.Should().Be(2);
        detail.Value.CanRenew.Should().BeFalse();
        detail.Value.RenewalPath.Should().BeNull();
        detail.Value.TodayNutrition.Should().BeNull();
        home.Value.TodaySchedule.Should().NotContain(x => x.MemberId == fx.MemberId);
    }

    [Fact]
    public async Task Friday_gap_keeps_old_paid_subscription_current_when_its_next_actual_day_is_Saturday()
    {
        var fx = await SeedAsync();
        var previous = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        previous.IncludeFriday = false;
        fx.Db.SubscriptionDays.RemoveRange(await fx.Db.SubscriptionDays.Where(x => x.SubscriptionId == previous.Id
            && x.ServiceDate == new DateOnly(2026, 8, 28)).ToListAsync());
        var next = new Subscription
        {
            CustomerUserId = previous.CustomerUserId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = 26, Status = SubscriptionStatus.Scheduled,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PaidAmount = new Money(17.5m, "KWD"), PaymentVerifiedAtUtc = DateTime.UtcNow,
            FamilyGroupId = fx.GroupId, FamilyMemberId = fx.MemberId,
        };
        fx.Db.Subscriptions.Add(next);
        fx.Db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = next.Id, ServiceDate = next.StartDate });
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = next.Id;
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc));
        var pricing = Substitute.For<IFamilyPricingSettingsReader>();
        pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings());
        var handler = new FamilyManagerHandlers(fx.Db, fx.User, clock, pricing, fx.Users);

        var home = await handler.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        var item = home.Value.Members.Single(x => x.MemberId == fx.MemberId);
        item.MemberSubscriptionId.Should().Be(previous.Id);
        item.TodayUiStatus.Should().BeNull();
        item.UpcomingSubscriptionId.Should().Be(next.Id);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled, false)]
    [InlineData(SubscriptionStatus.PendingPayment, false)]
    [InlineData(SubscriptionStatus.CancellationPending, true)]
    public async Task Future_pointer_does_not_replace_old_live_service_or_advertise_unfunded_upcoming(
        SubscriptionStatus nextStatus, bool oldIsProtected)
    {
        var fx = await SeedAsync();
        var previous = await fx.Db.Subscriptions.SingleAsync(x => x.Id == fx.MemberSubscriptionId);
        if (oldIsProtected) previous.Status = SubscriptionStatus.CancellationPending;
        var next = new Subscription
        {
            CustomerUserId = previous.CustomerUserId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
            SelectedTier = previous.SelectedTier, DurationDays = 26, Status = nextStatus,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PaidAmount = new Money(17.5m, "KWD"),
            PaymentVerifiedAtUtc = nextStatus == SubscriptionStatus.PendingPayment ? null : DateTime.UtcNow,
            FamilyGroupId = fx.GroupId, FamilyMemberId = fx.MemberId,
        };
        fx.Db.Subscriptions.Add(next);
        fx.Db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = next.Id, ServiceDate = next.StartDate });
        var member = await fx.Db.FamilyMembers.SingleAsync(x => x.Id == fx.MemberId);
        member.MemberSubscriptionId = next.Id;
        await fx.Db.SaveChangesAsync();

        var home = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        var item = home.Value.Members.Single(x => x.MemberId == fx.MemberId);
        item.MemberSubscriptionId.Should().Be(previous.Id);
        if (nextStatus == SubscriptionStatus.CancellationPending)
            item.UpcomingSubscriptionId.Should().Be(next.Id);
        else
            item.UpcomingSubscriptionId.Should().BeNull();
    }

    [Fact]
    public async Task Members_list_is_lightweight()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.Should().NotContain(m => m.MemberId == fx.ManagerMemberId);
        var yusuf = result.Value.Single(m => m.MemberId == fx.MemberId);
        yusuf.AgeYears.Should().Be(16);
        yusuf.RemainingDays.Should().Be(7);
        yusuf.ProgramNameAr.Should().Be("متوازن");
    }

    [Fact]
    public async Task Members_list_and_home_handle_unsubscribed_roster_members()
    {
        var fx = await SeedAsync();
        var unSub = new FamilyMember
        {
            FamilyGroupId = fx.GroupId,
            Role = FamilyMemberRole.Member,
            DisplayName = "Unsubscribed Child",
            RelationshipCode = "son",
            Gender = FamilyMemberGender.Male,
            AgeYears = 8,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.NotStarted,
            DeliveryAddressId = null,
            ProgramId = null,
            BundleId = null,
            SelectedTier = null,
            DurationDays = null,
            IncludeFriday = false,
            MemberSubscriptionId = null,
        };
        fx.Db.FamilyMembers.Add(unSub);
        fx.Db.CustomerProfiles.Add(new MealMate.Domain.IdentityAccess.CustomerProfile
        {
            UserId = fx.User.UserId!.Value, ProfileImageStorageKey = "uploads/customers/profile/manager.png"
        });
        await fx.Db.SaveChangesAsync();
        var unSubId = unSub.Id;

        var membersResult = await fx.Handlers.Handle(new FamilyMembersQuery(), CancellationToken.None);
        membersResult.IsSuccess.Should().BeTrue();
        membersResult.Value.Should().HaveCount(2);
        membersResult.Value.Should().NotContain(m => m.MemberId == fx.ManagerMemberId);
        var unSubListItem = membersResult.Value.Single(m => m.MemberId == unSubId);
        unSubListItem.DisplayName.Should().Be("Unsubscribed Child");
        unSubListItem.RemainingDays.Should().Be(0);
        unSubListItem.MemberSubscriptionId.Should().BeNull();
        unSubListItem.ProgramNameAr.Should().BeNull();
        unSubListItem.SetupStatus.Should().Be(FamilyMemberSetupStatus.NotStarted);
        unSubListItem.HasSeparateAccount.Should().BeFalse();
        unSubListItem.ProfileImageUrl.Should().BeNull();

        var homeResult = await fx.Handlers.Handle(new GetFamilyHomeQuery(), CancellationToken.None);
        homeResult.IsSuccess.Should().BeTrue();
        homeResult.Value.Members.Should().HaveCount(2);
        homeResult.Value.Members.Should().NotContain(m => m.MemberId == fx.ManagerMemberId);
        var unSubHomeItem = homeResult.Value.Members.Single(m => m.MemberId == unSubId);
        unSubHomeItem.DisplayName.Should().Be("Unsubscribed Child");
        unSubHomeItem.RemainingDays.Should().Be(0);
        unSubHomeItem.MemberSubscriptionId.Should().BeNull();
        unSubHomeItem.TodayUiStatus.Should().BeNull();

        var detailResult = await fx.Handlers.Handle(new GetFamilyMemberQuery(unSubId), CancellationToken.None);
        detailResult.IsSuccess.Should().BeTrue();
        detailResult.Value.DisplayName.Should().Be("Unsubscribed Child");
        detailResult.Value.RemainingDays.Should().Be(0);
        detailResult.Value.MemberSubscriptionId.Should().BeNull();
        detailResult.Value.CanRenew.Should().BeFalse();
        detailResult.Value.RenewalPath.Should().BeNull();
        detailResult.Value.TodayNutrition.Should().BeNull();
    }

    [Fact]
    public async Task Member_detail_includes_remaining_days_renewal_and_today_nutrition()
    {
        var fx = await SeedAsync(withTodayMeal: true);
        var result = await fx.Handlers.Handle(new GetFamilyMemberQuery(fx.MemberId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PhoneNumber.Should().Be("+96551111112");
        result.Value.RemainingDays.Should().Be(7);
        result.Value.StartDate.Should().Be(new DateOnly(2026, 8, 1));
        result.Value.EndDate.Should().Be(new DateOnly(2026, 8, 31));
        result.Value.CanRenew.Should().BeTrue();
        result.Value.RenewalPath.Should().Be($"/api/v1/customer/subscriptions/{fx.MemberSubscriptionId}/renewal-requests");
        result.Value.TodayNutrition.Should().NotBeNull();
        result.Value.TodayNutrition!.Calories.Should().Be(500);
        result.Value.TodayNutrition.ProteinG.Should().Be(40);
        result.Value.TodayNutrition.CarbsG.Should().Be(30);
        result.Value.TodayNutrition.FatG.Should().Be(20);
    }

    [Fact]
    public async Task Update_access_invite_and_accept_roundtrip()
    {
        var fx = await SeedAsync();
        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.InviteDeepLink.Should().NotBeNullOrEmpty();
        var token = access.Value.InviteDeepLink!.Split('/').Last();

        fx.AsLinkedMember();
        var accept = await fx.Handlers.Handle(new AcceptFamilyInviteCommand(token), CancellationToken.None);
        accept.IsSuccess.Should().BeTrue();
        accept.Value.LinkedCustomerUserId.Should().Be(fx.LinkedUserId);
        accept.Value.GeneratedPassword.Should().BeNull();
    }

    [Fact]
    public async Task Accept_invite_binds_accepter_default_address_when_member_had_manager_address()
    {
        var fx = await SeedAsync();
        var managerUserId = (await fx.Db.FamilyGroups.SingleAsync(g => g.Id == fx.GroupId)).ManagerCustomerUserId;
        var managerAddress = new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = managerUserId,
            Label = "Manager home",
            AddressLine = "Manager Street",
            IsDefault = true,
        };
        var accepterAddress = new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = fx.LinkedUserId,
            Label = "Accepter home",
            AddressLine = "Accepter Street",
            IsDefault = true,
        };
        fx.Db.CustomerAddresses.AddRange(managerAddress, accepterAddress);
        var member = await fx.Db.FamilyMembers.SingleAsync(m => m.Id == fx.MemberId);
        member.DeliveryAddressId = managerAddress.Id;
        await fx.Db.SaveChangesAsync();

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);
        access.IsSuccess.Should().BeTrue();
        var token = access.Value.InviteDeepLink!.Split('/').Last();

        fx.AsLinkedMember();
        var accept = await fx.Handlers.Handle(new AcceptFamilyInviteCommand(token), CancellationToken.None);
        accept.IsSuccess.Should().BeTrue();

        var updated = await fx.Db.FamilyMembers.SingleAsync(m => m.Id == fx.MemberId);
        updated.DeliveryAddressId.Should().Be(accepterAddress.Id);
    }

    [Fact]
    public async Task Enabling_access_returns_generated_password_once_for_new_login()
    {
        var fx = await SeedAsync();
        fx.Users.FindByPhoneAsync("+96551111112", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                fx.ProvisionedUserId, null, "+96551111112", "يوسف", UserType.Customer,
                AccountStatus.Active, null, false, true, DateTime.UtcNow.AddDays(-10), null));
        fx.Users.GeneratePasswordResetTokenAsync(fx.ProvisionedUserId, Arg.Any<CancellationToken>())
            .Returns("reset-token");
        fx.Users.ResetPasswordAsync(fx.ProvisionedUserId, "reset-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.GeneratedPassword.Should().NotBeNullOrWhiteSpace();
        access.Value.HasExistingLogin.Should().BeFalse();
        access.Value.LinkedCustomerUserId.Should().Be(fx.ProvisionedUserId);

        var again = await fx.Handlers.Handle(new GetFamilyMemberAccessQuery(fx.MemberId), CancellationToken.None);
        again.Value.GeneratedPassword.Should().BeNull();
        again.Value.HasSeparateAccount.Should().BeTrue();
        again.Value.PhoneNumber.Should().Be("+96551111112");
    }

    [Fact]
    public async Task Reenabling_linked_account_never_resets_existing_password_without_login_history()
    {
        var fx = await SeedAsync();
        var access = await fx.Db.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == fx.MemberId);
        access.LinkedCustomerUserId = fx.ProvisionedUserId;
        await fx.Db.SaveChangesAsync();
        fx.Users.FindByIdAsync(fx.ProvisionedUserId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            fx.ProvisionedUserId, null, "+96551111112", "Member", UserType.Customer,
            AccountStatus.Active, null, false, true, DateTime.UtcNow.AddDays(-10), null));
        var result = await fx.Handlers.Handle(new UpdateFamilyMemberAccessCommand(fx.MemberId,
            new(true, null, null)), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.GeneratedPassword.Should().BeNull();
        await fx.Users.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Enabling_access_does_not_reset_password_for_existing_login()
    {
        var fx = await SeedAsync();
        fx.Users.FindByPhoneAsync("+96551111112", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                fx.ProvisionedUserId, null, "+96551111112", "يوسف", UserType.Customer,
                AccountStatus.Active, null, false, true, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddDays(-1)));

        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: true, IsPaused: false, Remove: null)),
            CancellationToken.None);

        access.IsSuccess.Should().BeTrue();
        access.Value.HasExistingLogin.Should().BeTrue();
        access.Value.GeneratedPassword.Should().BeNull();
        access.Value.LinkedCustomerUserId.Should().Be(fx.ProvisionedUserId);
        await fx.Users.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_blocks_are_persisted()
    {
        var fx = await SeedAsync();
        var access = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.MemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: null, IsPaused: true, Remove: null)),
            CancellationToken.None);
        access.IsSuccess.Should().BeTrue();
        access.Value.IsPaused.Should().BeTrue();

        var again = await fx.Handlers.Handle(new GetFamilyMemberAccessQuery(fx.MemberId), CancellationToken.None);
        again.Value.IsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task Cannot_remove_manager()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new UpdateFamilyMemberAccessCommand(fx.ManagerMemberId, new UpdateFamilyMemberAccessRequest(
                HasSeparateAccount: null, IsPaused: null, Remove: true)),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.CannotRemoveManager);
    }

    [Fact]
    public async Task Create_member_tells_client_to_use_checkout_flow()
    {
        var fx = await SeedAsync();
        var result = await fx.Handlers.Handle(
            new CreateFamilyMemberCommand(new CreateFamilyMemberRequest(
                "Sara", "daughter", FamilyMemberGender.Female, 12, "balanced",
                FamilyActivityLevel.Moderate, null, null, null, null, false)),
            CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Family.UseCheckoutFlow);
    }

    [Fact]
    public async Task Permissions_include_grouped_labels_and_are_immutable_for_manager()
    {
        var fx = await SeedAsync();
        var ok = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.MemberId, new UpdateFamilyMemberPermissionsRequest(
                true, false, true, false, false, false, false, false, ExpectedVersion: 0)),
            CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.EditDailyMeals.Should().BeTrue();
        ok.Value.Groups.Should().HaveCount(4);
        ok.Value.Groups.Select(g => g.Code).Should().Equal("meals", "program", "delivery", "account");
        ok.Value.Groups.Single(g => g.Code == "meals").Permissions
            .Single(p => p.Key == "editDailyMeals").Value.Should().BeTrue();

        var loaded = await fx.Handlers.Handle(new GetFamilyMemberPermissionsQuery(fx.MemberId), CancellationToken.None);
        loaded.Value.Groups.Should().HaveCount(4);

        var bad = await fx.Handlers.Handle(
            new UpdateFamilyMemberPermissionsCommand(fx.ManagerMemberId, new UpdateFamilyMemberPermissionsRequest(
                true, true, true, true, true, true, true, true)),
            CancellationToken.None);
        bad.IsFailure.Should().BeTrue();
        bad.Error.Code.Should().Be(MessageCodes.Family.ManagerPermissionsImmutable);
    }

    private static async Task<Fixture> SeedAsync(bool withTodayMeal = false)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var managerUserId = Guid.NewGuid();
        var linkedUserId = Guid.NewGuid();
        var provisionedUserId = Guid.NewGuid();
        var program = new Program { Code = "BAL", NameAr = "متوازن", NameEn = "Balanced", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "3M", NameAr = "3", NameEn = "3",
            BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 0,
            MealsPerDay = 3, Status = ProgramStatus.Active, IsActive = true
        };
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerUserId,
            Name = "عائلة أحمد",
            Status = FamilyGroupStatus.Active,
            QuotedFinalPrice = 37.5m,
            CurrencyCode = "KWD"
        };
        var start = new DateOnly(2026, 8, 1);
        var end = new DateOnly(2026, 8, 31);
        var managerSub = new Subscription
        {
            CustomerUserId = managerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = start,
            EndDate = end,
            PaidAmount = new Money(20m, "KWD"),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        var memberSub = new Subscription
        {
            CustomerUserId = managerUserId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            FamilyGroupId = group.Id,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = start,
            EndDate = end,
            PaidAmount = new Money(17.5m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
            FulfillmentCompletedAtUtc = DateTime.UtcNow
        };
        db.AddRange(program, bundle, group, managerSub, memberSub);
        await db.SaveChangesAsync();

        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 35,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            PhoneNumber = "+96551111111",
            MemberSubscriptionId = managerSub.Id
        };
        var member = new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "يوسف",
            RelationshipCode = "son",
            Gender = FamilyMemberGender.Male,
            AgeYears = 16,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Active,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            PhoneNumber = "+96551111112",
            MemberSubscriptionId = memberSub.Id
        };
        memberSub.FamilyMemberId = member.Id;
        db.AddRange(managerMember, member);
        await db.SaveChangesAsync();
        db.AddRange(
            new FamilyMemberAccess { FamilyMemberId = managerMember.Id },
            new FamilyMemberAccess { FamilyMemberId = member.Id },
            new FamilyMemberPermissions { FamilyMemberId = managerMember.Id },
            new FamilyMemberPermissions { FamilyMemberId = member.Id });
        await db.SaveChangesAsync();

        for (var i = 1; i <= 6; i++)
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = memberSub.Id,
                ServiceDate = new DateOnly(2026, 8, 25).AddDays(i), Status = SubscriptionDayStatus.Open });
        if (!withTodayMeal)
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = memberSub.Id,
                ServiceDate = new DateOnly(2026, 8, 25), Status = SubscriptionDayStatus.Open });
        await db.SaveChangesAsync();
        if (withTodayMeal)
        {
            var day = new SubscriptionDay
            {
                SubscriptionId = memberSub.Id,
                ServiceDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionDayStatus.CustomerSelected
            };
            db.SubscriptionDays.Add(day);
            await db.SaveChangesAsync();
            var meal = new Meal
            {
                MenuId = Guid.NewGuid(),
                RestaurantId = Guid.NewGuid(),
                Code = "M1",
                NameAr = "وجبة",
                NameEn = "Meal",
                Calories = 500,
                ProteinG = 40,
                CarbsG = 30,
                FatG = 20
            };
            db.Meals.Add(meal);
            await db.SaveChangesAsync();
            db.DailyMealSelections.Add(new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = meal.Id,
                SortOrder = 0,
                SelectedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(managerUserId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 25));
        var pricing = Substitute.For<IFamilyPricingSettingsReader>();
        pricing.GetAsync(Arg.Any<CancellationToken>()).Returns(new FamilyPricingSettings
        {
            Packages =
            [
                new FamilySizePackageSettings
                {
                    NameAr = "باقة عائلية", NameEn = "Family package", MaxMembers = 4, IsActive = true
                }
            ]
        });
        var users = Substitute.For<IUserAccountService>();

        var handlers = new FamilyManagerHandlers(db, user, clock, pricing, users);
        return new Fixture(db, handlers, user, users, linkedUserId, provisionedUserId, group.Id, managerMember.Id, member.Id, memberSub.Id);
    }

    private sealed class Fixture(
        AppDbContext Db,
        FamilyManagerHandlers Handlers,
        ICurrentUser User,
        IUserAccountService Users,
        Guid LinkedUserId,
        Guid ProvisionedUserId,
        Guid GroupId,
        Guid ManagerMemberId,
        Guid MemberId,
        Guid MemberSubscriptionId)
    {
        public AppDbContext Db { get; } = Db;
        public ICurrentUser User { get; } = User;
        public FamilyManagerHandlers Handlers { get; } = Handlers;
        public IUserAccountService Users { get; } = Users;
        public Guid LinkedUserId { get; } = LinkedUserId;
        public Guid ProvisionedUserId { get; } = ProvisionedUserId;
        public Guid GroupId { get; } = GroupId;
        public Guid ManagerMemberId { get; } = ManagerMemberId;
        public Guid MemberId { get; } = MemberId;
        public Guid MemberSubscriptionId { get; } = MemberSubscriptionId;

        public void AsLinkedMember()
        {
            User.UserId.Returns(LinkedUserId);
            User.UserType.Returns(UserType.Customer);
        }
    }
}
