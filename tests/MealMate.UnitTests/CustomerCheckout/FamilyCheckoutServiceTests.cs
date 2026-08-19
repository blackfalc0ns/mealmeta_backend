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

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.Draft,
            DiscountPercentSnapshot = 0.10m,
        };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, programId, bundleId, 26);
        var spouse = ReadyMember(group.Id, FamilyMemberRole.Member, "Spouse", address.Id, programId, bundleId, 12);
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
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
        subscriptions.Should().OnlyContain(x => x.CustomerUserId == managerId);

        var refreshed = await db.FamilyGroups.SingleAsync(x => x.Id == group.Id);
        refreshed.Status.Should().Be(FamilyGroupStatus.CheckoutPending);
        refreshed.QuotedFinalPrice.Should().Be(270m);
        refreshed.PaymentAnchorSubscriptionId.Should().Be(result.Value.PaymentAnchorSubscriptionId);
        refreshed.PaymentAnchorSubscriptionId.Should().Be(
            subscriptions.Single(x => x.FamilyMemberId == manager.Id).Id);

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
