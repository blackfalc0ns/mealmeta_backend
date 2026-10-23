using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.ApiTests.OrderLifecycle;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.Support;

public sealed class SupportDeliveredBoxesHttpTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;

    public SupportDeliveredBoxesHttpTests(OrderLifecycleEndToEndFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CustomerDeliveredBoxesRouteIsAvailableAndRequiresAuthentication()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/support/delivered-boxes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var driver = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Driver, ["Driver"], []));
        (await driver.GetAsync("/api/v1/support/delivered-boxes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        var response = await customer.GetAsync("/api/v1/support/delivered-boxes");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await response.Content.ReadFromJsonAsync<SupportDeliveredBoxesPageDto>();
        page.Should().NotBeNull();
        page!.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task DeliveredBoxQueryValidatesDatesPagingAndOffsetOverflow()
    {
        using var customer = _fixture.CreateClientForUser(new TokenUser(Guid.NewGuid(), UserType.Customer, ["Customer"], []));
        (await customer.GetAsync("/api/v1/support/delivered-boxes?pageNumber=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.GetAsync("/api/v1/support/delivered-boxes?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.GetAsync("/api/v1/support/delivered-boxes?fromDate=2026-10-24&toDate=2026-10-23")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await customer.GetAsync("/api/v1/support/delivered-boxes?pageNumber=2147483647&pageSize=100")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SelectorReturnsOnlyOwnedDeliveredOrdersAndLegacyDeliveredDaysWithStablePaging()
    {
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var start = new DateOnly(2026, 10, 20);
        var ids = new DeliveredBoxSeedIds();
        await using (var db = _fixture.CreateDbContext())
        {
            var program = NewProgram();
            var bundle = NewBundle(program.Id);
            var owned = NewSubscription(customerId, program.Id, bundle.Id, start);
            var foreign = NewSubscription(otherCustomerId, program.Id, bundle.Id, start);
            db.AddRange(program, bundle, owned, foreign, NewRestaurant(restaurantId));

            var deliveredDay = NewDay(owned.Id, restaurantId, start.AddDays(2), SubscriptionDayStatus.Delivered);
            var deliveredOrder = NewOrder(deliveredDay.Id, restaurantId, start.AddDays(2), OrderStatus.Delivered, 3);
            deliveredOrder.Id = ids.DeliveredOrderId;
            var fallbackDay = NewDay(owned.Id, restaurantId, start.AddDays(1), SubscriptionDayStatus.Delivered);
            fallbackDay.Id = ids.FallbackDayId;
            var pendingDay = NewDay(owned.Id, restaurantId, start.AddDays(3), SubscriptionDayStatus.Delivered);
            var pendingOrder = NewOrder(pendingDay.Id, restaurantId, start.AddDays(3), OrderStatus.Created, 1);
            var failedDay = NewDay(owned.Id, restaurantId, start.AddDays(4), SubscriptionDayStatus.Delivered);
            var failedOrder = NewOrder(failedDay.Id, restaurantId, start.AddDays(4), OrderStatus.DeliveryFailed, 1);
            var cancelledDay = NewDay(owned.Id, restaurantId, start.AddDays(5), SubscriptionDayStatus.Delivered);
            var cancelledOrder = NewOrder(cancelledDay.Id, restaurantId, start.AddDays(5), OrderStatus.Cancelled, 1);
            var softDeletedDay = NewDay(owned.Id, restaurantId, start.AddDays(6), SubscriptionDayStatus.Delivered);
            softDeletedDay.IsDeleted = true;
            var softDeletedOrder = NewOrder(softDeletedDay.Id, restaurantId, start.AddDays(6), OrderStatus.Delivered, 1);
            softDeletedOrder.IsDeleted = true;
            var softDeletedOrderDay = NewDay(owned.Id, restaurantId, start.AddDays(7), SubscriptionDayStatus.Delivered);
            var softDeletedOnlyOrder = NewOrder(softDeletedOrderDay.Id, restaurantId, start.AddDays(7), OrderStatus.Delivered, 1);
            softDeletedOnlyOrder.IsDeleted = true;
            var foreignDay = NewDay(foreign.Id, restaurantId, start.AddDays(8), SubscriptionDayStatus.Delivered);
            var foreignOrder = NewOrder(foreignDay.Id, restaurantId, start.AddDays(8), OrderStatus.Delivered, 2);

            var firstTrip = NewTrip(restaurantId, "delivered-first", start.AddDays(2));
            var latestTrip = NewTrip(restaurantId, "delivered-latest", start.AddDays(2));
            var failedTrip = NewTrip(restaurantId, "failed-attempt", start.AddDays(2));
            var legacyTrip = NewTrip(restaurantId, "legacy-delivered", start.AddDays(1));
            var firstAt = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
            var latestAt = firstAt.AddHours(2);
            var legacyAt = new DateTime(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc);
            db.AddRange(firstTrip, latestTrip, failedTrip, legacyTrip);
            var legacyStop = NewStop(legacyTrip.Id, null, "LEGACY-ORDER", TripStopStatus.Delivered, legacyAt);
            legacyStop.SubscriptionDayId = fallbackDay.Id;
            db.AddRange(
                NewStop(firstTrip.Id, deliveredOrder.Id, "ORDER-OLD", TripStopStatus.Delivered, firstAt),
                NewStop(latestTrip.Id, deliveredOrder.Id, "ORDER-LATEST", TripStopStatus.Delivered, latestAt),
                NewStop(failedTrip.Id, deliveredOrder.Id, "ORDER-FAILED", TripStopStatus.Failed, latestAt.AddHours(1)),
                legacyStop);
            db.AddRange(deliveredDay, fallbackDay, pendingDay, failedDay, cancelledDay, softDeletedDay, softDeletedOrderDay, foreignDay);
            db.AddRange(deliveredOrder, pendingOrder, failedOrder, cancelledOrder, softDeletedOrder, softDeletedOnlyOrder, foreignOrder);
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var response = await customer.GetAsync("/api/v1/support/delivered-boxes?fromDate=2026-10-20&toDate=2026-10-28&pageNumber=1&pageSize=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var firstPage = await response.Content.ReadFromJsonAsync<SupportDeliveredBoxesPageDto>();
        firstPage.Should().NotBeNull();
        firstPage!.TotalCount.Should().Be(2);
        firstPage.PageNumber.Should().Be(1);
        firstPage.PageSize.Should().Be(1);
        firstPage.Items.Should().ContainSingle();
        firstPage.Items[0].RelatedResourceId.Should().Be(ids.DeliveredOrderId);
        firstPage.Items[0].RelatedResourceType.Should().Be("RestaurantDailyOrder");
        firstPage.Items[0].BoxCount.Should().Be(3);
        firstPage.Items[0].OrderCode.Should().Be("ORDER-LATEST");
        firstPage.Items[0].DeliveredAtUtc.Should().Be(new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));

        var secondPage = await customer.GetFromJsonAsync<SupportDeliveredBoxesPageDto>("/api/v1/support/delivered-boxes?pageNumber=2&pageSize=1");
        secondPage.Should().NotBeNull();
        secondPage!.TotalCount.Should().Be(2);
        secondPage.Items.Should().ContainSingle(x => x.RelatedResourceType == "SubscriptionDay"
            && x.RelatedResourceId == ids.FallbackDayId && x.BoxCount == null
            && x.OrderCode == "LEGACY-ORDER" && x.DeliveredAtUtc == new DateTime(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc));

        var subscriptionFilter = await customer.GetFromJsonAsync<SupportDeliveredBoxesPageDto>(
            $"/api/v1/support/delivered-boxes?subscriptionId={Guid.NewGuid()}");
        subscriptionFilter!.TotalCount.Should().Be(0);
        var dateFilter = await customer.GetFromJsonAsync<SupportDeliveredBoxesPageDto>(
            "/api/v1/support/delivered-boxes?fromDate=2026-10-22&toDate=2026-10-22");
        dateFilter!.Items.Should().ContainSingle(x => x.RelatedResourceId == ids.DeliveredOrderId);
    }

    [Fact]
    public async Task ReturnedSelectorCanBindComplaintButCannotBindAnotherCustomersOrder()
    {
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 23);
        var orderId = Guid.NewGuid();
        var fallbackDayId = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext())
        {
            var program = NewProgram();
            var bundle = NewBundle(program.Id);
            var owned = NewSubscription(customerId, program.Id, bundle.Id, date);
            var foreign = NewSubscription(otherCustomerId, program.Id, bundle.Id, date);
            var ownDay = NewDay(owned.Id, restaurantId, date, SubscriptionDayStatus.Delivered);
            var fallbackDay = NewDay(owned.Id, restaurantId, date.AddDays(-1), SubscriptionDayStatus.Delivered);
            fallbackDay.Id = fallbackDayId;
            var foreignDay = NewDay(foreign.Id, restaurantId, date, SubscriptionDayStatus.Delivered);
            var ownedOrder = NewOrder(ownDay.Id, restaurantId, date, OrderStatus.Delivered, 2);
            ownedOrder.Id = orderId;
            var foreignOrder = NewOrder(foreignDay.Id, restaurantId, date, OrderStatus.Delivered, 1);
            db.AddRange(program, bundle, owned, foreign, ownDay, fallbackDay, foreignDay, ownedOrder, foreignOrder, NewRestaurant(restaurantId));
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var selector = await customer.GetFromJsonAsync<SupportDeliveredBoxesPageDto>("/api/v1/support/delivered-boxes");
        selector.Should().NotBeNull();
        selector!.Items.Should().HaveCount(2);
        var selectedOrder = selector.Items.Single(item => item.RelatedResourceType == "RestaurantDailyOrder");
        selectedOrder.RelatedResourceId.Should().Be(orderId);
        var selectedLegacyDay = selector.Items.Single(item => item.RelatedResourceType == "SubscriptionDay");
        selectedLegacyDay.RelatedResourceId.Should().Be(fallbackDayId);

        var orderCreate = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", new CustomerSupportCreateRequest(
            "MissingItems", "Delivered box issue", "Something was wrong.", "delivered-box-selector-order-issue",
            selectedOrder.RelatedResourceType, selectedOrder.RelatedResourceId));
        orderCreate.StatusCode.Should().Be(HttpStatusCode.Created, await orderCreate.Content.ReadAsStringAsync());
        var createdOrderCase = await orderCreate.Content.ReadFromJsonAsync<SupportCaseDto>();
        createdOrderCase!.Kind.Should().Be(nameof(SupportCaseKind.Complaint));
        createdOrderCase.Category.Should().Be("customer.issue.MissingItems");
        createdOrderCase.RelatedResourceType.Should().Be("RestaurantDailyOrder");
        createdOrderCase.RelatedResourceId.Should().Be(orderId);

        var dayCreate = await customer.PostAsJsonAsync("/api/v1/customer/support/issues", new CustomerSupportCreateRequest(
            "MissingItems", "Legacy delivered day issue", "Something was wrong.", "delivered-box-selector-day-issue",
            selectedLegacyDay.RelatedResourceType, selectedLegacyDay.RelatedResourceId));
        dayCreate.StatusCode.Should().Be(HttpStatusCode.Created, await dayCreate.Content.ReadAsStringAsync());
        var createdDayCase = await dayCreate.Content.ReadFromJsonAsync<SupportCaseDto>();
        createdDayCase!.Kind.Should().Be(nameof(SupportCaseKind.Complaint));
        createdDayCase.Category.Should().Be("customer.issue.MissingItems");
        createdDayCase.RelatedResourceType.Should().Be("SubscriptionDay");
        createdDayCase.RelatedResourceId.Should().Be(fallbackDayId);

        await using (var db = _fixture.CreateDbContext())
        {
            var persistedCases = await db.SupportCases.AsNoTracking()
                .Where(supportCase => supportCase.Id == createdOrderCase.Id || supportCase.Id == createdDayCase.Id)
                .ToDictionaryAsync(supportCase => supportCase.Id);
            persistedCases[createdOrderCase.Id].RelatedResourceType.Should().Be("RestaurantDailyOrder");
            persistedCases[createdOrderCase.Id].RelatedResourceId.Should().Be(orderId);
            persistedCases[createdDayCase.Id].RelatedResourceType.Should().Be("SubscriptionDay");
            persistedCases[createdDayCase.Id].RelatedResourceId.Should().Be(fallbackDayId);
        }

        using var other = _fixture.CreateClientForUser(new TokenUser(otherCustomerId, UserType.Customer, ["Customer"], []));
        var forged = await other.PostAsJsonAsync("/api/v1/customer/support/issues", new CustomerSupportCreateRequest(
            "MissingItems", "Foreign order", "Should not bind.", "foreign-delivered-box-selector-case",
            selectedOrder.RelatedResourceType, selectedOrder.RelatedResourceId));
        forged.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateReplayIsResolvedBeforeSoftDeletedResourceAndStillChecksChangedPayload()
    {
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 23);
        var dayId = Guid.NewGuid();
        await using (var db = _fixture.CreateDbContext())
        {
            var program = NewProgram();
            var bundle = NewBundle(program.Id);
            var subscription = NewSubscription(customerId, program.Id, bundle.Id, date);
            var day = NewDay(subscription.Id, restaurantId, date, SubscriptionDayStatus.Delivered);
            day.Id = dayId;
            db.AddRange(program, bundle, subscription, day, NewRestaurant(restaurantId));
            await db.SaveChangesAsync();
        }

        using var customer = _fixture.CreateClientForUser(new TokenUser(customerId, UserType.Customer, ["Customer"], []));
        var request = new CreateSupportCaseRequest(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            "delivery", "Delivered day issue", "Please review this delivered day.",
            "soft-deleted-resource-create-replay", "SubscriptionDay", dayId);
        var createdResponse = await customer.PostAsJsonAsync("/api/v1/support/cases", request);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createdResponse.Content.ReadAsStringAsync());
        var created = await createdResponse.Content.ReadFromJsonAsync<SupportCaseDto>();
        created.Should().NotBeNull();

        await using (var db = _fixture.CreateDbContext())
        {
            var day = await db.SubscriptionDays.SingleAsync(subscriptionDay => subscriptionDay.Id == dayId);
            day.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var replay = await customer.PostAsJsonAsync("/api/v1/support/cases", request);
        replay.StatusCode.Should().Be(HttpStatusCode.Created, await replay.Content.ReadAsStringAsync());
        (await replay.Content.ReadFromJsonAsync<SupportCaseDto>())!.Id.Should().Be(created!.Id);

        var changedReplay = await customer.PostAsJsonAsync("/api/v1/support/cases", request with { Subject = "Changed after deletion" });
        changedReplay.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var newKeyRequest = request with { IdempotencyKey = "soft-deleted-resource-new-key" };
        var missingResource = await customer.PostAsJsonAsync("/api/v1/support/cases", newKeyRequest);
        missingResource.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static MealMate.Domain.ProgramsSubscriptions.Program NewProgram()
        => new() { Code = $"support-boxes-{Guid.NewGuid():N}", NameAr = "خطة", NameEn = "Plan" };

    private static Bundle NewBundle(Guid programId)
        => new() { ProgramId = programId, Code = $"support-boxes-{Guid.NewGuid():N}", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };

    private static Subscription NewSubscription(Guid customerId, Guid programId, Guid bundleId, DateOnly startDate)
        => new()
        {
            CustomerUserId = customerId,
            ProgramId = programId,
            BundleId = bundleId,
            Status = SubscriptionStatus.Active,
            PaidAmount = new Money(10m, "KWD"),
            StartDate = startDate,
            EndDate = startDate.AddDays(30),
            DurationDays = 30
        };

    private static RestaurantProfile NewRestaurant(Guid id)
        => new()
        {
            Id = id,
            UserId = Guid.NewGuid(),
            TradeNameAr = "مطعم الاختبار",
            TradeNameEn = "Test restaurant",
            LegalNameAr = "مطعم الاختبار القانوني",
            LegalNameEn = "Test Restaurant LLC",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };

    private static SubscriptionDay NewDay(Guid subscriptionId, Guid restaurantId, DateOnly date, SubscriptionDayStatus status)
        => new() { SubscriptionId = subscriptionId, RestaurantId = restaurantId, ServiceDate = date, Status = status };

    private static RestaurantDailyOrder NewOrder(Guid dayId, Guid restaurantId, DateOnly date, OrderStatus status, int boxCount)
        => new() { SubscriptionDayId = dayId, RestaurantId = restaurantId, ServiceDate = date, Status = status, BoxCount = boxCount };

    private static Trip NewTrip(Guid restaurantId, string code, DateOnly date)
        => new() { RestaurantId = restaurantId, Code = $"{code}-{Guid.NewGuid():N}", PlannedDate = date };

    private static TripStop NewStop(Guid tripId, Guid? orderId, string code, TripStopStatus status, DateTime? deliveredAtUtc)
        => new()
        {
            TripId = tripId,
            DailyOrderId = orderId,
            OrderCode = code,
            CustomerMaskedId = "C-TEST",
            ZoneNameAr = "المنطقة",
            ZoneNameEn = "Test zone",
            MealSummaryAr = "وجبة",
            MealSummaryEn = "Meal",
            SlotLabel = "AM",
            Status = status,
            DeliveredAtUtc = deliveredAtUtc
        };

    private sealed class DeliveredBoxSeedIds
    {
        public Guid DeliveredOrderId { get; } = Guid.NewGuid();
        public Guid FallbackDayId { get; } = Guid.NewGuid();
    }
}
