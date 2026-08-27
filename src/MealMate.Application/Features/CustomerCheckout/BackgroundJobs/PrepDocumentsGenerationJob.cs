using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CustomerCheckout.BackgroundJobs;

public sealed class PrepDocumentsGenerationJob(
    IServiceScopeFactory scopes,
    ILogger<PrepDocumentsGenerationJob> logger)
    : ScheduledJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
    protected override string JobName => nameof(PrepDocumentsGenerationJob);

    protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<IAppDbContext>();
        var clock = services.GetRequiredService<IDateTimeProvider>();
        var generator = services.GetRequiredService<IPrepDocumentsGenerator>();
        var now = clock.UtcNow;

        var candidates = await db.SubscriptionDays
            .AsNoTracking()
            .Where(d => (d.Status == SubscriptionDayStatus.ConfirmedByRestaurant
                    || d.Status == SubscriptionDayStatus.Preparing
                    || d.Status == SubscriptionDayStatus.OutForDelivery
                    || d.Status == SubscriptionDayStatus.Delivered)
                && d.RestaurantId != null
                && !db.MealBarcodes.Any(b => b.SubscriptionDayId == d.Id))
            .Select(d => new { d.Id, d.ServiceDate, d.SelectionDeadlineUtc })
            .ToListAsync(cancellationToken);

        var eligible = candidates
            .Where(d => PrepDocumentsGenerationEligibility.IsPrepWindowStarted(d.ServiceDate, d.SelectionDeadlineUtc, now))
            .ToList();

        var generated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var day in eligible)
        {
            var result = await generator.GenerateForDayAsync(day.Id, cancellationToken: cancellationToken);
            if (result.IsSuccess)
            {
                generated++;
                continue;
            }

            if (result.Error.Code == "prep_documents.missing_template")
            {
                skipped++;
                continue;
            }

            failed++;
            logger.LogWarning(
                "Prep document generation failed for day {SubscriptionDayId}: {ErrorCode}",
                day.Id,
                result.Error.Code);
        }

        if (generated > 0 || skipped > 0 || failed > 0)
        {
            logger.LogInformation(
                "Prep documents generation tick: {Generated} generated, {Skipped} skipped (missing templates), {Failed} failed, {PendingWindow} awaiting prep window.",
                generated,
                skipped,
                failed,
                candidates.Count - eligible.Count);
        }
    }
}
