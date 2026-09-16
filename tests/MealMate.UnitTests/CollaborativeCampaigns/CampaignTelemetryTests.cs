using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignTelemetryTests : IDisposable
{
    private readonly ICampaignTelemetry _telemetry;
    private readonly MeterListener _meterListener;
    private readonly ActivityListener _activityListener;

    private long _createdCheckouts = 0;
    private long _completedCheckouts = 0;
    private long _expiredCheckouts = 0;
    private double _lastReservationDuration = 0;
    private decimal _totalSettlementRecorded = 0;

    public CampaignTelemetryTests()
    {
        _telemetry = new CampaignTelemetry();

        _meterListener = new MeterListener();
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CampaignTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "mealmate.campaigns.checkouts.created") _createdCheckouts += measurement;
            if (instrument.Name == "mealmate.campaigns.checkouts.completed") _completedCheckouts += measurement;
            if (instrument.Name == "mealmate.campaigns.checkouts.expired") _expiredCheckouts += measurement;
        });

        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "mealmate.campaigns.reservations.duration_ms") _lastReservationDuration = measurement;
        });

        _meterListener.SetMeasurementEventCallback<decimal>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "mealmate.campaigns.settlement.total_amount") _totalSettlementRecorded += measurement;
        });

        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CampaignTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    [Fact]
    public void RecordCheckoutCreated_IncrementsCreatedCheckoutsCounter()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _telemetry.RecordCheckoutCreated(campaignId, sessionId);
        _meterListener.RecordObservableInstruments();

        _createdCheckouts.Should().Be(1);
    }

    [Fact]
    public void RecordCheckoutCompleted_IncrementsCompletedCheckoutsCounter()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _telemetry.RecordCheckoutCompleted(campaignId, sessionId, 95.000m, "KWD");
        _meterListener.RecordObservableInstruments();

        _completedCheckouts.Should().Be(1);
    }

    [Fact]
    public void RecordCheckoutExpired_IncrementsExpiredCheckoutsCounter()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _telemetry.RecordCheckoutExpired(campaignId, sessionId);
        _meterListener.RecordObservableInstruments();

        _expiredCheckouts.Should().Be(1);
    }

    [Fact]
    public void RecordReservationDuration_RecordsHistogram()
    {
        _telemetry.RecordReservationDuration(45.5, success: true);
        _meterListener.RecordObservableInstruments();

        _lastReservationDuration.Should().Be(45.5);
    }

    [Fact]
    public void RecordSettlementPosted_RecordsSettlementAmount()
    {
        var campaignId = Guid.NewGuid();
        var restId = Guid.NewGuid();

        _telemetry.RecordSettlementPosted(campaignId, restId, 150.0m, "KWD");
        _meterListener.RecordObservableInstruments();

        _totalSettlementRecorded.Should().Be(150.0m);
    }

    [Fact]
    public void StartCampaignActivity_CreatesSanitizedSpanWithoutSensitivePaymentData()
    {
        var campaignId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var restId = Guid.NewGuid();

        using var activity = _telemetry.StartCampaignActivity(
            "ProcessCampaignCheckout",
            campaignId,
            sessionId,
            restId);

        activity.Should().NotBeNull();
        activity!.TagObjects.Should().Contain(t => t.Key == "campaign.id" && (string)t.Value! == campaignId.ToString());
        activity.TagObjects.Should().Contain(t => t.Key == "checkout.session_id" && (string)t.Value! == sessionId.ToString());
        activity.TagObjects.Should().Contain(t => t.Key == "restaurant.id" && (string)t.Value! == restId.ToString());

        // Sensitive telemetry assertion: NO payment card details, tokens, secrets
        activity.TagObjects.Should().NotContain(t => t.Key.Contains("card", StringComparison.OrdinalIgnoreCase));
        activity.TagObjects.Should().NotContain(t => t.Key.Contains("cvv", StringComparison.OrdinalIgnoreCase));
        activity.TagObjects.Should().NotContain(t => t.Key.Contains("secret", StringComparison.OrdinalIgnoreCase));
        activity.TagObjects.Should().NotContain(t => t.Key.Contains("password", StringComparison.OrdinalIgnoreCase));
    }
}
