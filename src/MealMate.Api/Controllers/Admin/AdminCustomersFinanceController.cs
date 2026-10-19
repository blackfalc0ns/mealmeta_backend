using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerFinance;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/customers/{customerId:guid}/wallets")]
public sealed class AdminCustomersFinanceController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.CustomersWalletsManage)]
    public async Task<IActionResult> GetCustomerWallets(Guid customerId, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetCustomerWalletsQuery(customerId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("{walletType:int}")]
    [HasPermission(PermissionCodes.CustomersWalletsManage)]
    public async Task<IActionResult> AdjustWallet(
        Guid customerId,
        int walletType,
        [FromBody] AdjustWalletBalanceRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(WalletType), walletType))
            return BadRequest(new { code = "wallet.type_invalid", message = "Invalid wallet type." });

        var command = new AdjustWalletBalanceCommand(
            customerId,
            (WalletType)walletType,
            request.TransactionType,
            request.Reason,
            request.Amount);

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}

public sealed record AdjustWalletBalanceRequest(
    WalletTransactionType TransactionType,
    decimal Amount,
    string Reason);
