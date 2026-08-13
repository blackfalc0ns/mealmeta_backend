using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/nutrition_programs_management")]
public sealed class NutritionProgramsManagementController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> List(
        [FromQuery] ProgramStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetProgramsListQuery(status, search, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetProgramByIdQuery(id), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> Create([FromBody] CreateProgramCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/nutrition_programs_management");

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProgramRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UpdateProgramCommand(id, request.NameAr, request.NameEn, request.DescriptionAr, request.DescriptionEn,
            request.SortOrder, request.ShortCode, request.KeywordsAr, request.KeywordsEn), cancellationToken));

    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeProgramStatusRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ChangeProgramStatusCommand(id, request.Status, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.ProgramsApprove)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] StatusActionRequest? request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ChangeProgramStatusCommand(id, ProgramStatus.Approved, request?.Reason), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.ProgramsApprove)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] StatusActionRequest? request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ChangeProgramStatusCommand(id, ProgramStatus.Rejected, request?.Reason), cancellationToken));
}

public sealed record UpdateProgramRequest(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    int SortOrder,
    string? ShortCode = null,
    IReadOnlyList<string>? KeywordsAr = null,
    IReadOnlyList<string>? KeywordsEn = null);

public sealed record ChangeProgramStatusRequest(
    ProgramStatus Status,
    string? Reason);

public sealed record StatusActionRequest(string? Reason);
