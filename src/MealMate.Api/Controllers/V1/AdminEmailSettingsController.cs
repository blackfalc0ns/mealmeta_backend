using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminDashboard.Email;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/email-settings")]
[HasPermission(PermissionCodes.SettingsManage)]
public sealed class AdminEmailSettingsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        HandleResult(await Sender.Send(new GetEmailSettingsQuery(), cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateEmailSettingsCommand command,
        CancellationToken cancellationToken) =>
        HandleResult(await Sender.Send(command, cancellationToken));

    [HttpPost("test")]
    public async Task<IActionResult> Test(
        [FromBody] TestEmailRequest request,
        CancellationToken cancellationToken) =>
        HandleResult(await Sender.Send(new SendTestEmailCommand(request.Recipient), cancellationToken));

    [HttpPost("templates/{templateKey}/test")]
    public async Task<IActionResult> TestTemplate(
        string templateKey,
        [FromBody] TestTemplateEmailRequest request,
        CancellationToken cancellationToken) =>
        HandleResult(await Sender.Send(
            new SendTemplateTestEmailCommand(templateKey, request.Language, request.Recipient),
            cancellationToken));

    [HttpGet("logs")]
    public async Task<IActionResult> Logs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        HandleResult(await Sender.Send(new GetEmailLogsQuery(page, pageSize), cancellationToken));
}

public sealed record TestEmailRequest(string? Recipient);
public sealed record TestTemplateEmailRequest(string Language, string Recipient);
