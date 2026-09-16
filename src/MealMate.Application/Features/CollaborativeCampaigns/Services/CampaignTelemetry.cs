using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignTelemetry
{
    void RecordCheckoutCreated(Guid campaignId, Guid sessionId);
    void RecordCheckoutCompleted(Guid campaignId, Guid sessionId, decimal amount, string currency);
    void RecordCheckoutExpired(Guid campaignId, Guid sessionId);
    void RecordReservationDuration(double durationMs, bool success);
    void RecordSettlementPosted(Guid campaignId, Guid restaurantId, decimal amount, string currency);
    Activity? StartCampaignActivity(string name, Guid campaignId, Guid? sessionId = null, Guid? restaurantId = null);
}

public class CampaignTelemetry : ICampaignTelemetry
{
    public const string MeterName = "MealMate.CollaborativeCampaigns";
    public const string ActivitySourceName = "MealMate.CollaborativeCampaigns";
    private const string Version = "2.0.0";

    private static readonly Meter Meter = new(MeterName, Version);
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private readonly Counter<long> _checkoutsCreatedCounter;
    private readonly Counter<long> _checkoutsCompletedCounter;
    private readonly Counter<long> _checkoutsExpiredCounter;
    private readonly Histogram<double> _reservationDurationHistogram;
    private readonly Counter<decimal> _settlementAmountCounter;

    public CampaignTelemetry()
    {
        _checkoutsCreatedCounter = Meter.CreateCounter<long>(
            "mealmate.campaigns.checkouts.created",
            "count",
            "Total campaign checkout sessions created");

        _checkoutsCompletedCounter = Meter.CreateCounter<long>(
            "mealmate.campaigns.checkouts.completed",
            "count",
            "Total campaign checkout sessions completed");

        _checkoutsExpiredCounter = Meter.CreateCounter<long>(
            "mealmate.campaigns.checkouts.expired",
            "count",
            "Total campaign checkout sessions expired");

        _reservationDurationHistogram = Meter.CreateHistogram<double>(
            "mealmate.campaigns.reservations.duration_ms",
            "ms",
            "Duration of campaign capacity reservation coordination");

        _settlementAmountCounter = Meter.CreateCounter<decimal>(
            "mealmate.campaigns.settlement.total_amount",
            "currency_units",
            "Cumulative financial settlement posted to restaurant ledgers");
    }

    public void RecordCheckoutCreated(Guid campaignId, Guid sessionId)
    {
        _checkoutsCreatedCounter.Add(1,
            new KeyValuePair<string, object?>("campaign.id", campaignId.ToString()),
            new KeyValuePair<string, object?>("checkout.session_id", sessionId.ToString()));
    }

    public void RecordCheckoutCompleted(Guid campaignId, Guid sessionId, decimal amount, string currency)
    {
        _checkoutsCompletedCounter.Add(1,
            new KeyValuePair<string, object?>("campaign.id", campaignId.ToString()),
            new KeyValuePair<string, object?>("checkout.session_id", sessionId.ToString()),
            new KeyValuePair<string, object?>("currency", currency));
    }

    public void RecordCheckoutExpired(Guid campaignId, Guid sessionId)
    {
        _checkoutsExpiredCounter.Add(1,
            new KeyValuePair<string, object?>("campaign.id", campaignId.ToString()),
            new KeyValuePair<string, object?>("checkout.session_id", sessionId.ToString()));
    }

    public void RecordReservationDuration(double durationMs, bool success)
    {
        _reservationDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("reservation.success", success));
    }

    public void RecordSettlementPosted(Guid campaignId, Guid restaurantId, decimal amount, string currency)
    {
        _settlementAmountCounter.Add(amount,
            new KeyValuePair<string, object?>("campaign.id", campaignId.ToString()),
            new KeyValuePair<string, object?>("restaurant.id", restaurantId.ToString()),
            new KeyValuePair<string, object?>("currency", currency));
    }

    public Activity? StartCampaignActivity(string name, Guid campaignId, Guid? sessionId = null, Guid? restaurantId = null)
    {
        var activity = ActivitySource.StartActivity(name, ActivityKind.Internal);
        if (activity != null)
        {
            activity.SetTag("campaign.id", campaignId.ToString());
            if (sessionId.HasValue)
            {
                activity.SetTag("checkout.session_id", sessionId.Value.ToString());
            }
            if (restaurantId.HasValue)
            {
                activity.SetTag("restaurant.id", restaurantId.Value.ToString());
            }
        }
        return activity;
    }
}
