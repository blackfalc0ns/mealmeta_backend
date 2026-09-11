using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class FamilyCheckoutServiceTests
{
    [Fact]
    public async Task StartCheckout_TwoReadyMembers_CreatesTwoSubscriptions_OnePaymentAtFinalPrice_GroupCheckoutPending()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = managerId,
            Label = "Home",
            AddressLine = "Kuwait City",
            RegionId = regionId,
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        SeedAllergies(db, managerId, spouseUserId);

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Draft,
            DiscountPercentSnapshot = 0.10m,
        };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, programId, bundleId, 26);
        var spouse = ReadyMember(group.Id, FamilyMemberRole.Member, "Spouse", address.Id, programId, bundleId, 12);
        spouse.StartDate = new DateOnly(2026, 9, 1);
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
        LinkMember(db, spouse.Id, spouseUserId);
        await db.SaveChangesAsync();

        var quote = new FamilyQuoteResult(
            [
                new FamilyQuoteLine(manager.Id, "Manager", 200m, 0, 0, 0),
                new FamilyQuoteLine(spouse.Id, "Spouse", 100m, 0, 0, 0),
            ],
            Subtotal: 300m,
            FamilyDiscountPercent: 0.10m,
            FamilyDiscountAmount: 30m,
            FinalPrice: 270m,
            CurrencyCode: "KWD",
            DiscountApplied: true);

        var quoteService = Substitute.For<IFamilyQuoteService>();
        quoteService.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>()).Returns(quote);

        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 8, 19);
        var serviceDates = new List<DateOnly> { startDate, startDate.AddDays(1) };
        schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(serviceDates);

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

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>())
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
                    PaidAmount = new Money(1m, "KWD"),
                    CorrelationId = cmd.CorrelationId,
                };
                db.Subscriptions.Add(sub);
                db.SaveChanges();
                return Task.FromResult(Result<SubscriptionDto>.Success(new SubscriptionDto(
                    sub.Id, sub.CustomerUserId, sub.ProgramId, "", "", sub.BundleId, "", "",
                    sub.SelectedTier, sub.DurationDays, sub.Status, sub.StartDate, sub.EndDate,
                    sub.PaidAmount.Amount, sub.PaidAmount.CurrencyCode, sub.DeliveryAddress, sub.CorrelationId,
                    DateTime.UtcNow, [], [], sub.IncludeFriday)));
            });

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                270m,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                group.Id.ToString(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=PAY-FAMILY-1");

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            users,
            quoteService,
            settings,
            schedule,
            eligibility,
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            pool,
            payments,
            sender,
            clock);

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-1",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FamilyGroupId.Should().Be(group.Id);
        result.Value.FinalPrice.Should().Be(270m);
        result.Value.PaymentUrl.Should().Contain("PAY-FAMILY-1");

        var subscriptions = await db.Subscriptions.Where(x => x.FamilyGroupId == group.Id).ToListAsync();
        subscriptions.Should().HaveCount(2);
        subscriptions.Select(x => x.PaidAmount.Amount).OrderByDescending(x => x).Should().Equal(200m, 100m);
        subscriptions.Should().OnlyContain(x => x.Status == SubscriptionStatus.PendingPayment);
        subscriptions.Single(x => x.FamilyMemberId == manager.Id).CustomerUserId.Should().Be(managerId);
        subscriptions.Single(x => x.FamilyMemberId == spouse.Id).CustomerUserId.Should().Be(spouseUserId);
        subscriptions.Single(x => x.FamilyMemberId == spouse.Id).StartDate.Should().Be(new DateOnly(2026, 9, 1));
        subscriptions.Single(x => x.FamilyMemberId == manager.Id).PaymentId.Should().Be("PAY-FAMILY-1");
        subscriptions.Single(x => x.FamilyMemberId == spouse.Id).PaymentId.Should().BeNull();

        var refreshed = await db.FamilyGroups.SingleAsync(x => x.Id == group.Id);
        refreshed.Status.Should().Be(FamilyGroupStatus.CheckoutPending);
        refreshed.QuotedFinalPrice.Should().Be(270m);
        refreshed.PaymentAnchorSubscriptionId.Should().Be(result.Value.PaymentAnchorSubscriptionId);
        refreshed.PaymentAnchorSubscriptionId.Should().Be(
            subscriptions.Single(x => x.FamilyMemberId == manager.Id).Id);
        refreshed.PaymentId.Should().Be("PAY-FAMILY-1");

        await payments.Received(1).ExecutePaymentAsync(
            270m,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            group.Id.ToString(),
            2,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCheckout_CheckoutPending_ReturnsFamilyNotDraft_NotNotFound()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            Substitute.For<IUserAccountService>(),
            Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IFamilyPricingSettingsReader>(),
            Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            Substitute.For<IRestaurantPoolSnapshotService>(),
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<ISender>(),
            Substitute.For<IDateTimeProvider>());

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-pending",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.not_draft");
    }

    [Fact]
    public async Task StartCheckout_CheckoutPendingWithHolds_RetriesPaymentWithoutCreatingSubscriptions()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
        };
        var sub = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 8, 19),
            EndDate = new DateOnly(2026, 9, 13),
            PaidAmount = new Money(270m, "KWD"),
            DeliveryAddress = "Kuwait",
            FamilyGroupId = group.Id,
        };
        db.FamilyGroups.Add(group);
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        group.PaymentAnchorSubscriptionId = sub.Id;
        db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = sub.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 8, 20),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = now.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                270m,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                group.Id.ToString(),
                2,
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=PAY-RETRY-1");
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);
        var sender = Substitute.For<ISender>();

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            users,
            Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IFamilyPricingSettingsReader>(),
            Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            Substitute.For<IRestaurantPoolSnapshotService>(),
            payments,
            sender,
            clock);

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-retry",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentUrl.Should().Contain("PAY-RETRY-1");
        result.Value.FinalPrice.Should().Be(270m);
        await sender.DidNotReceive().Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>());
        (await db.Subscriptions.CountAsync(x => x.FamilyGroupId == group.Id)).Should().Be(1);
    }

    [Fact]
    public async Task StartCheckout_CheckoutPendingMultipleMembers_AttachesGatewayPaymentIdOnlyToAnchorSubscription()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var spouseId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
        };
        var anchorSub = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 8, 19),
            EndDate = new DateOnly(2026, 9, 13),
            PaidAmount = new Money(180m, "KWD"),
            DeliveryAddress = "Kuwait",
            FamilyGroupId = group.Id,
        };
        var siblingSub = new Subscription
        {
            CustomerUserId = spouseId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.PendingPayment,
            StartDate = new DateOnly(2026, 8, 19),
            EndDate = new DateOnly(2026, 9, 13),
            PaidAmount = new Money(90m, "KWD"),
            DeliveryAddress = "Kuwait",
            FamilyGroupId = group.Id,
        };
        db.FamilyGroups.Add(group);
        db.Subscriptions.AddRange(anchorSub, siblingSub);
        await db.SaveChangesAsync();
        group.PaymentAnchorSubscriptionId = anchorSub.Id;
        db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = anchorSub.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 8, 20),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = now.AddMinutes(10),
        });
        db.CapacityHolds.Add(new CapacityHold
        {
            RestaurantId = Guid.NewGuid(),
            SubscriptionId = siblingSub.Id,
            MenuPlanVersionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 8, 20),
            Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = now.AddMinutes(10),
        });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                270m,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                group.Id.ToString(),
                2,
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=PAY-MULTI-RESUME");
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);
        var sender = Substitute.For<ISender>();

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            users,
            Substitute.For<IFamilyQuoteService>(),
            Substitute.For<IFamilyPricingSettingsReader>(),
            Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            Substitute.For<IRestaurantPoolSnapshotService>(),
            payments,
            sender,
            clock);

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-multi-resume",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PaymentUrl.Should().Contain("PAY-MULTI-RESUME");

        var refreshedAnchor = await db.Subscriptions.SingleAsync(x => x.Id == anchorSub.Id);
        var refreshedSibling = await db.Subscriptions.SingleAsync(x => x.Id == siblingSub.Id);
        var refreshedGroup = await db.FamilyGroups.SingleAsync(x => x.Id == group.Id);

        refreshedAnchor.PaymentId.Should().Be("PAY-MULTI-RESUME");
        refreshedSibling.PaymentId.Should().BeNull();
        refreshedGroup.PaymentId.Should().Be("PAY-MULTI-RESUME");
    }

    [Fact]
    public async Task StartCheckout_ConvertedManager_ReusesSubscription_CreatesOnlyNewMembers()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = managerId,
            Label = "Home",
            AddressLine = "Kuwait City",
            RegionId = regionId,
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        SeedAllergies(db, spouseUserId);

        var existingSub = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = programId,
            BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(200m, "KWD"),
            DeliveryAddress = "Kuwait City",
            RegionId = regionId,
        };
        db.Subscriptions.Add(existingSub);

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Draft,
            DiscountPercentSnapshot = 0.10m,
            ConvertedFromSubscriptionId = existingSub.Id,
        };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, programId, bundleId, 26);
        manager.MemberSubscriptionId = existingSub.Id;
        var spouse = ReadyMember(group.Id, FamilyMemberRole.Member, "Spouse", address.Id, programId, bundleId, 12);
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
        LinkMember(db, spouse.Id, spouseUserId);
        existingSub.FamilyGroupId = group.Id;
        existingSub.FamilyMemberId = manager.Id;
        await db.SaveChangesAsync();

        var quote = new FamilyQuoteResult(
            [
                new FamilyQuoteLine(manager.Id, "Manager", 0m, 0, 0, 0),
                new FamilyQuoteLine(spouse.Id, "Spouse", 100m, 0, 0, 0),
            ],
            Subtotal: 100m,
            FamilyDiscountPercent: 0.10m,
            FamilyDiscountAmount: 10m,
            FinalPrice: 90m,
            CurrencyCode: "KWD",
            DiscountApplied: true);

        var quoteService = Substitute.For<IFamilyQuoteService>();
        quoteService.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>()).Returns(quote);

        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 8, 19);
        var serviceDates = new List<DateOnly> { startDate, startDate.AddDays(1) };
        schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(serviceDates);

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
            .Returns(_ => new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>())
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
                    PaidAmount = new Money(1m, "KWD"),
                    CorrelationId = cmd.CorrelationId,
                };
                db.Subscriptions.Add(sub);
                db.SaveChanges();
                return Task.FromResult(Result<SubscriptionDto>.Success(new SubscriptionDto(
                    sub.Id, sub.CustomerUserId, sub.ProgramId, "", "", sub.BundleId, "", "",
                    sub.SelectedTier, sub.DurationDays, sub.Status, sub.StartDate, sub.EndDate,
                    sub.PaidAmount.Amount, sub.PaidAmount.CurrencyCode, sub.DeliveryAddress, sub.CorrelationId,
                    DateTime.UtcNow, [], [], sub.IncludeFriday)));
            });

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                90m,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                group.Id.ToString(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=PAY-CONV-1");

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            users,
            quoteService,
            settings,
            schedule,
            eligibility,
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            pool,
            payments,
            sender,
            clock);

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-conv",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FinalPrice.Should().Be(90m);
        result.Value.PaymentAnchorSubscriptionId.Should().Be(existingSub.Id);

        await sender.Received(1).Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>());

        var subscriptions = await db.Subscriptions.Where(x => x.FamilyGroupId == group.Id).ToListAsync();
        subscriptions.Should().HaveCount(2);
        subscriptions.Single(x => x.Id == existingSub.Id).Status.Should().Be(SubscriptionStatus.Active);
        subscriptions.Single(x => x.Id != existingSub.Id).Status.Should().Be(SubscriptionStatus.PendingPayment);
        subscriptions.Single(x => x.Id != existingSub.Id).PaidAmount.Amount.Should().Be(100m);
        subscriptions.Single(x => x.Id != existingSub.Id).PaymentId.Should().Be("PAY-CONV-1");
        existingSub.PaymentId.Should().BeNull();
        subscriptions.Single(x => x.Id != existingSub.Id).CustomerUserId.Should().Be(spouseUserId);
    }

    [Fact]
    public async Task StartCheckout_MissingAllergies_ReturnsAllergiesRequired()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = managerId,
            Label = "Home",
            AddressLine = "Kuwait City",
            RegionId = Guid.NewGuid(),
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Draft,
        };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, programId, bundleId, 26);
        group.Members.Add(manager);
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var quote = new FamilyQuoteResult(
            [new FamilyQuoteLine(manager.Id, "Manager", 200m, 0, 0, 0)],
            Subtotal: 200m,
            FamilyDiscountPercent: 0m,
            FamilyDiscountAmount: 0m,
            FinalPrice: 200m,
            CurrencyCode: "KWD",
            DiscountApplied: false);
        var quoteService = Substitute.For<IFamilyQuoteService>();
        quoteService.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>()).Returns(quote);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            Substitute.For<IUserAccountService>(),
            quoteService,
            settings,
            Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantEligibilityService>(),
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            Substitute.For<IRestaurantPoolSnapshotService>(),
            Substitute.For<IMyFatoorahPaymentService>(),
            Substitute.For<ISender>(),
            Substitute.For<IDateTimeProvider>());

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("family.allergies_required");
    }

    [Fact]
    public async Task StartCheckout_ActiveGroupAddon_CreatesOnlyNewMemberSubscription()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = managerId,
            Label = "Home",
            AddressLine = "Kuwait City",
            RegionId = regionId,
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        SeedAllergies(db, spouseUserId);

        var existingSub = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = programId,
            BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(200m, "KWD"),
            DeliveryAddress = "Kuwait City",
            RegionId = regionId,
        };
        db.Subscriptions.Add(existingSub);

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Active,
            DiscountPercentSnapshot = 0.10m,
            PaidAtUtc = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc),
            PaymentAnchorSubscriptionId = existingSub.Id,
        };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, programId, bundleId, 26);
        manager.MemberSubscriptionId = existingSub.Id;
        var spouse = ReadyMember(group.Id, FamilyMemberRole.Member, "Spouse", address.Id, programId, bundleId, 12);
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
        LinkMember(db, spouse.Id, spouseUserId);
        existingSub.FamilyGroupId = group.Id;
        existingSub.FamilyMemberId = manager.Id;
        await db.SaveChangesAsync();

        var quote = new FamilyQuoteResult(
            [
                new FamilyQuoteLine(manager.Id, "Manager", 0m, 0, 0, 0),
                new FamilyQuoteLine(spouse.Id, "Spouse", 100m, 0, 0, 0),
            ],
            Subtotal: 100m,
            FamilyDiscountPercent: 0.15m,
            FamilyDiscountAmount: 15m,
            FinalPrice: 85m,
            CurrencyCode: "KWD",
            DiscountApplied: true);

        var quoteService = Substitute.For<IFamilyQuoteService>();
        quoteService.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>()).Returns(quote);

        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.15m, MaxMembers = 4, MinMembersForDiscount = 2 });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 8, 19);
        var serviceDates = new List<DateOnly> { startDate, startDate.AddDays(1) };
        schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(serviceDates);

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
            .Returns(_ => new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>())
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
                    PaidAmount = new Money(1m, "KWD"),
                    CorrelationId = cmd.CorrelationId,
                };
                db.Subscriptions.Add(sub);
                db.SaveChanges();
                return Task.FromResult(Result<SubscriptionDto>.Success(new SubscriptionDto(
                    sub.Id, sub.CustomerUserId, sub.ProgramId, "", "", sub.BundleId, "", "",
                    sub.SelectedTier, sub.DurationDays, sub.Status, sub.StartDate, sub.EndDate,
                    sub.PaidAmount.Amount, sub.PaidAmount.CurrencyCode, sub.DeliveryAddress, sub.CorrelationId,
                    DateTime.UtcNow, [], [], sub.IncludeFriday)));
            });

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(
                85m,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                group.Id.ToString(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns("https://pay.example/invoice?paymentId=PAY-ADDON-1");

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var sut = new FamilyCheckoutService(
            db,
            currentUser,
            users,
            quoteService,
            settings,
            schedule,
            eligibility,
            Substitute.For<ICapacityConcurrencyLock>(),
            new InlineTransactionExecutor(),
            pool,
            payments,
            sender,
            clock);

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest(
                "https://localhost/callback",
                "https://localhost/error",
                CorrelationId: "corr-addon",
                PaymentMethodId: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FinalPrice.Should().Be(85m);
        await sender.Received(1).Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>());

        var subscriptions = await db.Subscriptions.Where(x => x.FamilyGroupId == group.Id).ToListAsync();
        subscriptions.Should().HaveCount(2);
        subscriptions.Single(x => x.Id == existingSub.Id).Status.Should().Be(SubscriptionStatus.Active);
        var addon = subscriptions.Single(x => x.Id != existingSub.Id);
        addon.Status.Should().Be(SubscriptionStatus.PendingPayment);
        addon.CustomerUserId.Should().Be(spouseUserId);
        addon.PaidAmount.Amount.Should().Be(100m);

        var refreshed = await db.FamilyGroups.SingleAsync(x => x.Id == group.Id);
        refreshed.Status.Should().Be(FamilyGroupStatus.Active);
        refreshed.PaymentAnchorSubscriptionId.Should().Be(addon.Id);
        refreshed.QuotedFinalPrice.Should().Be(85m);
    }

    private static void SeedAllergies(AppDbContext db, params Guid[] userIds)
    {
        foreach (var userId in userIds)
            db.CustomerProfiles.Add(new CustomerProfile { UserId = userId, AllergiesCompletedAtUtc = DateTime.UtcNow });
    }

    private static void LinkMember(AppDbContext db, Guid memberId, Guid userId)
        => db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = memberId,
            HasSeparateAccount = true,
            LinkedCustomerUserId = userId,
        });

    private static FamilyMember ReadyMember(
        Guid groupId,
        FamilyMemberRole role,
        string name,
        Guid addressId,
        Guid programId,
        Guid bundleId,
        int days)
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
            ProgramId = programId,
            BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = days,
            IncludeFriday = false,
        };

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
