using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Finance.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/finance/ledger")]
public sealed class FinanceLedgerController(
    LedgerQueries queries,
    IJournalSourceService sources,
    JournalReversalService reversals,
    IPeriodLifecycleService periods,
    IPostingService posting,
    ITrialBalanceQuery trialBalance) : ApiControllerBase
{
    [HttpGet("entities")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Entities(CancellationToken cancellationToken) => HandleResult(await queries.ListEntitiesAsync(cancellationToken));

    [HttpGet("entities/{entityId:guid}/accounts")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Accounts(Guid entityId, [FromQuery] int take = 500, CancellationToken cancellationToken = default)
        => HandleResult(await queries.ListAccountsAsync(entityId, take, cancellationToken));

    [HttpPost("accounts")]
    [HasPermission(PermissionCodes.LedgerManage)]
    public async Task<IActionResult> CreateAccount([FromBody] LedgerCreateAccountRequest request, CancellationToken cancellationToken)
        => HandleResult(await queries.CreateAccountAsync(request, cancellationToken));

    [HttpGet("entities/{entityId:guid}/journals")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Journals(Guid entityId, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
        => HandleResult(await queries.ListJournalsAsync(entityId, take, cancellationToken));

    [HttpGet("journals/{id:guid}")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Journal(Guid id, CancellationToken cancellationToken)
        => HandleResult(await queries.GetJournalAsync(id, cancellationToken));

    [HttpGet("entities/{entityId:guid}/periods")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Periods(Guid entityId, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
        => HandleResult(await queries.ListPeriodsAsync(entityId, take, cancellationToken));

    [HttpPost("periods")]
    [HasPermission(PermissionCodes.LedgerPeriodsManage)]
    public async Task<IActionResult> CreatePeriod([FromBody] LedgerCreatePeriodRequest request, CancellationToken cancellationToken)
        => HandleResult(await periods.CreateAsync(request, cancellationToken));

    [HttpPost("periods/{id:guid}/request-close")]
    [HasPermission(PermissionCodes.LedgerPeriodsManage)]
    public async Task<IActionResult> RequestClose(Guid id, [FromBody] LedgerPeriodCommandRequest request, CancellationToken cancellationToken)
        => HandleResult(await periods.RequestCloseAsync(id, request, cancellationToken));

    [HttpPost("periods/{id:guid}/cancel-close")]
    [HasPermission(PermissionCodes.LedgerPeriodsManage)]
    public async Task<IActionResult> CancelClose(Guid id, [FromBody] LedgerPeriodCommandRequest request, CancellationToken cancellationToken)
        => HandleResult(await periods.CancelCloseAsync(id, request, cancellationToken));

    [HttpPost("periods/{id:guid}/close")]
    [HasPermission(PermissionCodes.LedgerPeriodsManage)]
    public async Task<IActionResult> Close(Guid id, [FromBody] LedgerPeriodCommandRequest request, CancellationToken cancellationToken)
        => HandleResult(await periods.CloseAsync(id, request, cancellationToken));

    [HttpGet("entities/{entityId:guid}/sources")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Sources(Guid entityId, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
        => HandleResult(await sources.ListAsync(entityId, take, cancellationToken));

    [HttpGet("sources/{id:guid}")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> Source(Guid id, CancellationToken cancellationToken)
        => HandleResult(await sources.GetAsync(id, cancellationToken));

    [HttpPost("sources")]
    [HasPermission(PermissionCodes.LedgerManage)]
    public async Task<IActionResult> CreateSource([FromBody] LedgerSourceDraftRequest request, CancellationToken cancellationToken)
    {
        var result = await sources.CreateDraftAsync(request, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Source), new { id = result.Value.Id }, result.Value) : HandleResult(result);
    }

    [HttpPost("journals/{journalId:guid}/reversal-drafts")]
    [HasPermission(PermissionCodes.LedgerManage)]
    public async Task<IActionResult> CreateReversalDraft(Guid journalId, [FromBody] LedgerReversalDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await reversals.CreateDraftAsync(journalId, request.ReversalDate, request.Reason,
            request.EvidenceReference, request.LegalEntityId, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Source), new { id = result.Value.Id }, result.Value) : HandleResult(result);
    }

    [HttpPost("sources/{id:guid}/submit")]
    [HasPermission(PermissionCodes.LedgerManage)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] LedgerSourceSubmitRequest request, CancellationToken cancellationToken)
        => HandleResult(await sources.SubmitAsync(id, request, cancellationToken));

    [HttpPost("sources/{id:guid}/approve")]
    [HasPermission(PermissionCodes.LedgerApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] LedgerSourceDecisionRequest request, CancellationToken cancellationToken)
        => HandleResult(await sources.ApproveAsync(id, request, cancellationToken));

    [HttpPost("sources/{id:guid}/reject")]
    [HasPermission(PermissionCodes.LedgerApprove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] LedgerSourceDecisionRequest request, CancellationToken cancellationToken)
        => HandleResult(await sources.RejectAsync(id, request, cancellationToken));

    [HttpPost("sources/{id:guid}/process-approved")]
    [HasPermission(PermissionCodes.LedgerManage)]
    public async Task<IActionResult> ProcessApproved(Guid id, CancellationToken cancellationToken)
        => HandleResult(await posting.PostApprovedSourceAsync(id, cancellationToken));

    [HttpGet("trial-balance")]
    [HasPermission(PermissionCodes.LedgerView)]
    public async Task<IActionResult> TrialBalance([FromQuery] Guid legalEntityId, [FromQuery] Guid periodId,
        [FromQuery] DateOnly asOfDate, [FromQuery] int skip = 0, [FromQuery] int take = 500, CancellationToken cancellationToken = default)
        => HandleResult(await trialBalance.GetAsync(legalEntityId, periodId, asOfDate, skip, take, cancellationToken));
}

public sealed record LedgerReversalDraftRequest(Guid LegalEntityId, DateOnly ReversalDate, string Reason, string EvidenceReference);
