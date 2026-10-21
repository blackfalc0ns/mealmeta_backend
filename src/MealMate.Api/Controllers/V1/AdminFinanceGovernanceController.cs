using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/finance/governance")]
public sealed class AdminFinanceGovernanceController : ApiControllerBase
{
    private readonly IFinanceGovernanceQueries _queries;
    private readonly IGovernedFinancialRuleService _rules;
    private readonly IReportingReadinessEvaluator _readiness;

    public AdminFinanceGovernanceController(IFinanceGovernanceQueries queries, IGovernedFinancialRuleService rules, IReportingReadinessEvaluator readiness)
    {
        _queries = queries;
        _rules = rules;
        _readiness = readiness;
    }

    [HttpGet("rules")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> List([FromQuery] Guid? legalEntityId, [FromQuery] RuleApprovalStatus? status, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
        => Ok(await _queries.ListRulesAsync(legalEntityId, status, take, cancellationToken));

    [HttpGet("rules/{id:guid}")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await _queries.GetRuleAsync(id, cancellationToken));

    [HttpGet("legal-entities")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> LegalEntities(CancellationToken cancellationToken)
        => Ok(await _queries.GetLegalEntitiesAsync(cancellationToken));

    [HttpGet("reporting-profiles")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> ReportingProfiles([FromQuery] Guid? legalEntityId, CancellationToken cancellationToken)
        => Ok(await _queries.GetReportingProfilesAsync(legalEntityId, cancellationToken));

    [HttpGet("readiness")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> Readiness([FromQuery] Guid legalEntityId, [FromQuery] string profileCode, [FromQuery] string reportType, [FromQuery] DateOnly periodDate, CancellationToken cancellationToken)
        => Ok(await _readiness.EvaluateAsync(legalEntityId, profileCode, reportType, periodDate, cancellationToken));

    [HttpPost("rules/simulate")]
    [HasPermission(PermissionCodes.FinanceGovernanceView)]
    public async Task<IActionResult> Simulate([FromBody] FinancialRuleCreateRequest request, CancellationToken cancellationToken)
        => HandleResult(await _queries.SimulateAsync(request, cancellationToken));

    [HttpPost("rules")]
    [HasPermission(PermissionCodes.FinanceGovernanceManage)]
    public async Task<IActionResult> Create([FromBody] FinancialRuleCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await _rules.CreateDraftAsync(request, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(Get), new { id = result.Value.RuleVersionId }, result.Value) : HandleResult(result);
    }

    [HttpPost("rules/{id:guid}/submit")]
    [HasPermission(PermissionCodes.FinanceGovernanceManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken cancellationToken)
        => HandleResult(await _rules.SubmitForApprovalAsync(id, cancellationToken));

    [HttpPost("rules/{id:guid}/approve")]
    [HasPermission(PermissionCodes.FinanceGovernanceApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] FinanceRuleDecisionRequest request, CancellationToken cancellationToken)
        => HandleResult(await _rules.ApproveAsync(id, request.ExpectedFingerprint, request.ExpectedRowVersion, cancellationToken));

    [HttpPost("rules/{id:guid}/reject")]
    [HasPermission(PermissionCodes.FinanceGovernanceApprove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] FinanceRuleRejectionRequest request, CancellationToken cancellationToken)
        => HandleResult(await _rules.RejectAsync(id, request.ExpectedFingerprint, request.ExpectedRowVersion, request.Reason, cancellationToken));
}

public sealed record FinanceRuleDecisionRequest(string ExpectedFingerprint, int ExpectedRowVersion);
public sealed record FinanceRuleRejectionRequest(string ExpectedFingerprint, int ExpectedRowVersion, string Reason);
