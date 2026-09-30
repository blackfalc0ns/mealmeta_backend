using MealMate.Api.Authorization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[ApiController, Authorize, HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/provider-refunds")]
public sealed class ProviderRefundOperationsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IAppDbContext db, CancellationToken ct)
        => Ok(await db.RefundOperations.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new { x.Id, x.OperationReference, x.PaymentProvider, x.Amount, x.Currency, Status = x.State.ToString(),
                x.ProviderRefundReference, x.Detail, x.CreatedAtUtc, x.SubmittedAtUtc, x.ReconciledAtUtc, x.ConfirmedAtUtc, x.LocalAppliedAtUtc }).ToListAsync(ct));

    [HttpPost("{id:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id, [FromServices] IAppDbContext db,
        [FromServices] IPaymentRefundGateway gateway, CancellationToken ct)
    {
        if (!await db.RefundOperations.AsNoTracking().AnyAsync(x => x.Id == id, ct)) return NotFound();
        return Ok(await gateway.ReconcileAsync(id, ct));
    }
}
