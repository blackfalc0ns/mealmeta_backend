using MealMate.Api.Common;
using MealMate.Application.Features.CustomerFinance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MealMate.Application.Common.Abstractions;

namespace MealMate.Api.Controllers.Customer;

/// <summary>Customer wallet read endpoints (spec F6): balances and paged transaction ledger.</summary>
[Authorize]
[Route("api/v1/customer/wallet")]
[Route("api/v1/customer/wallets")]
public sealed class CustomerWalletController : ApiControllerBase
{
    private ICurrentUser? _currentUser;

    [ActivatorUtilitiesConstructor]
    public CustomerWalletController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public CustomerWalletController() { }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [HttpGet]
    public async Task<IActionResult> GetWalletSummary(CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCustomerWalletSummaryQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetWalletTransactions(
        [FromQuery] string? walletType = "promotional",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCustomerWalletTransactionsQuery(
            WalletType: walletType ?? string.Empty,
            Page: page,
            PageSize: pageSize), cancellationToken);
        return HandleResult(result);
    }
}
