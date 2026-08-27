using FluentAssertions;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.PrepDocuments;

public class PrepDocumentsGenerationEligibilityTests
{
    private static readonly DateOnly ServiceDate = new(2026, 8, 28);

    [Fact]
    public void IsPrepWindowStarted_uses_selection_deadline_plus_48h()
    {
        var deadline = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        PrepDocumentsGenerationEligibility.IsPrepWindowStarted(
                ServiceDate,
                deadline,
                deadline.AddHours(47))
            .Should().BeFalse();

        PrepDocumentsGenerationEligibility.IsPrepWindowStarted(
                ServiceDate,
                deadline,
                deadline.AddHours(48))
            .Should().BeTrue();
    }

    [Fact]
    public void IsPrepWindowStarted_falls_back_to_service_date_minus_24h()
    {
        var windowStart = ServiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-24);

        PrepDocumentsGenerationEligibility.IsPrepWindowStarted(
                ServiceDate,
                selectionDeadlineUtc: null,
                windowStart.AddMinutes(-1))
            .Should().BeFalse();

        PrepDocumentsGenerationEligibility.IsPrepWindowStarted(
                ServiceDate,
                selectionDeadlineUtc: null,
                windowStart)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.ConfirmedByRestaurant, true)]
    [InlineData(SubscriptionDayStatus.Preparing, true)]
    [InlineData(SubscriptionDayStatus.SentToRestaurant, false)]
    [InlineData(SubscriptionDayStatus.Open, false)]
    public void IsEligibleForGeneration_requires_confirmed_status_and_no_barcode(
        SubscriptionDayStatus status,
        bool expected)
    {
        var now = ServiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        PrepDocumentsGenerationEligibility.IsEligibleForGeneration(
                status,
                ServiceDate,
                selectionDeadlineUtc: null,
                hasBarcode: false,
                now)
            .Should().Be(expected);
    }

    [Fact]
    public void IsEligibleForGeneration_rejects_when_barcode_exists()
    {
        var now = ServiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        PrepDocumentsGenerationEligibility.IsEligibleForGeneration(
                SubscriptionDayStatus.ConfirmedByRestaurant,
                ServiceDate,
                selectionDeadlineUtc: null,
                hasBarcode: true,
                now)
            .Should().BeFalse();
    }

    [Fact]
    public void IsEligibleForGeneration_requires_prep_window()
    {
        var deadline = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var beforeWindow = deadline.AddHours(47);

        PrepDocumentsGenerationEligibility.IsEligibleForGeneration(
                SubscriptionDayStatus.ConfirmedByRestaurant,
                ServiceDate,
                deadline,
                hasBarcode: false,
                beforeWindow)
            .Should().BeFalse();

        PrepDocumentsGenerationEligibility.IsEligibleForGeneration(
                SubscriptionDayStatus.ConfirmedByRestaurant,
                ServiceDate,
                deadline,
                hasBarcode: false,
                deadline.AddHours(48))
            .Should().BeTrue();
    }
}
