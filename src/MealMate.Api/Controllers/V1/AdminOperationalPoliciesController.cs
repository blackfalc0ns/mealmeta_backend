using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using MealMate.Application.Features.CustomerCalendar;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/operational-policies")]
public sealed class AdminOperationalPoliciesController : ApiControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AdminOperationalPoliciesController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => HandleResult(await _sender.Send(new GetOperationalPoliciesQuery(), ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] OperationalPolicySettings settings, CancellationToken ct)
    {
        var adminId = _currentUser.UserId ?? Guid.Empty;
        return HandleResult(await _sender.Send(new UpdateOperationalPoliciesCommand(settings, adminId), ct));
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PolicyPreviewRequest request, CancellationToken ct)
        => HandleResult(await _sender.Send(new PreviewOperationalPoliciesQuery(
            request.Settings,
            request.SampleServiceDate,
            request.SampleSubscriptionPrice,
            request.SampleTotalDays,
            request.SampleRemainingDays), ct));

    [HttpGet("history")]
    public async Task<IActionResult> History([FromServices] IAppDbContext db, CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking().Where(x => x.Key.StartsWith(OperationalPolicyService.HistoryPrefix))
            .OrderByDescending(x => x.Key).Take(100).Select(x => x.ValueJson).ToListAsync(ct);
        return Ok(rows.Select(x => JsonSerializer.Deserialize<PolicyPublication>(x, OperationalPolicyRules.JsonOptions)));
    }

    public sealed record RestorePolicyRequest(long ExpectedVersion, string Reason);

    [HttpPost("history/{version:long}/restore")]
    public async Task<IActionResult> Restore(long version, [FromBody] RestorePolicyRequest request,
        [FromServices] IAppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { detail = "Restore reason is required." });
        var key = OperationalPolicyService.HistoryPrefix + version.ToString("D20");
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return NotFound();
        var publication = JsonSerializer.Deserialize<PolicyPublication>(row.ValueJson, OperationalPolicyRules.JsonOptions)!;
        return HandleResult(await _sender.Send(new UpdateOperationalPoliciesCommand(
            publication.After with { Version = request.ExpectedVersion, ChangeReason = request.Reason }, _currentUser.UserId ?? Guid.Empty), ct));
    }

    private static OperationalTrialService Trial(IAppDbContext db, IDateTimeProvider clock, IHostEnvironment env,
        IOperationalPolicyService policies, IApplicationTransactionExecutor transactions, IApplicationLockManager locks)
        => new(db, clock, env, policies, transactions, locks);

    [HttpGet("trials/capabilities")]
    public IActionResult TrialCapabilities([FromServices] IHostEnvironment env)
        => Ok(new { Enabled = env.IsDevelopment() || env.IsEnvironment("Test") });

    [HttpGet("trials/{id:guid}/days/{date}")]
    public async Task<IActionResult> TrialState(Guid id, DateOnly date, [FromServices] IAppDbContext db,
        [FromServices] IDateTimeProvider clock, [FromServices] IHostEnvironment env, [FromServices] IOperationalPolicyService policies,
        [FromServices] IApplicationTransactionExecutor transactions, [FromServices] IApplicationLockManager locks, CancellationToken ct)
        => HandleResult(await Trial(db, clock, env, policies, transactions, locks).InspectAsync(id, date, ct));

    [HttpPut("trials/{id:guid}/days/{date}")]
    public async Task<IActionResult> ConfigureTrial(Guid id, DateOnly date, [FromBody] TrialRequest request,
        [FromServices] IAppDbContext db, [FromServices] IDateTimeProvider clock, [FromServices] IHostEnvironment env,
        [FromServices] IOperationalPolicyService policies, [FromServices] IApplicationTransactionExecutor transactions,
        [FromServices] IApplicationLockManager locks, CancellationToken ct)
        => HandleResult(await Trial(db, clock, env, policies, transactions, locks).ConfigureAsync(id, date, request, _currentUser.UserId ?? Guid.Empty, ct));

    public sealed record TrialActionRequest(string Reason);
    [HttpPost("trials/{id:guid}/days/{date}/{action}")]
    public async Task<IActionResult> RunTrial(Guid id, DateOnly date, string action, [FromBody] TrialActionRequest request,
        [FromServices] IAppDbContext db, [FromServices] IDateTimeProvider clock, [FromServices] IHostEnvironment env,
        [FromServices] IOperationalPolicyService policies, [FromServices] IApplicationTransactionExecutor transactions,
        [FromServices] IApplicationLockManager locks, [FromServices] CustomerCalendarEngine engine, CancellationToken ct)
        => HandleResult(await Trial(db, clock, env, policies, transactions, locks).RunAsync(id, date, action, request.Reason,
            _currentUser.UserId ?? Guid.Empty, engine, ct));
}
