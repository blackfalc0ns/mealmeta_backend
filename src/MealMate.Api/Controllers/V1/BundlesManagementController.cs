using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/bundles_management")]
public sealed class BundlesManagementController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.BundlesView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? programId = null,
        [FromQuery] ProgramStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetBundlesListQuery(programId, status, search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.BundlesView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetBundleByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.BundlesManage)]
    public async Task<IActionResult> Create([FromBody] CreateBundleCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/bundles_management");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.BundlesManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBundleRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateBundleCommand(id, request.NameAr, request.NameEn, request.MealsPerDay, request.Description, request.SortOrder), cancellationToken));

    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.BundlesManage)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeBundleStatusRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ChangeBundleStatusCommand(id, request.Status, request.Reason), cancellationToken));
}

public sealed record UpdateBundleRequest(
    string NameAr,
    string NameEn,
    int MealsPerDay,
    string? Description,
    int SortOrder);

public sealed record ChangeBundleStatusRequest(
    ProgramStatus Status,
    string? Reason);
