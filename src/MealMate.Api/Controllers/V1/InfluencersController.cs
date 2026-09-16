using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/influencers")]
public sealed class InfluencersController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.InfluencerView)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListInfluencersQuery(), cancellationToken));

    [HttpGet("eligible-customers")]
    [HasPermission(PermissionCodes.InfluencerView)]
    public async Task<IActionResult> GetEligibleCustomers(
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListEligibleCustomersForPromotionQuery(search), cancellationToken));

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterInfluencerCommand body, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(body, cancellationToken), "/api/v1/influencers");

    [HttpPost("promote-customer")]
    [HasPermission(PermissionCodes.InfluencerRegCreate)]
    public async Task<IActionResult> PromoteCustomer(
        [FromBody] PromoteCustomerToInfluencerRequest body,
        CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(new PromoteCustomerToInfluencerCommand(
            body.CustomerUserId,
            body.DisplayName,
            body.InfluencerCode,
            body.DefaultCommissionRate,
            body.ActivateNow), cancellationToken), "/api/v1/influencers");

    [HttpPost("{id:guid}/link")]
    [HasPermission(PermissionCodes.InfluencerLink)]
    public async Task<IActionResult> Link(Guid id, [FromBody] LinkRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new LinkInfluencerCustomerCommand(id, body.CustomerUserId, body.Reason), cancellationToken));

    [HttpPost("{id:guid}/unlink")]
    [HasPermission(PermissionCodes.InfluencerLink)]
    public async Task<IActionResult> Unlink(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnlinkInfluencerCustomerCommand(id, body.Reason), cancellationToken));

    [HttpGet("withdrawals")]
    [HttpGet("/api/v1/admin/influencers/withdrawals")]
    [HasPermission(PermissionCodes.InfluencerView)]
    public async Task<IActionResult> ListWithdrawals(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListAdminWithdrawalsQuery(status, search, page, pageSize), cancellationToken));

    [HttpPost("withdrawals/{id:guid}/approve")]
    [HttpPost("/api/v1/admin/influencers/withdrawals/{id:guid}/approve")]
    [HasPermission(PermissionCodes.InfluencerManage)]
    public async Task<IActionResult> ApproveWithdrawal(
        Guid id,
        [FromBody] ApproveWithdrawalApiRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ApproveInfluencerWithdrawalCommand(id, body.TransferReferenceNumber, body.TransferDateUtc, body.Notes), cancellationToken));

    [HttpPost("withdrawals/{id:guid}/reject")]
    [HttpPost("/api/v1/admin/influencers/withdrawals/{id:guid}/reject")]
    [HasPermission(PermissionCodes.InfluencerManage)]
    public async Task<IActionResult> RejectWithdrawal(
        Guid id,
        [FromBody] RejectWithdrawalApiRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RejectInfluencerWithdrawalCommand(id, body.RejectionReason), cancellationToken));

    [HttpGet("{id:guid}")]
    [HttpGet("/api/v1/admin/influencers/{id:guid}")]
    [HasPermission(PermissionCodes.InfluencerView)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminInfluencerDetailQuery(id), cancellationToken));

    [HttpPut("{id:guid}/status")]
    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.InfluencerManage)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateInfluencerStatusApiRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateInfluencerStatusCommand(id, body.Status, body.Reason ?? "Updated by admin"), cancellationToken));

    [HttpPut("{id:guid}/commission-rate")]
    [HttpPost("{id:guid}/commission-rate")]
    [HasPermission(PermissionCodes.InfluencerManage)]
    public async Task<IActionResult> UpdateCommissionRate(
        Guid id,
        [FromBody] UpdateInfluencerCommissionRateApiRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateInfluencerCommissionRateCommand(id, body.Rate, body.Reason ?? "Updated by admin"), cancellationToken));

    [HttpPut("{id:guid}/bank-info")]
    [HttpPost("{id:guid}/bank-info")]
    [HasPermission(PermissionCodes.InfluencerManage)]
    public async Task<IActionResult> UpdateBankInfo(
        Guid id,
        [FromBody] UpdateInfluencerBankInfoApiRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateInfluencerBankInfoCommand(id, body.BankName, body.AccountHolderName, body.Iban), cancellationToken));
}

public sealed record PromoteCustomerToInfluencerRequest(
    Guid CustomerUserId,
    string DisplayName,
    string InfluencerCode,
    decimal DefaultCommissionRate,
    bool ActivateNow = true);

public sealed record LinkRequest(Guid CustomerUserId, string Reason);

public sealed record ApproveWithdrawalApiRequest(
    string TransferReferenceNumber,
    DateTime? TransferDateUtc = null,
    string? Notes = null);

public sealed record RejectWithdrawalApiRequest(string RejectionReason);

public sealed record UpdateInfluencerStatusApiRequest(string Status, string? Reason);

public sealed record UpdateInfluencerCommissionRateApiRequest(decimal Rate, string? Reason);

public sealed record UpdateInfluencerBankInfoApiRequest(string BankName, string AccountHolderName, string Iban);
