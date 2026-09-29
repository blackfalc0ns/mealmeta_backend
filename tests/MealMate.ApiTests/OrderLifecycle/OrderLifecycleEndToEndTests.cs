using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace MealMate.ApiTests.OrderLifecycle;

[Collection("OrderLifecycleE2E")]
public sealed class OrderLifecycleEndToEndTests : IClassFixture<OrderLifecycleEndToEndFixture>
{
    private readonly OrderLifecycleEndToEndFixture _fixture;
    private readonly ITestOutputHelper _output;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public OrderLifecycleEndToEndTests(OrderLifecycleEndToEndFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task GoldenPath_CustomerReadinessToDriverAssignmentAndOutboxPush()
    {
        _output.WriteLine("================================================================================");
        _output.WriteLine("STARTING GOLDEN PATH END-TO-END TEST");
        _output.WriteLine("================================================================================");

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)); // within allowed booking horizon
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var customerClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.CustomerUser.Id, UserType.Customer, ["Customer"], []));
        var kitchenClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.KitchenStaffA.Id, UserType.Restaurant, ["Restaurant"], [], fixture.RestaurantA.Id));
        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher", "DeliveryManager"], [], fixture.RestaurantA.Id));

        // -------------------------------------------------------------------------
        // STEP 1: Customer Readiness Check
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 1] Checking Customer Subscription Readiness...");
        var readinessUri = $"/api/v1/customer/subscription-flow/readiness?ProgramId={fixture.Program.Id}&BundleId={fixture.Bundle.Id}&Tier=Basic&DurationDays=26&StartDate={startDate:yyyy-MM-dd}&IncludeFriday=false";
        var readinessResp = await customerClient.GetAsync(readinessUri);
        readinessResp.StatusCode.Should().Be(HttpStatusCode.OK, "Readiness endpoint must succeed for qualified profile");
        
        var readinessDto = await readinessResp.Content.ReadFromJsonAsync<SubscriptionCheckoutReadinessDto>(JsonOptions);
        readinessDto.Should().NotBeNull();
        readinessDto!.CanCheckout.Should().BeTrue("All readiness gates (address, allergies, preferences) are satisfied");
        readinessDto.DeliveringRestaurantCount.Should().BeGreaterThan(0);
        _output.WriteLine($"[STEP 1 PASS] CanCheckout: {readinessDto.CanCheckout}, Delivering Restaurants: {readinessDto.DeliveringRestaurantCount}");

        // -------------------------------------------------------------------------
        // STEP 2: Start Checkout via API
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 2] Starting Subscription Checkout via API...");

        var checkoutReq = new StartCheckoutRequest(
            ProgramId: fixture.Program.Id,
            BundleId: fixture.Bundle.Id,
            Tier: RestaurantTier.Basic,
            DurationDays: 26,
            CallbackUrl: "https://maelmate.runasp.net/checkout/callback",
            ErrorUrl: "https://maelmate.runasp.net/checkout/error",
            StartDate: startDate,
            IncludeFriday: false,
            PaymentMethodId: 1
        );
        var checkoutResp = await customerClient.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", checkoutReq);
        if (checkoutResp.StatusCode != HttpStatusCode.OK)
        {
            var err = await checkoutResp.Content.ReadAsStringAsync();
            _output.WriteLine($"[STEP 2 FAILED] Status: {checkoutResp.StatusCode}, Body: {err}");
        }
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.OK, "Checkout initialization must succeed");

        var checkoutStarted = await checkoutResp.Content.ReadFromJsonAsync<CheckoutStartedDto>(JsonOptions);
        checkoutStarted.Should().NotBeNull();
        var subscriptionId = checkoutStarted!.SubscriptionId;
        subscriptionId.Should().NotBeEmpty();
        checkoutStarted.PaymentUrl.Should().NotBeNullOrWhiteSpace();
        _output.WriteLine($"[STEP 2 PASS] SubscriptionId: {subscriptionId}, PaymentUrl: {checkoutStarted.PaymentUrl}");

        // Read-only DB check: Capacity holds are created
        decimal subscriptionAmount;
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var holdCount = await verifyDb.CapacityHolds.CountAsync(h => h.SubscriptionId == subscriptionId && h.Status == CapacityHoldStatus.PendingPayment);
            holdCount.Should().Be(26, "Capacity holds must be held for all 26 service dates");

            var sub = await verifyDb.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
            subscriptionAmount = sub.PaidAmount.Amount;
        }

        // -------------------------------------------------------------------------
        // STEP 3: Confirm Payment via Signed Webhook and Payment Return
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 3] Confirming Payment via Signed Webhook & Return URL...");
        var paymentId = PaymentIdFromUrl(checkoutStarted.PaymentUrl) ?? ("PAY-" + Guid.NewGuid().ToString("N")[..8]);
        var invoiceId = "INV-" + paymentId;
        _fixture.PaymentService.PaymentCustomerReferences[paymentId] = subscriptionId.ToString();
        _fixture.PaymentService.PaymentInvoiceValues[paymentId] = subscriptionAmount;

        // Webhook request
        var webhookPayload = new
        {
            Event = new { Code = 1, Name = "PAYMENT_STATUS_CHANGED" },
            Data = new
            {
                Invoice = new
                {
                    Id = invoiceId,
                    Status = "Paid",
                    UserDefinedField = subscriptionId.ToString(),
                    ExternalIdentifier = subscriptionId.ToString()
                },
                Transaction = new
                {
                    Status = "SUCCESS",
                    PaymentId = paymentId
                },
                Amount = new
                {
                    ValueInBaseCurrency = subscriptionAmount.ToString("F2", CultureInfo.InvariantCulture),
                    BaseCurrency = "KWD"
                }
            }
        };

        var signatureData = $"Invoice.Id={invoiceId},Invoice.Status=Paid,Transaction.Status=SUCCESS,Transaction.PaymentId={paymentId},Invoice.ExternalIdentifier={subscriptionId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-webhook-secret-key-12345"));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureData)));

        var webhookMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/myfatoorah")
        {
            Content = JsonContent.Create(webhookPayload)
        };
        webhookMsg.Headers.Add("MyFatoorah-Signature", signature);

        var unauthClient = _fixture.Factory.CreateClient();
        var webhookResp = await unauthClient.SendAsync(webhookMsg);
        if (webhookResp.StatusCode != HttpStatusCode.OK)
        {
            var err = await webhookResp.Content.ReadAsStringAsync();
            _output.WriteLine($"[WEBHOOK FAILED] Status: {webhookResp.StatusCode}, Body: {err}");
        }
        webhookResp.StatusCode.Should().Be(HttpStatusCode.OK, "Valid signed webhook must be accepted");

        // Payment return URL request
        var returnResp = await unauthClient.GetAsync($"/api/v1/payments/myfatoorah/return?to=mealmate://checkout/success&paymentId={paymentId}&Id={paymentId}&InvoiceId={invoiceId}");
        if (returnResp.StatusCode != HttpStatusCode.OK)
        {
            var err = await returnResp.Content.ReadAsStringAsync();
            _output.WriteLine($"[RETURN FAILED] Status: {returnResp.StatusCode}, Body: {err}");
        }
        returnResp.StatusCode.Should().Be(HttpStatusCode.OK, "Payment return URL must process idempotently");

        // Verify in DB: Subscription is Active, days generated, holds confirmed
        Guid firstDayId;
        DateOnly firstServiceDate;
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var sub = await verifyDb.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
            sub.Status.Should().BeOneOf(SubscriptionStatus.Active, SubscriptionStatus.Scheduled);
            sub.FulfillmentCompletedAtUtc.Should().NotBeNull();

            var days = await verifyDb.SubscriptionDays
                .Where(d => d.SubscriptionId == subscriptionId)
                .OrderBy(d => d.ServiceDate)
                .ToListAsync();
            days.Should().HaveCount(26, "26 SubscriptionDays must be generated on fulfillment");
            firstDayId = days[0].Id;
            firstServiceDate = days[0].ServiceDate;

            var confirmedHolds = await verifyDb.CapacityHolds
                .CountAsync(h => h.SubscriptionId == subscriptionId && h.Status == CapacityHoldStatus.Confirmed);
            confirmedHolds.Should().Be(26, "All 26 capacity holds must transition to Confirmed");
        }
        _output.WriteLine($"[STEP 3 PASS] Payment confirmed. First SubscriptionDayId: {firstDayId}, ServiceDate: {firstServiceDate}");

        // -------------------------------------------------------------------------
        // STEP 4: Deterministic Day Cutoff & Restaurant Order Dispatch
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 4] Deterministic Day Cutoff & Restaurant Order Dispatch...");
        
        // Advance deadline to past and designate Restaurant A for deterministic cutoff
        await using (var seedScope = _fixture.CreateDbContext())
        {
            var day = await seedScope.SubscriptionDays.SingleAsync(d => d.Id == firstDayId);
            day.RestaurantId = fixture.RestaurantA.Id;
            day.SelectionDeadlineUtc = DateTime.UtcNow.AddMinutes(-5);
            await seedScope.SaveChangesAsync();
        }

        // Trigger deterministic day cutoff & dispatch
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<CustomerCalendarEngine>();
            var autoResult = await engine.AutoSelectFullBoxAsync(
                subscriptionId, firstServiceDate, lockAfter: true, requireOwner: false, CancellationToken.None);
            if (autoResult.IsFailure)
            {
                _output.WriteLine($"[CUTOFF ERROR] {autoResult.Error.Code} - {autoResult.Error.Description}");
            }
            autoResult.IsSuccess.Should().BeTrue("Day cutoff and auto-selection must succeed");
        }

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var dailyOrder = await verifyDb.RestaurantDailyOrders
                .FirstOrDefaultAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Should().NotBeNull("RestaurantDailyOrder must be created for the dispatched day");
            dailyOrder!.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);
        }
        _output.WriteLine("[STEP 4 PASS] RestaurantDailyOrder created in AwaitingRestaurantConfirmation status.");

        // -------------------------------------------------------------------------
        // STEP 5: Restaurant Order Confirmation
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 5] Restaurant confirms daily order...");
        var confirmResp = await kitchenClient.PostAsJsonAsync($"/api/v1/restaurants/orders/{firstDayId}/confirm", new { note = "Kitchen accepted" });
        confirmResp.StatusCode.Should().Be(HttpStatusCode.OK, "Kitchen confirmation must succeed");

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Status.Should().Be(OrderStatus.RestaurantConfirmed);
        }
        _output.WriteLine("[STEP 5 PASS] Daily order status transitioned to RestaurantConfirmed.");

        // -------------------------------------------------------------------------
        // STEP 6: Kitchen Start Prep
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 6] Kitchen updates prep status to 'preparing'...");
        var prepResp = await kitchenClient.PutAsJsonAsync($"/api/v1/restaurants/orders/daily/{firstDayId}/prep-status", new { status = "preparing" });
        prepResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Status.Should().Be(OrderStatus.Preparing);
        }
        _output.WriteLine("[STEP 6 PASS] Daily order status transitioned to Preparing.");

        // -------------------------------------------------------------------------
        // STEP 7: Request Prep Documents & Canonical Barcode Issuance
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 7] Requesting prep documents and checking canonical barcode...");
        var docsResp = await kitchenClient.GetAsync($"/api/v1/restaurants/orders/{firstDayId}/prep-documents");
        docsResp.StatusCode.Should().Be(HttpStatusCode.OK, "Prep documents generation must succeed");

        var docsDto = await docsResp.Content.ReadFromJsonAsync<RestaurantPrepDocumentsDto>(JsonOptions);
        docsDto.Should().NotBeNull();
        docsDto!.BarcodeValue.Should().NotBeNullOrWhiteSpace("A canonical barcode value must be issued");
        var canonicalBarcode = docsDto.BarcodeValue!;

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var barcodeRecord = await verifyDb.MealBarcodes.FirstOrDefaultAsync(b => b.SubscriptionDayId == firstDayId);
            barcodeRecord.Should().NotBeNull();
            barcodeRecord!.BarcodeValue.Should().Be(canonicalBarcode);

            // Verify printing docs does NOT prematurely change order status
            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Status.Should().Be(OrderStatus.Preparing, "Printing prep documents must not mutate order status");
        }
        _output.WriteLine($"[STEP 7 PASS] Barcode generated: {canonicalBarcode}. Status remains Preparing.");

        // -------------------------------------------------------------------------
        // STEP 8: Confirm Kitchen Ready
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 8] Kitchen marks box as 'ready' (ReadyForPickup)...");
        var readyResp = await kitchenClient.PutAsJsonAsync($"/api/v1/restaurants/orders/daily/{firstDayId}/prep-status", new { status = "ready" });
        readyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Status.Should().Be(OrderStatus.ReadyForPickup);
            dailyOrder.ReadyForPickupAtUtc.Should().NotBeNull();
        }
        _output.WriteLine("[STEP 8 PASS] Order status is ReadyForPickup with ReadyForPickupAtUtc set.");

        // -------------------------------------------------------------------------
        // STEP 9: Query Dispatcher Order Queue
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 9] Dispatcher queries unassigned order queue...");
        var queueResp = await dispatcherClient.GetAsync($"/api/v1/dispatcher/orders/queue?restaurantId={fixture.RestaurantA.Id}");
        queueResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var queueDto = await queueResp.Content.ReadFromJsonAsync<DispatcherOrderQueueDto>(JsonOptions);
        queueDto.Should().NotBeNull();
        queueDto!.Boxes.Should().Contain(b => b.BoxId == firstDayId, "The ready box must appear in the dispatcher queue");
        var queueBox = queueDto.Boxes.First(b => b.BoxId == firstDayId);
        queueBox.BoxCode.Should().Be(canonicalBarcode, "Queue must display the persisted canonical barcode value");
        _output.WriteLine($"[STEP 9 PASS] Box {firstDayId} visible in dispatcher queue with code: {queueBox.BoxCode}.");

        // -------------------------------------------------------------------------
        // STEP 10: Assign Box to Driver via Real API
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 10] Assigning box to driver via Dispatcher API...");
        var assignReq = new { driverId = fixture.DriverProfileA.Id, notes = "Deliver carefully" };
        var assignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{firstDayId}/assign", assignReq);
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK, "Driver assignment must succeed");

        var assignResult = await assignResp.Content.ReadFromJsonAsync<AssignBoxResponse>(JsonOptions);
        assignResult.Should().NotBeNull();
        assignResult!.DriverId.Should().Be(fixture.DriverProfileA.Id);
        assignResult.BoxCode.Should().Be(canonicalBarcode);
        _output.WriteLine($"[STEP 10 PASS] Box assigned to driver {fixture.DriverProfileA.Id}.");

        // -------------------------------------------------------------------------
        // STEP 11: Read-Only Database Verification
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 11] Performing read-only database verification of Trip, Stop, Assignment, and Outbox...");
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var stop = await verifyDb.TripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.SubscriptionDayId == firstDayId);

            stop.Should().NotBeNull("TripStop must exist for the assigned subscription day");
            stop!.OrderCode.Should().Be(canonicalBarcode, "TripStop OrderCode must strictly equal canonical BarcodeValue");
            stop.Status.Should().Be(TripStopStatus.Pending);

            stop.Trip.Should().NotBeNull();
            stop.Trip.Status.Should().Be(TripStatus.Assigned);
            stop.Trip.RestaurantId.Should().Be(fixture.RestaurantA.Id);

            var activeAssignment = stop.Trip.DriverAssignments.FirstOrDefault(da => da.Status == "Active");
            activeAssignment.Should().NotBeNull();
            activeAssignment!.DriverProfileId.Should().Be(fixture.DriverProfileA.Id);

            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == firstDayId);
            dailyOrder.Status.Should().Be(OrderStatus.AssignedToDriver);

            var outboxMsg = await verifyDb.OutboxMessages
                .Where(m => m.EventType == "DriverBoxAssigned")
                .OrderByDescending(m => m.CreatedAtUtc)
                .FirstOrDefaultAsync();

            outboxMsg.Should().NotBeNull("OutboxMessage with EventType 'DriverBoxAssigned' must be created");
            outboxMsg!.PayloadJson.Should().Contain(firstDayId.ToString());
            outboxMsg.PayloadJson.Should().Contain(canonicalBarcode);
        }
        _output.WriteLine("[STEP 11 PASS] Trip, TripStop, DriverAssignment, and OutboxMessage verified in DB.");

        // -------------------------------------------------------------------------
        // STEP 12: Execute Outbox Background Worker and Verify Driver Push
        // -------------------------------------------------------------------------
        _output.WriteLine("[STEP 12] Executing Outbox Background Worker tick...");
        var scopeFactory = _fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
        var outboxJob = new OutboxBackgroundJob(scopeFactory);
        var processedCount = await outboxJob.ProcessPendingMessagesAsync(10, CancellationToken.None);
        processedCount.Should().BeGreaterThan(0, "Outbox messages must be processed by the worker");

        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var outboxMsg = await verifyDb.OutboxMessages
                .Where(m => m.EventType == "DriverBoxAssigned" && m.PayloadJson.Contains(firstDayId.ToString()))
                .SingleAsync();

            outboxMsg.ProcessedAtUtc.Should().NotBeNull("Outbox message must be marked processed");
            outboxMsg.LockedByWorkerId.Should().BeNull("Lock must be released on completion");
        }

        _fixture.PushNotifier.BoxAssignments.Should().Contain(a => a.BoxCode == canonicalBarcode && a.DriverUserId == fixture.DriverUserA.Id);
        _output.WriteLine($"[STEP 12 PASS] Outbox message processed. Driver push notifier received notification for code: {canonicalBarcode}.");

        _output.WriteLine("================================================================================");
        _output.WriteLine("GOLDEN PATH COMPLETED SUCCESSFULLY (12/12 STEPS VERIFIED)");
        _output.WriteLine("================================================================================");
    }

    [Fact]
    public async Task FailurePath_DuplicatePaymentOrConcurrentWebhookReturn_ResultsInSingleFulfillment()
    {
        _output.WriteLine("--- Testing Failure Path: Duplicate Payment & Concurrent Webhook/Return ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var customerClient = _fixture.CreateClientForUser(new TokenUser(fixture.CustomerUser.Id, UserType.Customer, ["Customer"], []));
        var checkoutReq = new StartCheckoutRequest(
            fixture.Program.Id, fixture.Bundle.Id, RestaurantTier.Basic, 26,
            "https://maelmate.runasp.net/checkout/callback", "https://maelmate.runasp.net/checkout/error", startDate, false);

        var checkoutResp = await customerClient.PostAsJsonAsync("/api/v1/customer/subscriptions/checkout", checkoutReq);
        if (checkoutResp.StatusCode != HttpStatusCode.OK)
        {
            var err = await checkoutResp.Content.ReadAsStringAsync();
            _output.WriteLine($"[DUPLICATE TEST CHECKOUT FAILED] Status: {checkoutResp.StatusCode}, Body: {err}");
        }
        checkoutResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await checkoutResp.Content.ReadFromJsonAsync<CheckoutStartedDto>(JsonOptions);
        var subscriptionId = started!.SubscriptionId;

        decimal subscriptionAmount;
        await using (var subDb = _fixture.CreateDbContext())
        {
            var sub = await subDb.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
            subscriptionAmount = sub.PaidAmount.Amount;
        }

        var paymentId = PaymentIdFromUrl(started.PaymentUrl) ?? ("PAY-CONC-" + Guid.NewGuid().ToString("N")[..8]);
        var invoiceId = "INV-" + paymentId;
        _fixture.PaymentService.PaymentCustomerReferences[paymentId] = subscriptionId.ToString();
        _fixture.PaymentService.PaymentInvoiceValues[paymentId] = subscriptionAmount;

        var signatureData = $"Invoice.Id={invoiceId},Invoice.Status=Paid,Transaction.Status=SUCCESS,Transaction.PaymentId={paymentId},Invoice.ExternalIdentifier={subscriptionId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-webhook-secret-key-12345"));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureData)));

        var webhookMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/myfatoorah")
        {
            Content = JsonContent.Create(new
            {
                Event = new { Code = 1, Name = "PAYMENT_STATUS_CHANGED" },
                Data = new
                {
                    Invoice = new { Id = invoiceId, Status = "Paid", ExternalIdentifier = subscriptionId.ToString() },
                    Transaction = new { Status = "SUCCESS", PaymentId = paymentId },
                    Amount = new { ValueInBaseCurrency = subscriptionAmount.ToString("F2", CultureInfo.InvariantCulture), BaseCurrency = "KWD" }
                }
            })
        };
        webhookMsg.Headers.Add("MyFatoorah-Signature", signature);

        var unauthClient = _fixture.Factory.CreateClient();

        // Concurrently run Webhook and Return URL
        var webhookTask = unauthClient.SendAsync(webhookMsg);
        var returnTask = unauthClient.GetAsync($"/api/v1/payments/myfatoorah/return?to=mealmate://cb&paymentId={paymentId}&Id={paymentId}&InvoiceId={invoiceId}");

        await Task.WhenAll(webhookTask, returnTask);

        // Verify exactly one receipt exists and exactly 26 days created (no duplication)
        await using var verifyDb = _fixture.CreateDbContext();
        var receipts = await verifyDb.PaymentWebhookReceipts.CountAsync(r => r.PaymentId == paymentId);
        receipts.Should().Be(1, "Exactly one PaymentWebhookReceipt must be recorded");

        var dayCount = await verifyDb.SubscriptionDays.CountAsync(d => d.SubscriptionId == subscriptionId);
        dayCount.Should().Be(26, "Exactly 26 service days must be generated");

        _output.WriteLine("[PASS] Single fulfillment achieved across concurrent payment confirmation.");
    }

    [Fact]
    public async Task FailurePath_PreparingOrUnbarcodedBox_ExcludedFromQueueAndRejectedOnAssignment()
    {
        _output.WriteLine("--- Testing Failure Path: Preparing/Unbarcoded Box Excluded & Rejected ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var dailyOrder = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.Preparing, // Not yet ReadyForPickup!
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(dailyOrder);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        // 1. Should NOT appear in dispatcher queue
        var queueResp = await dispatcherClient.GetAsync($"/api/v1/dispatcher/orders/queue?restaurantId={fixture.RestaurantA.Id}");
        var queue = await queueResp.Content.ReadFromJsonAsync<DispatcherOrderQueueDto>(JsonOptions);
        queue!.Boxes.Should().NotContain(b => b.BoxId == day.Id, "Preparing boxes must not appear in the assignment queue");

        // 2. Direct assignment attempt must be rejected
        var assignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
            new { driverId = fixture.DriverProfileA.Id });
        assignResp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Assigning a box that is not ReadyForPickup must be rejected");

        _output.WriteLine("[PASS] Preparing or unbarcoded box correctly hidden and rejected on assignment.");
    }

    [Fact]
    public async Task FailurePath_CrossRestaurantTenantIsolation_DispatcherCannotViewOrAssignOtherRestaurantBox()
    {
        _output.WriteLine("--- Testing Failure Path: Cross-Restaurant Tenant Isolation ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        // Create ready box for Restaurant B
        var dayB = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantB.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(dayB);

        var barcodeB = new MealBarcode
        {
            SubscriptionDayId = dayB.Id,
            BarcodeValue = "MM-KW-RESTB-001",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcodeB);

        var orderB = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantB.Id,
            SubscriptionDayId = dayB.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(orderB);
        await db.SaveChangesAsync();

        // Dispatcher from Restaurant A tries to assign Restaurant B's box
        var dispatcherClientA = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        var assignResp = await dispatcherClientA.PostAsJsonAsync($"/api/v1/dispatcher/orders/{dayB.Id}/assign",
            new { driverId = fixture.DriverProfileA.Id });

        assignResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Dispatcher from Restaurant A cannot assign boxes of Restaurant B");

        // Verify DB unchanged
        await using var verifyDb = _fixture.CreateDbContext();
        var stopCount = await verifyDb.TripStops.CountAsync(s => s.SubscriptionDayId == dayB.Id);
        stopCount.Should().Be(0, "Cross-restaurant assignment attempt must not write any TripStop records");

        _output.WriteLine("[PASS] Cross-restaurant tenant isolation enforced (403 Forbidden).");
    }

    [Fact]
    public async Task FailurePath_ConcurrentSingleVsBulkAssignment_OnlyOneSucceedsAndNoDuplicateStop()
    {
        _output.WriteLine("--- Testing Failure Path: Concurrent Single vs Bulk Assignment ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-KW-CONC-" + Guid.NewGuid().ToString("N")[..6],
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcode);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        // Concurrently run single assign vs bulk assign for the same box
        var singleTask = dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
            new { driverId = fixture.DriverProfileA.Id, notes = "Single assignment" });
        var bulkTask = dispatcherClient.PostAsJsonAsync("/api/v1/dispatcher/orders/bulk-assign",
            new { boxIds = new[] { day.Id }, driverId = fixture.DriverProfileA.Id, notes = "Bulk assignment" });

        var responses = await Task.WhenAll(singleTask, bulkTask);
        foreach (var r in responses)
        {
            var b = await r.Content.ReadAsStringAsync();
            _output.WriteLine($"[CONCURRENT ASSIGN RESP] {r.StatusCode}: {b}");
        }

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var rejectedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

        successCount.Should().Be(1, "Exactly one concurrent assignment request must succeed");
        rejectedCount.Should().Be(1, "Conflicting assignment request must be rejected (either 409 Conflict or 400 Box.NotReadyForPickup)");

        // Verify DB: exactly 1 stop exists
        await using var verifyDb = _fixture.CreateDbContext();
        var stopCount = await verifyDb.TripStops.CountAsync(s => s.SubscriptionDayId == day.Id);
        stopCount.Should().Be(1, "Zero duplicate TripStops allowed in database");

        _output.WriteLine("[PASS] Concurrent single vs bulk assignment handled safely (1 Success, 1 Rejection, 1 DB Stop).");
    }

    [Fact]
    public async Task FailurePath_RotatedBarcode_AssignmentPersistsSavedValue()
    {
        _output.WriteLine("--- Testing Failure Path: Rotated Barcode Persists Saved Canonical Value ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        // Old revoked barcode
        var oldBarcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-OLD-BARCODE-001",
            Status = MealBarcodeStatus.Revoked,
            IsRevoked = true,
            GeneratedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        // New active rotated barcode
        var newBarcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-ROTATED-CANONICAL-999",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.AddRange(oldBarcode, newBarcode);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        var assignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
            new { driverId = fixture.DriverProfileA.Id });
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = _fixture.CreateDbContext();
        var stop = await verifyDb.TripStops.SingleAsync(s => s.SubscriptionDayId == day.Id);
        stop.OrderCode.Should().Be("MM-ROTATED-CANONICAL-999", "Assignment must bind to the active rotated barcode, not the revoked or ad-hoc value");

        _output.WriteLine("[PASS] Rotated barcode correctly resolved to persisted canonical value.");
    }

    [Fact]
    public async Task FailurePath_AssignmentFailure_RollsBack_NoPushAndNoOutbox()
    {
        _output.WriteLine("--- Testing Failure Path: Assignment Failure Rolls Back (No Push, No Outbox) ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(8));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-FAIL-ROLLBACK-001",
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcode);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        var nonExistentDriverId = Guid.NewGuid();
        var assignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
            new { driverId = nonExistentDriverId });

        assignResp.StatusCode.Should().Be(HttpStatusCode.NotFound, "Non-existent driver assignment must fail");

        // Verify no TripStop and no Outbox message were saved
        await using var verifyDb = _fixture.CreateDbContext();
        var stopCount = await verifyDb.TripStops.CountAsync(s => s.SubscriptionDayId == day.Id);
        stopCount.Should().Be(0);

        var outboxCount = await verifyDb.OutboxMessages.CountAsync(m => m.PayloadJson.Contains(day.Id.ToString()));
        outboxCount.Should().Be(0);

        _fixture.PushNotifier.BoxAssignments.Should().NotContain(a => a.BoxCode == "MM-FAIL-ROLLBACK-001");

        _output.WriteLine("[PASS] Assignment failure rolled back cleanly: zero stops, zero outbox, zero push.");
    }

    [Fact]
    public async Task FailurePath_PushFailureAfterCommit_AssignmentRemainsAndOutboxRetries()
    {
        _output.WriteLine("--- Testing Failure Path: Push Failure After Commit Leaves Assignment Intact & Retries Outbox ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing
        };
        db.SubscriptionDays.Add(day);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-RETRY-TEST-" + Guid.NewGuid().ToString("N")[..6],
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcode);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        // 1. Assign box while PushNotifier is configured to throw on push
        _fixture.PushNotifier.ShouldThrowOnPush = true;
        try
        {
            var assignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
                new { driverId = fixture.DriverProfileA.Id });
            assignResp.StatusCode.Should().Be(HttpStatusCode.OK, "Commit to DB must succeed before Outbox worker runs push");

            // 2. Run Outbox worker: push throws exception
            var scopeFactory = _fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>();
            var outboxJob = new OutboxBackgroundJob(scopeFactory);
            await outboxJob.ProcessPendingMessagesAsync(10, CancellationToken.None);

            // 3. Verify: Assignment in DB is STILL COMMITTED
            await using (var verifyDb = _fixture.CreateDbContext())
            {
                var stop = await verifyDb.TripStops.FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id);
                stop.Should().NotBeNull("Assignment in DB must remain committed despite push notifier failure");

                var outbox = await verifyDb.OutboxMessages
                    .FirstOrDefaultAsync(m => m.EventType == "DriverBoxAssigned" && m.PayloadJson.Contains(day.Id.ToString()));
                outbox.Should().NotBeNull();
                outbox!.ProcessedAtUtc.Should().BeNull("Message must not be marked processed after failure");
                outbox.RetryCount.Should().Be(1, "RetryCount must increment on failure");
                outbox.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow.AddSeconds(-5), "Next attempt must be scheduled");
                outbox.LockedByWorkerId.Should().BeNull("Lease lock must be cleared on failure");

                // Reset retry time to now so we can test retry recovery
                outbox.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
                await verifyDb.SaveChangesAsync();
            }

            // 4. Recover push notifier and run Outbox worker again
            _fixture.PushNotifier.ShouldThrowOnPush = false;
            await outboxJob.ProcessPendingMessagesAsync(10, CancellationToken.None);

            // 5. Verify: Outbox now processed and push notification delivered
            await using (var verifyDb = _fixture.CreateDbContext())
            {
                var outbox = await verifyDb.OutboxMessages
                    .SingleAsync(m => m.EventType == "DriverBoxAssigned" && m.PayloadJson.Contains(day.Id.ToString()));
                outbox.ProcessedAtUtc.Should().NotBeNull("Message must now be processed successfully on retry");
            }

            _fixture.PushNotifier.BoxAssignments.Should().Contain(a => a.BoxCode == barcode.BarcodeValue);
            _output.WriteLine("[PASS] Push failure handled according to Outbox retry policy; assignment intact.");
        }
        finally
        {
            _fixture.PushNotifier.ShouldThrowOnPush = false;
        }
    }

    [Fact]
    public async Task ExplicitConflict_BulkAssignment_AlreadyAssignedBox_Returns409Conflict_AndDayStatusVerified()
    {
        _output.WriteLine("--- Testing Explicit 409 Conflict on Bulk Assignment of Already-Assigned Box ---");
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        await using var db = _fixture.CreateDbContext();
        var fixture = await OrderLifecycleTestDataSeeder.SeedAsync(db, startDate);

        var day = new SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            RestaurantId = fixture.RestaurantA.Id,
            ServiceDate = startDate,
            Status = SubscriptionDayStatus.Preparing,
            DeliveryAddressLine = "حولي - قطعة 4"
        };
        db.SubscriptionDays.Add(day);

        var barcode = new MealBarcode
        {
            SubscriptionDayId = day.Id,
            BarcodeValue = "MM-BULK-409-" + Guid.NewGuid().ToString("N")[..6],
            Status = MealBarcodeStatus.Generated,
            GeneratedAtUtc = DateTime.UtcNow
        };
        db.MealBarcodes.Add(barcode);

        var order = new RestaurantDailyOrder
        {
            RestaurantId = fixture.RestaurantA.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = startDate,
            Status = OrderStatus.ReadyForPickup,
            ReadyForPickupAtUtc = DateTime.UtcNow,
            BoxCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.RestaurantDailyOrders.Add(order);
        await db.SaveChangesAsync();

        var dispatcherClient = _fixture.CreateClientForUser(new TokenUser(
            fixture.DispatcherA.Id, UserType.DeliveryManager, ["Dispatcher"], [], fixture.RestaurantA.Id));

        // Step 1: Assign box initially to Driver A
        var initialAssignResp = await dispatcherClient.PostAsJsonAsync($"/api/v1/dispatcher/orders/{day.Id}/assign",
            new { driverId = fixture.DriverProfileA.Id, notes = "Initial assignment" });
        initialAssignResp.StatusCode.Should().Be(HttpStatusCode.OK, "Initial assignment to Driver A must succeed");

        // Step 2: Verify Day and Order status
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var dailyOrder = await verifyDb.RestaurantDailyOrders.SingleAsync(o => o.SubscriptionDayId == day.Id);
            dailyOrder.Status.Should().Be(OrderStatus.AssignedToDriver, "Daily order must transition to AssignedToDriver");

            var stop = await verifyDb.TripStops.SingleAsync(s => s.SubscriptionDayId == day.Id);
            stop.Status.Should().Be(TripStopStatus.Pending);
            stop.OrderCode.Should().Be(barcode.BarcodeValue);
        }

        // Step 3: Attempt bulk assignment containing the same box to Driver B (should fail with 409 Conflict)
        var bulkAssignReq = new
        {
            boxIds = new[] { day.Id },
            driverId = fixture.DriverProfileB.Id,
            notes = "Conflicting bulk assignment attempt"
        };
        var conflictResp = await dispatcherClient.PostAsJsonAsync("/api/v1/dispatcher/orders/bulk-assign", bulkAssignReq);
        var errBody = await conflictResp.Content.ReadAsStringAsync();
        _output.WriteLine($"[CONFLICT RESPONSE BODY] {errBody}");

        // Step 4: Explicitly assert 409 Conflict
        conflictResp.StatusCode.Should().Be(HttpStatusCode.Conflict, $"Assigning an already-assigned box in bulk must return 409 Conflict. Response: {errBody}");
        errBody.Should().Contain("Box.AlreadyAssigned");

        // Step 5: Verify DB integrity: Stop is still assigned to Driver A, Driver B has zero stops
        await using (var verifyDb = _fixture.CreateDbContext())
        {
            var stops = await verifyDb.TripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .Where(s => s.SubscriptionDayId == day.Id)
                .ToListAsync();

            stops.Should().HaveCount(1, "Exactly one TripStop must exist for this box in database");
            var activeAssignment = stops[0].Trip.DriverAssignments.FirstOrDefault(da => da.Status == "Active");
            activeAssignment.Should().NotBeNull();
            activeAssignment!.DriverProfileId.Should().Be(fixture.DriverProfileA.Id, "Box must remain assigned to Driver A");
        }

        _output.WriteLine("[PASS] Bulk assignment conflict correctly returned 409 Conflict with Box.AlreadyAssigned code.");
    }

    private static string? PaymentIdFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["paymentId"] ?? uri.Segments.LastOrDefault()?.Trim('/');
        }
        catch
        {
            return null;
        }
    }
}
