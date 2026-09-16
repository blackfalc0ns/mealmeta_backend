using FluentAssertions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CheckoutSessionStateMachineTests
{
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void HappyPath_FullLifecycle_TransitionsCorrectly()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = _now.AddMinutes(15)
        };

        // 1. Pending -> PaymentPending
        var pResult = session.MarkPaymentPending();
        pResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.PaymentPending);

        // 2. PaymentPending -> Paid
        var paidResult = session.MarkPaid();
        paidResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);

        // 3. Paid -> Committing
        var comResult = session.MarkCommitting();
        comResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Committing);

        // 4. Committing -> Completed
        var doneResult = session.MarkCompleted();
        doneResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Completed);
    }

    [Fact]
    public void MarkExpired_WhenActiveLeaseExists_FailsAndProtectsSession()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = _now.AddMinutes(-5) // Expired TTL
        };

        // Acquire lease for 2 minutes
        session.AcquireProcessingLease(TimeSpan.FromMinutes(2), _now);

        // Try expiring during active lease
        var expireResult = session.MarkExpired(_now.AddMinutes(1));
        expireResult.IsFailure.Should().BeTrue();
        expireResult.Error.Should().Contain("processing lease is active");
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Pending);

        // Expire after lease expires
        var expireAfterResult = session.MarkExpired(_now.AddMinutes(3));
        expireAfterResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Expired);
    }

    [Fact]
    public void MarkExpired_WhenAlreadyPaidOrCompleted_Fails()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.Paid,
            ExpiresAtUtc = _now.AddMinutes(-5)
        };

        var result = session.MarkExpired(_now);
        result.IsFailure.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);
    }

    [Fact]
    public void ProcessingLease_AcquisitionAndRelease_EnforcesConcurrencyGuard()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.Pending
        };

        // Acquire lease
        var r1 = session.AcquireProcessingLease(TimeSpan.FromMinutes(5), _now);
        r1.IsSuccess.Should().BeTrue();
        session.ProcessingLeaseExpiresAtUtc.Should().Be(_now.AddMinutes(5));

        // Overlapping acquisition before expiry fails
        var r2 = session.AcquireProcessingLease(TimeSpan.FromMinutes(5), _now.AddMinutes(2));
        r2.IsFailure.Should().BeTrue();
        r2.Error.Should().Contain("Active processing lease already exists");

        // Release lease explicitly
        session.ReleaseProcessingLease();
        session.ProcessingLeaseExpiresAtUtc.Should().BeNull();

        // Now acquisition succeeds
        var r3 = session.AcquireProcessingLease(TimeSpan.FromMinutes(5), _now.AddMinutes(2));
        r3.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RefundWorkflow_FromPaidOrExpired_TransitionsCorrectly()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.Paid
        };

        var refPending = session.MarkRefundPending();
        refPending.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.RefundPending);

        var refunded = session.MarkRefunded();
        refunded.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Refunded);

        // Cannot mark refunded again
        var doubleRefund = session.MarkRefunded();
        doubleRefund.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PaymentFailed_TransitionsFromPendingOrPaymentPending()
    {
        var session = new CampaignCheckoutSession
        {
            Status = CampaignCheckoutSessionStatus.PaymentPending
        };

        var failResult = session.MarkPaymentFailed();
        failResult.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(CampaignCheckoutSessionStatus.PaymentFailed);

        // Cannot mark paid after payment failed
        var paidResult = session.MarkPaid();
        paidResult.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void PaymentTransactionTracking_CanBeAssociatedWithSession()
    {
        var session = new CampaignCheckoutSession(Guid.NewGuid())
        {
            CampaignId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending
        };

        var tx = new CampaignPaymentTransaction
        {
            CheckoutSessionId = session.Id,
            CampaignId = session.CampaignId,
            PaymentProvider = "Hesabe",
            ProviderTransactionId = "hesabe-tx-999",
            ProviderPaymentReference = "ref-12345",
            Amount = 45.000m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Initiated,
            InitiatedAtUtc = _now
        };

        session.PaymentTransactions.Add(tx);
        session.PaymentTransactions.Should().HaveCount(1);
        session.PaymentTransactions.First().Status.Should().Be(PaymentTransactionStatus.Initiated);

        // Update tx to succeeded
        tx.Status = PaymentTransactionStatus.Succeeded;
        tx.CompletedAtUtc = _now.AddSeconds(30);

        session.PaymentTransactions.First().Status.Should().Be(PaymentTransactionStatus.Succeeded);
        session.PaymentTransactions.First().CompletedAtUtc.Should().Be(_now.AddSeconds(30));
    }
}
