using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminDashboard.Tenants;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/tenants")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public sealed class AdminTenantsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ListTenantsQuery(), cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantRequest body,
        CancellationToken cancellationToken)
        => HandleCreated(
            await Sender.Send(new CreateTenantCommand(
                body.Code,
                body.NameAr,
                body.NameEn,
                body.CountryCode,
                body.CountryNameAr,
                body.CountryNameEn,
                body.CurrencyCode,
                body.TimeZoneId,
                body.DefaultLanguage,
                body.Domain,
                body.Reason), cancellationToken),
            "/api/v1/admin/tenants");

    [HttpPost("{tenantId:guid}/status")]
    public async Task<IActionResult> SetStatus(
        Guid tenantId,
        [FromBody] SetTenantStatusRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(
            new SetTenantStatusCommand(tenantId, body.Status, body.Reason),
            cancellationToken));
}

public sealed record CreateTenantRequest(
    string Code,
    string NameAr,
    string NameEn,
    string CountryCode,
    string CountryNameAr,
    string CountryNameEn,
    string CurrencyCode,
    string TimeZoneId,
    string DefaultLanguage,
    string Domain,
    string Reason);

public sealed record SetTenantStatusRequest(TenantStatus Status, string Reason);
