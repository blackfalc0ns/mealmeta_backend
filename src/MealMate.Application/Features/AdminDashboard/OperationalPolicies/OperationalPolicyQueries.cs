using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public sealed record PolicySimulationPreviewDto(
    DateOnly SampleServiceDate,
    DateTime CutoffDeadlineUtc,
    DateTime CutoffDeadlineKuwaitTime,
    decimal SampleSubscriptionPrice,
    int SampleTotalDays,
    decimal SampleDailyPrice,
    int SampleRemainingDays,
    decimal GrossRefund,
    decimal CancellationFee,
    decimal NetRefund,
    bool RefundToCashWallet);

public sealed record OperationalPolicyDto(
    CutoffPolicySettings Cutoff,
    CancellationPolicySettings Cancellation,
    FreezePolicySettings Freeze,
    PolicySimulationPreviewDto? Simulation = null);

public sealed record PolicyPreviewRequest(
    OperationalPolicySettings Settings,
    DateOnly? SampleServiceDate = null,
    decimal? SampleSubscriptionPrice = null,
    int? SampleTotalDays = null,
    int? SampleRemainingDays = null);

public static class PolicySimulationHelper
{
    private static readonly TimeZoneInfo KuwaitTimeZone = ResolveKuwaitTimeZone();

    public static PolicySimulationPreviewDto GenerateSimulationPreview(
        OperationalPolicySettings settings,
        IDateTimeProvider clock,
        IOperationalPolicyService policyService,
        DateOnly? sampleServiceDate = null,
        decimal? sampleSubscriptionPrice = null,
        int? sampleTotalDays = null,
        int? sampleRemainingDays = null)
    {
        var serviceDate = sampleServiceDate ?? clock.TodayUtc.AddDays(7);
        var subPrice = sampleSubscriptionPrice ?? 90.000m;
        var totalDays = sampleTotalDays is > 0 ? sampleTotalDays.Value : 30;
        var remainingDays = sampleRemainingDays is >= 0 ? sampleRemainingDays.Value : 20;

        var dailyPrice = totalDays > 0 ? Math.Round(subPrice / totalDays, 3) : 0m;
        var grossRefund = Math.Round(remainingDays * dailyPrice, 3);

        decimal fee = 0m;
        if (grossRefund > 0)
        {
            if (settings.Cancellation.FeeType == CancellationFeeType.Percentage)
            {
                fee = Math.Round(grossRefund * (settings.Cancellation.FeeValue / 100m), 3);
            }
            else if (settings.Cancellation.FeeType == CancellationFeeType.FixedAmount)
            {
                fee = Math.Min(grossRefund, Math.Round(settings.Cancellation.FeeValue, 3));
            }
        }
        var netRefund = Math.Max(0m, grossRefund - fee);

        var deadlineUtc = policyService.CalculateSelectionDeadlineUtc(serviceDate, settings);
        var deadlineKuwait = TimeZoneInfo.ConvertTimeFromUtc(deadlineUtc, KuwaitTimeZone);

        return new PolicySimulationPreviewDto(
            SampleServiceDate: serviceDate,
            CutoffDeadlineUtc: deadlineUtc,
            CutoffDeadlineKuwaitTime: deadlineKuwait,
            SampleSubscriptionPrice: subPrice,
            SampleTotalDays: totalDays,
            SampleDailyPrice: dailyPrice,
            SampleRemainingDays: remainingDays,
            GrossRefund: grossRefund,
            CancellationFee: fee,
            NetRefund: netRefund,
            RefundToCashWallet: settings.Cancellation.RefundToCashWallet);
    }

    private static TimeZoneInfo ResolveKuwaitTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuwait", "Arab Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Kuwait", TimeSpan.FromHours(3), "Kuwait", "Kuwait");
    }
}

public sealed record GetOperationalPoliciesQuery : IQuery<OperationalPolicyDto>;

public sealed class GetOperationalPoliciesQueryHandler : IQueryHandler<GetOperationalPoliciesQuery, OperationalPolicyDto>
{
    private readonly IOperationalPolicyService _policyService;
    private readonly IDateTimeProvider _clock;

    public GetOperationalPoliciesQueryHandler(IOperationalPolicyService policyService, IDateTimeProvider clock)
    {
        _policyService = policyService;
        _clock = clock;
    }

    public async Task<Result<OperationalPolicyDto>> Handle(GetOperationalPoliciesQuery request, CancellationToken ct)
    {
        var settings = await _policyService.GetSettingsAsync(ct);
        var preview = PolicySimulationHelper.GenerateSimulationPreview(settings, _clock, _policyService);
        return Result.Success(new OperationalPolicyDto(settings.Cutoff, settings.Cancellation, settings.Freeze, preview));
    }
}

public sealed record PreviewOperationalPoliciesQuery(
    OperationalPolicySettings Settings,
    DateOnly? SampleServiceDate = null,
    decimal? SampleSubscriptionPrice = null,
    int? SampleTotalDays = null,
    int? SampleRemainingDays = null) : IQuery<PolicySimulationPreviewDto>;

public sealed class PreviewOperationalPoliciesQueryHandler : IQueryHandler<PreviewOperationalPoliciesQuery, PolicySimulationPreviewDto>
{
    private readonly IOperationalPolicyService _policyService;
    private readonly IDateTimeProvider _clock;

    public PreviewOperationalPoliciesQueryHandler(IOperationalPolicyService policyService, IDateTimeProvider clock)
    {
        _policyService = policyService;
        _clock = clock;
    }

    public Task<Result<PolicySimulationPreviewDto>> Handle(PreviewOperationalPoliciesQuery request, CancellationToken ct)
    {
        var preview = PolicySimulationHelper.GenerateSimulationPreview(
            request.Settings,
            _clock,
            _policyService,
            request.SampleServiceDate,
            request.SampleSubscriptionPrice,
            request.SampleTotalDays,
            request.SampleRemainingDays);

        return Task.FromResult(Result.Success(preview));
    }
}
