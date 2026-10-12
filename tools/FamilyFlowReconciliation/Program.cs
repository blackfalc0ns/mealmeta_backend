using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FamilyFlowReconciliation;

public sealed record Finding(string Code, Guid GroupId, Guid? RecordId, string Evidence);
public sealed record ReconciliationReport(string Environment, DateTime GeneratedAtUtc, DateOnly BusinessDate, int Groups,
    int ActiveGroups, int Members, int SelectedUnlinked, int PaidBatches, int PaidBatchesNeedingReview,
    int ActiveMembersWithPointerMismatch, int PaidReadyWithoutDays, int PaidSubscriptionsMissingBatchEvidence, int SuspiciousOpenConfirmations,
    int UnknownPaidPayerOrNet, int GrossRefundExceedsProvenNet, int ProfilePurchaseAddressDifferences,
    int IncompleteFrozenAddressSnapshots, int CancellationReviews,
    bool ReadOnlyEnforced, IReadOnlyList<Finding> Findings,
    string ReportFingerprint);

public static class ReconciliationRunner
{
    private const int PageSize = 250;
    private const int FindingLimit = 200;

    public static async Task<ReconciliationReport> RunAsync(string connectionString, string environment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("An explicit connection is required.", nameof(connectionString));
        if (string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("An explicit environment name is required.", nameof(environment));
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        await using var modeCommand = db.Database.GetDbConnection().CreateCommand();
        modeCommand.Transaction = transaction.GetDbTransaction();
        modeCommand.CommandText = "SHOW transaction_read_only";
        var readOnlyEnforced = string.Equals(Convert.ToString(await modeCommand.ExecuteScalarAsync(cancellationToken)),
            "on", StringComparison.OrdinalIgnoreCase);
        if (!readOnlyEnforced) throw new InvalidOperationException("PostgreSQL did not enforce a read-only transaction.");

        var asOfUtc = DateTime.UtcNow;
        var businessDate = BusinessCalendarDate.KuwaitToday(asOfUtc);
        var groups = await db.FamilyGroups.AsNoTracking().CountAsync(cancellationToken);
        var activeGroups = await db.FamilyGroups.AsNoTracking().CountAsync(x => x.Status == FamilyGroupStatus.Active, cancellationToken);
        var memberCount = await db.FamilyMembers.AsNoTracking().CountAsync(x => !x.IsDeleted, cancellationToken);
        var paidBatches = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.Status == FamilyCheckoutBatchStatus.Paid).CountAsync(cancellationToken);
        var paidBatchesNeedingReview = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.Status == FamilyCheckoutBatchStatus.Paid && (!x.PaidAtUtc.HasValue
                || x.PayerCustomerUserId == Guid.Empty || x.GatewayPaymentId == null || x.GatewayInvoiceId == null
                || !db.FamilyCheckoutPaymentAttempts.Any(a => a.BatchId == x.Id && a.ResultCode == "Paid" && a.VerifiedAtUtc.HasValue
                    && a.GatewayPaymentId == x.GatewayPaymentId && a.GatewayInvoiceId == x.GatewayInvoiceId)
                || !db.PaymentWebhookReceipts.Any(r => r.VerifiedAtUtc.HasValue && r.PaymentId == x.GatewayPaymentId
                    && r.InvoiceId == x.GatewayInvoiceId && r.Amount == x.FinalPrice && r.CurrencyCode == x.CurrencyCode
                    && (r.Status == "PaymentVerified" || r.Status == "FulfillmentCompleted"
                        || r.Status == "FulfillmentRetryPending"))
                || !db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id)
                || db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id
                    && (line.NetPaidAmount < 0m || line.NetPaidAmount != line.PriceBeforeFamilyDiscount - line.FamilyDiscountAllocated
                        || line.CurrencyCode != x.CurrencyCode))
                || db.FamilyCheckoutBatchLines.Where(line => line.BatchId == x.Id).Sum(line => line.NetPaidAmount) != x.FinalPrice))
            .CountAsync(cancellationToken);
        var unknownPaidPayerOrNetQuery = db.FamilyCheckoutBatches.AsNoTracking().Where(x => x.Status == FamilyCheckoutBatchStatus.Paid
            && (x.PayerCustomerUserId == Guid.Empty || !db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id)
                || db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id
                    && (line.NetPaidAmount < 0m || line.NetPaidAmount != line.PriceBeforeFamilyDiscount - line.FamilyDiscountAllocated
                        || line.CurrencyCode != x.CurrencyCode))
                || db.FamilyCheckoutBatchLines.Where(line => line.BatchId == x.Id).Sum(line => line.NetPaidAmount) != x.FinalPrice));
        var unknownPaidPayerOrNet = await unknownPaidPayerOrNetQuery.CountAsync(cancellationToken);
        var unknownPaidPayerOrNetFindings = await unknownPaidPayerOrNetQuery.OrderBy(x => x.FamilyGroupId).ThenBy(x => x.Id)
            .Take(FindingLimit).Select(x => new Finding("paid_batch_unknown_payer_or_net", x.FamilyGroupId, x.Id,
                "missing-payer-or-allocation-does-not-conserve-recorded-batch-net")).ToListAsync(cancellationToken);
        var paidReadyWithoutDays = await db.FamilyMembers.AsNoTracking().CountAsync(member => !member.IsDeleted
            && member.SetupStatus == FamilyMemberSetupStatus.Ready
            && db.Subscriptions.Any(subscription => subscription.FamilyMemberId == member.Id
                && subscription.FamilyGroupId == member.FamilyGroupId
                && (subscription.PaymentVerifiedAtUtc.HasValue || db.FamilyCheckoutBatchLines.Any(line =>
                    line.SubscriptionId == subscription.Id && line.Batch.Status == FamilyCheckoutBatchStatus.Paid))
                && (subscription.Status == SubscriptionStatus.Paid || subscription.Status == SubscriptionStatus.Active
                    || subscription.Status == SubscriptionStatus.Scheduled || subscription.Status == SubscriptionStatus.SuspendedForReview)
                && !db.SubscriptionDays.Any(day => day.SubscriptionId == subscription.Id)), cancellationToken);
        var paidSubscriptionsMissingBatchEvidenceQuery = db.Subscriptions.AsNoTracking().Where(subscription => subscription.FamilyGroupId.HasValue
            && (subscription.Status == SubscriptionStatus.Paid || subscription.Status == SubscriptionStatus.Active
                || subscription.Status == SubscriptionStatus.Scheduled || subscription.Status == SubscriptionStatus.SuspendedForReview)
            && !db.FamilyCheckoutBatchLines.Any(line => line.SubscriptionId == subscription.Id
                && line.Batch.FamilyGroupId == subscription.FamilyGroupId && line.Batch.Status == FamilyCheckoutBatchStatus.Paid
                && line.Batch.PaidAtUtc.HasValue && line.Batch.PayerCustomerUserId != Guid.Empty
                && !string.IsNullOrWhiteSpace(line.Batch.GatewayPaymentId) && !string.IsNullOrWhiteSpace(line.Batch.GatewayInvoiceId)
                && line.Batch.PaymentAttempts.Any(attempt => attempt.ResultCode == "Paid" && attempt.VerifiedAtUtc.HasValue
                    && attempt.GatewayPaymentId == line.Batch.GatewayPaymentId && attempt.GatewayInvoiceId == line.Batch.GatewayInvoiceId)
                && db.PaymentWebhookReceipts.Any(receipt => receipt.VerifiedAtUtc.HasValue
                    && receipt.PaymentId == line.Batch.GatewayPaymentId && receipt.InvoiceId == line.Batch.GatewayInvoiceId
                    && receipt.Amount == line.Batch.FinalPrice && receipt.CurrencyCode == line.Batch.CurrencyCode
                    && (receipt.Status == "PaymentVerified" || receipt.Status == "FulfillmentCompleted"
                        || receipt.Status == "FulfillmentRetryPending"))
                && !db.FamilyCheckoutBatchLines.Any(allocation => allocation.BatchId == line.BatchId
                    && (allocation.NetPaidAmount < 0m || allocation.NetPaidAmount != allocation.PriceBeforeFamilyDiscount - allocation.FamilyDiscountAllocated
                        || allocation.CurrencyCode != line.Batch.CurrencyCode))
                && db.FamilyCheckoutBatchLines.Where(allocation => allocation.BatchId == line.BatchId)
                    .Sum(allocation => allocation.NetPaidAmount) == line.Batch.FinalPrice));
        var paidSubscriptionsMissingBatchEvidence = await paidSubscriptionsMissingBatchEvidenceQuery.CountAsync(cancellationToken);
        var missingBatchEvidenceFindings = await paidSubscriptionsMissingBatchEvidenceQuery.OrderBy(x => x.FamilyGroupId).ThenBy(x => x.Id)
            .Take(FindingLimit).Select(x => new Finding("family_paid_subscription_missing_immutable_batch_evidence", x.FamilyGroupId!.Value,
                x.Id, "payment-verified-or-paid-state-without-coherent-batch-payer-attempt-receipt-and-net-allocation"))
            .ToListAsync(cancellationToken);
        var suspiciousOpenConfirmations = await db.SubscriptionDays.AsNoTracking().CountAsync(day => day.ServiceDate > businessDate
            && day.Status == SubscriptionDayStatus.Open && day.ConfirmedAtUtc.HasValue
            && !db.RestaurantDailyOrders.Any(order => order.SubscriptionDayId == day.Id)
            && !db.MealInvoices.Any(invoice => invoice.SubscriptionDayId == day.Id)
            && !db.RestaurantConfirmations.Any(confirmation => db.RestaurantDailyOrders.Any(order =>
                order.Id == confirmation.DailyOrderId && order.SubscriptionDayId == day.Id))
            && !db.DailyMealSelections.Any(selection => selection.SubscriptionDayId == day.Id)
            && !db.DailyRestaurantSelections.Any(selection => selection.SubscriptionDayId == day.Id), cancellationToken);
        var selectedUnlinkedQuery = from line in db.FamilyCheckoutBatchLines.AsNoTracking()
            join member in db.FamilyMembers.AsNoTracking() on line.FamilyMemberId equals member.Id
            join familyGroup in db.FamilyGroups.AsNoTracking() on member.FamilyGroupId equals familyGroup.Id
            where line.CheckoutSelected && (line.Batch.Status == FamilyCheckoutBatchStatus.PaymentPending
                    || line.Batch.Status == FamilyCheckoutBatchStatus.ReviewRequired)
                && member.Role != FamilyMemberRole.Manager && !member.IsDeleted
                && !db.FamilyMemberAccesses.Any(access => access.FamilyMemberId == member.Id
                    && access.LinkedCustomerUserId.HasValue && access.RemovedAtUtc == null)
            select new { GroupId = familyGroup.Id, RecordId = line.Id };
        var selectedUnlinkedCount = await selectedUnlinkedQuery.CountAsync(cancellationToken);
        var selectedUnlinked = await selectedUnlinkedQuery
            .OrderBy(x => x.GroupId).ThenBy(x => x.RecordId).Take(FindingLimit)
            .Select(x => new Finding("selected_member_account_missing", x.GroupId, x.RecordId, "selected-and-unlinked"))
            .ToListAsync(cancellationToken);
        var badBatchFindings = await db.FamilyCheckoutBatches.AsNoTracking()
            .Where(x => x.Status == FamilyCheckoutBatchStatus.Paid && (!x.PaidAtUtc.HasValue
                || x.PayerCustomerUserId == Guid.Empty || x.GatewayPaymentId == null || x.GatewayInvoiceId == null
                || !db.FamilyCheckoutPaymentAttempts.Any(a => a.BatchId == x.Id && a.ResultCode == "Paid" && a.VerifiedAtUtc.HasValue
                    && a.GatewayPaymentId == x.GatewayPaymentId && a.GatewayInvoiceId == x.GatewayInvoiceId)
                || !db.PaymentWebhookReceipts.Any(r => r.VerifiedAtUtc.HasValue && r.PaymentId == x.GatewayPaymentId
                    && r.InvoiceId == x.GatewayInvoiceId && r.Amount == x.FinalPrice && r.CurrencyCode == x.CurrencyCode
                    && (r.Status == "PaymentVerified" || r.Status == "FulfillmentCompleted"
                        || r.Status == "FulfillmentRetryPending"))
                || !db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id)
                || db.FamilyCheckoutBatchLines.Any(line => line.BatchId == x.Id
                    && (line.NetPaidAmount < 0m || line.NetPaidAmount != line.PriceBeforeFamilyDiscount - line.FamilyDiscountAllocated
                        || line.CurrencyCode != x.CurrencyCode))
                || db.FamilyCheckoutBatchLines.Where(line => line.BatchId == x.Id).Sum(line => line.NetPaidAmount) != x.FinalPrice))
            .OrderBy(x => x.FamilyGroupId).ThenBy(x => x.Id).Take(FindingLimit)
            .Select(x => new Finding("paid_batch_needs_review", x.FamilyGroupId, x.Id, "paid-evidence-incomplete-or-contradictory"))
            .ToListAsync(cancellationToken);

        var pointerFindings = new List<Finding>();
        var pointerMismatchCount = 0;
        for (var offset = 0; ; offset += PageSize)
        {
            var page = await db.FamilyMembers.AsNoTracking().Where(x => !x.IsDeleted
                    ).OrderBy(x => x.Id).Skip(offset).Take(PageSize)
                .Select(x => new { x.Id, x.FamilyGroupId, x.MemberSubscriptionId }).ToListAsync(cancellationToken);
            if (page.Count == 0) break;
            var pageIds = page.Select(x => x.Id).ToArray();
            var accessRows = await db.FamilyMemberAccesses.AsNoTracking().Where(x => pageIds.Contains(x.FamilyMemberId))
                .Select(x => new { x.FamilyMemberId, x.RemovedAtUtc }).ToListAsync(cancellationToken);
            var removed = accessRows.Where(x => x.RemovedAtUtc.HasValue).Select(x => x.FamilyMemberId).ToHashSet();
            var active = page.Where(x => !removed.Contains(x.Id)).ToList();
            var pointers = active.ToDictionary(x => x.Id, x => x.MemberSubscriptionId);
            var actual = await new FamilyServiceSubscriptionResolver(db).ResolveAsync(active.Select(x => x.Id).ToArray(),
                pointers, businessDate, cancellationToken);
            foreach (var member in active)
            {
                var projection = actual.GetValueOrDefault(member.Id);
                if (projection is null || member.MemberSubscriptionId == projection.CurrentId || member.MemberSubscriptionId == projection.UpcomingId) continue;
                pointerMismatchCount++;
                if (pointerFindings.Count < FindingLimit)
                    pointerFindings.Add(new Finding("service_pointer_differs_from_resolver", member.FamilyGroupId,
                        member.Id, projection.CurrentId.HasValue ? "current-service-selected-by-resolver" :
                        projection.UpcomingId.HasValue ? "upcoming-service-selected-by-resolver" : "no-current-or-upcoming-service"));
            }
        }
        var paidReadyRows = await (from member in db.FamilyMembers.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking() on member.Id equals subscription.FamilyMemberId
            where !member.IsDeleted && member.SetupStatus == FamilyMemberSetupStatus.Ready
                && subscription.FamilyGroupId == member.FamilyGroupId
                && (subscription.PaymentVerifiedAtUtc.HasValue || db.FamilyCheckoutBatchLines.Any(line =>
                    line.SubscriptionId == subscription.Id && line.Batch.Status == FamilyCheckoutBatchStatus.Paid))
                && (subscription.Status == SubscriptionStatus.Paid || subscription.Status == SubscriptionStatus.Active
                    || subscription.Status == SubscriptionStatus.Scheduled || subscription.Status == SubscriptionStatus.SuspendedForReview)
                && !db.SubscriptionDays.Any(day => day.SubscriptionId == subscription.Id)
            select new { GroupId = member.FamilyGroupId, RecordId = subscription.Id, subscription.Status, subscription.StartDate,
                HasCoherentPaidBatch = db.FamilyCheckoutBatchLines.Any(line => line.SubscriptionId == subscription.Id
                    && line.Batch.FamilyGroupId == subscription.FamilyGroupId && line.Batch.Status == FamilyCheckoutBatchStatus.Paid
                    && line.Batch.PaidAtUtc.HasValue && line.Batch.PayerCustomerUserId != Guid.Empty
                    && !string.IsNullOrWhiteSpace(line.Batch.GatewayPaymentId) && !string.IsNullOrWhiteSpace(line.Batch.GatewayInvoiceId)
                    && line.Batch.PaymentAttempts.Any(attempt => attempt.ResultCode == "Paid" && attempt.VerifiedAtUtc.HasValue
                        && attempt.GatewayPaymentId == line.Batch.GatewayPaymentId && attempt.GatewayInvoiceId == line.Batch.GatewayInvoiceId)
                    && db.PaymentWebhookReceipts.Any(receipt => receipt.VerifiedAtUtc.HasValue
                        && receipt.PaymentId == line.Batch.GatewayPaymentId && receipt.InvoiceId == line.Batch.GatewayInvoiceId
                        && receipt.Amount == line.Batch.FinalPrice && receipt.CurrencyCode == line.Batch.CurrencyCode
                        && (receipt.Status == "PaymentVerified" || receipt.Status == "FulfillmentRetryPending"))
                    && !subscription.FulfillmentCompletedAtUtc.HasValue
                    && !db.FamilyCheckoutBatchLines.Any(allocation => allocation.BatchId == line.BatchId
                        && (allocation.NetPaidAmount < 0m || allocation.NetPaidAmount != allocation.PriceBeforeFamilyDiscount - allocation.FamilyDiscountAllocated
                            || allocation.CurrencyCode != line.Batch.CurrencyCode))
                    && db.FamilyCheckoutBatchLines.Where(allocation => allocation.BatchId == line.BatchId)
                        .Sum(allocation => allocation.NetPaidAmount) == line.Batch.FinalPrice) })
            .OrderBy(x => x.GroupId).ThenBy(x => x.RecordId).Take(FindingLimit)
            .ToListAsync(cancellationToken);
        var paidReadyFindings = paidReadyRows.Select(x => x.StartDate > businessDate
                && x.HasCoherentPaidBatch
                && (x.Status == SubscriptionStatus.Paid || x.Status == SubscriptionStatus.Scheduled)
            ? new Finding("paid_preparing_calendar_not_materialized", x.GroupId, x.RecordId,
                "future-paid-preparing-no-days-yet-informational")
            : new Finding("paid_ready_member_without_service_days", x.GroupId, x.RecordId,
                "current-or-active-paid-service-without-persisted-days-review-required"))
            .ToList();
        var openConfirmationFindings = await (from day in db.SubscriptionDays.AsNoTracking()
            join subscription in db.Subscriptions.AsNoTracking() on day.SubscriptionId equals subscription.Id
            where day.ServiceDate > businessDate
                && day.Status == SubscriptionDayStatus.Open && day.ConfirmedAtUtc.HasValue
                && !db.RestaurantDailyOrders.Any(order => order.SubscriptionDayId == day.Id)
                && !db.MealInvoices.Any(invoice => invoice.SubscriptionDayId == day.Id)
                && !db.RestaurantConfirmations.Any(confirmation => db.RestaurantDailyOrders.Any(order =>
                    order.Id == confirmation.DailyOrderId && order.SubscriptionDayId == day.Id))
                && !db.DailyMealSelections.Any(selection => selection.SubscriptionDayId == day.Id)
                && !db.DailyRestaurantSelections.Any(selection => selection.SubscriptionDayId == day.Id)
            orderby subscription.FamilyGroupId, day.ServiceDate, day.Id
            select new Finding("future_open_day_confirmation_unsubstantiated", subscription.FamilyGroupId ?? Guid.Empty, day.Id,
                "no-dispatch-print-restaurant-or-customer-evidence"))
            .Take(FindingLimit).ToListAsync(cancellationToken);
        var addressDifferences = await (from line in db.FamilyCheckoutBatchLines.AsNoTracking()
            join member in db.FamilyMembers.AsNoTracking() on line.FamilyMemberId equals member.Id
            where line.Batch.Status == FamilyCheckoutBatchStatus.Paid && line.DeliveryAddressId != member.DeliveryAddressId
            select new { GroupId = member.FamilyGroupId, RecordId = line.Id })
            .OrderBy(x => x.GroupId).ThenBy(x => x.RecordId).Take(FindingLimit)
            .Select(x => new Finding("profile_purchase_address_difference", x.GroupId, x.RecordId,
                "immutable-purchase-snapshot-may-differ-from-current-profile"))
            .ToListAsync(cancellationToken);
        var addressDifferenceCount = await (from line in db.FamilyCheckoutBatchLines.AsNoTracking()
            join member in db.FamilyMembers.AsNoTracking() on line.FamilyMemberId equals member.Id
            where line.Batch.Status == FamilyCheckoutBatchStatus.Paid && line.DeliveryAddressId != member.DeliveryAddressId
            select line.Id).CountAsync(cancellationToken);
        var incompleteAddressQuery = db.FamilyCheckoutBatchLines.AsNoTracking().Where(line => line.Batch.Status == FamilyCheckoutBatchStatus.Paid
            && (line.DeliveryAddressId.HasValue && string.IsNullOrWhiteSpace(line.AddressLine)
                || !line.DeliveryAddressId.HasValue && string.IsNullOrWhiteSpace(line.AddressLine)));
        var incompleteAddressCount = await incompleteAddressQuery.CountAsync(cancellationToken);
        var incompleteAddressFindings = await incompleteAddressQuery.OrderBy(x => x.BatchId).ThenBy(x => x.Id).Take(FindingLimit)
            .Select(x => new Finding("paid_line_frozen_address_snapshot_missing", x.Batch.FamilyGroupId, x.Id,
                "purchase-address-snapshot-incomplete-review-required")).ToListAsync(cancellationToken);
        var grossRefundFindings = new List<Finding>();
        var grossRefundExceedsNet = 0;
        for (var offset = 0; ; offset += PageSize)
        {
            var settlements = await db.SubscriptionCancellations.AsNoTracking()
                .Where(x => x.SettlementKind == "FamilyCancellation" && x.ContributionRefundsJson != null
                    && x.RefundCalculations.Any())
                .OrderBy(x => x.Id).Skip(offset).Take(PageSize)
                .Select(x => new { x.Id, x.FamilyGroupId, x.ContributionRefundsJson,
                    FinalNet = x.RefundCalculations.OrderByDescending(y => y.CalculatedAtUtc).ThenBy(y => y.Id)
                        .Select(y => (decimal?)y.FinalCashRefund.Amount).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            if (settlements.Count == 0) break;
            foreach (var settlement in settlements)
            {
                try
                {
                    var beneficiaries = JsonSerializer.Deserialize<List<FamilyRefundBeneficiaryDto>>(settlement.ContributionRefundsJson!);
                    if (beneficiaries is null || settlement.FinalNet is null) continue;
                    var beneficiaryRefunds = beneficiaries.Sum(x => x.RefundAmount);
                    if (beneficiaryRefunds <= settlement.FinalNet.Value) continue;
                    grossRefundExceedsNet++;
                    if (grossRefundFindings.Count < FindingLimit)
                        grossRefundFindings.Add(new Finding("recorded_refunds_exceed_proven_net", settlement.FamilyGroupId ?? Guid.Empty,
                            settlement.Id, "beneficiary-refund-total-exceeds-durable-final-net"));
                }
                catch (JsonException)
                {
                    if (grossRefundFindings.Count < FindingLimit)
                        grossRefundFindings.Add(new Finding("family_cancellation_refund_evidence_unreadable",
                            settlement.FamilyGroupId ?? Guid.Empty, settlement.Id, "malformed-beneficiary-evidence-review-required"));
                }
            }
        }
        var cancellationReviews = await db.SubscriptionCancellations.AsNoTracking()
            .CountAsync(x => x.SettlementKind == "FamilyCancellation" && x.Status == "ReviewRequired", cancellationToken);
        var findings = selectedUnlinked.Concat(badBatchFindings).Concat(missingBatchEvidenceFindings).Concat(pointerFindings)
            .Concat(unknownPaidPayerOrNetFindings).Concat(paidReadyFindings).Concat(openConfirmationFindings)
            .Concat(addressDifferences).Concat(incompleteAddressFindings).Concat(grossRefundFindings)
            .OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.GroupId).ThenBy(x => x.RecordId).ToList();
        var canonical = JsonSerializer.Serialize(new { environment, businessDate, groups, activeGroups, memberCount, selectedUnlinked = selectedUnlinkedCount,
            paidBatches, paidBatchesNeedingReview, pointerMismatchCount, paidReadyWithoutDays, paidSubscriptionsMissingBatchEvidence,
            suspiciousOpenConfirmations,
            unknownPaidPayerOrNet, grossRefundExceedsNet, addressDifferenceCount, incompleteAddressCount, readOnlyEnforced,
            cancellationReviews, findings });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ReconciliationReport(environment, asOfUtc, businessDate, groups, activeGroups, memberCount,
            selectedUnlinkedCount, paidBatches, paidBatchesNeedingReview, pointerMismatchCount, paidReadyWithoutDays,
            paidSubscriptionsMissingBatchEvidence, suspiciousOpenConfirmations, unknownPaidPayerOrNet, grossRefundExceedsNet, addressDifferenceCount,
            incompleteAddressCount, cancellationReviews, readOnlyEnforced,
            findings, fingerprint);
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--environment" || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: FamilyFlowReconciliation --environment <explicit-environment-name>; connection comes only from MEALMATE_DB_CONNECTION.");
            return 2;
        }
        var connection = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            Console.Error.WriteLine("MEALMATE_DB_CONNECTION is required; application settings are never read.");
            return 2;
        }
        var report = await ReconciliationRunner.RunAsync(connection, args[1]);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
