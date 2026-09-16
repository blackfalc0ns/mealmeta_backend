using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Influencer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/influencer-partner")]
[Authorize]
public sealed class InfluencerPartnerController : ApiControllerBase
{
    [HttpGet("banks")]
    public async Task<IActionResult> GetBanks(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerBanksQuery(), cancellationToken));

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerMeQuery(), cancellationToken));

    [HttpPut("me/social-links")]
    public async Task<IActionResult> UpdateSocialLinks(
        [FromBody] UpdateInfluencerSocialLinksRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateInfluencerSocialLinksCommand(
            body.Tiktok,
            body.Instagram,
            body.Whatsapp,
            body.Telegram,
            body.Youtube,
            body.Snapchat), cancellationToken));

    [HttpGet("me/home")]
    public async Task<IActionResult> GetHomeDashboard(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerHomeDashboardQuery(), cancellationToken));

    [HttpGet("me/links")]
    public async Task<IActionResult> GetLinks(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerLinksQuery(), cancellationToken));

    [HttpGet("me/activities")]
    public async Task<IActionResult> GetActivities(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetInfluencerActivitiesQuery(page, pageSize), cancellationToken));

    [HttpGet("me/analytics")]
    public async Task<IActionResult> GetAnalytics(
        [FromQuery] string? period,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerAnalyticsQuery(period ?? "30d"), cancellationToken));

    [HttpGet("me/wallet")]
    public async Task<IActionResult> GetWallet(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerWalletQuery(), cancellationToken));

    [HttpPost("me/withdrawals")]
    public async Task<IActionResult> SubmitWithdrawal(
        [FromBody] SubmitWithdrawalApiRequest body,
        CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(new SubmitInfluencerWithdrawalCommand(
            body.Amount,
            body.WithdrawalMethod ?? "BankTransfer",
            body.BankName,
            body.AccountHolderName,
            body.Iban,
            body.SaveAsDefaultBank,
            body.CorrelationId), cancellationToken), "/api/v1/influencer-partner/me/withdrawals");

    [HttpGet("me/bank-account")]
    public async Task<IActionResult> GetBankAccount(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerBankAccountQuery(), cancellationToken));

    [HttpPut("me/bank-account")]
    public async Task<IActionResult> UpdateBankAccount(
        [FromBody] UpdateInfluencerBankAccountRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateInfluencerBankAccountCommand(
            body.BankName,
            body.AccountHolderName,
            body.Iban), cancellationToken));

    [HttpDelete("me/bank-account")]
    public async Task<IActionResult> DeleteBankAccount(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new DeleteInfluencerBankAccountCommand(), cancellationToken));

    [HttpGet("me/withdrawals")]
    public async Task<IActionResult> GetWithdrawals(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetInfluencerWithdrawalsQuery(status, page, pageSize), cancellationToken));

    [HttpGet("me/withdrawals/{id:guid}")]
    public async Task<IActionResult> GetWithdrawalDetails(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetInfluencerWithdrawalDetailsQuery(id), cancellationToken));

    [HttpPost("me/withdrawals/{id:guid}/cancel")]
    public async Task<IActionResult> CancelWithdrawal(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new CancelInfluencerWithdrawalCommand(id), cancellationToken));
}

public sealed record UpdateInfluencerSocialLinksRequest(
    string? Tiktok,
    string? Instagram,
    string? Whatsapp,
    string? Telegram,
    string? Youtube,
    string? Snapchat);

public sealed record UpdateInfluencerBankAccountRequest(
    string BankName,
    string AccountHolderName,
    string Iban);

public sealed record SubmitWithdrawalApiRequest(
    decimal Amount,
    string? WithdrawalMethod = "BankTransfer",
    string? BankName = null,
    string? AccountHolderName = null,
    string? Iban = null,
    bool SaveAsDefaultBank = true,
    string? CorrelationId = null);

