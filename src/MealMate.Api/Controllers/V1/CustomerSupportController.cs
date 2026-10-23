using MealMate.Api.Common;
using MealMate.Application.Features.UnifiedSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/customer/support")]
public sealed class CustomerSupportController(SupportCaseService service) : ApiControllerBase
{
    [HttpGet("options")]
    [ProducesResponseType(typeof(CustomerSupportOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Options(CancellationToken ct)
        => HandleResult(await service.GetCustomerSupportOptionsAsync(ct));

    [HttpGet("issues")]
    [ProducesResponseType(typeof(CustomerSupportPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIssues([FromQuery] CustomerSupportListQuery query, CancellationToken ct)
        => HandleResult(await service.ListCustomerSupportRequestsAsync(CustomerSupportCatalog.IssuesRequestType, query, ct));

    [HttpPost("issues")]
    [ProducesResponseType(typeof(SupportCaseDto), StatusCodes.Status201Created)]
    public Task<IActionResult> CreateIssue([FromBody] CustomerSupportCreateRequest request, CancellationToken ct)
        => Create("issues", request, ct);

    [HttpGet("suggestions")]
    [ProducesResponseType(typeof(CustomerSupportPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSuggestions([FromQuery] CustomerSupportListQuery query, CancellationToken ct)
        => HandleResult(await service.ListCustomerSupportRequestsAsync(CustomerSupportCatalog.SuggestionsRequestType, query, ct));

    [HttpPost("suggestions")]
    [ProducesResponseType(typeof(SupportCaseDto), StatusCodes.Status201Created)]
    public Task<IActionResult> CreateSuggestion([FromBody] CustomerSupportCreateRequest request, CancellationToken ct)
        => Create("suggestions", request, ct);

    [HttpGet("{requestType}/{caseId:guid}", Name = "CustomerSupportRequestDetail")]
    [ProducesResponseType(typeof(SupportCaseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(string requestType, Guid caseId, CancellationToken ct)
        => HandleResult(await service.GetCustomerSupportRequestAsync(requestType, caseId, ct));

    [HttpPost("{requestType}/{caseId:guid}/messages")]
    [ProducesResponseType(typeof(SupportCaseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMessage(string requestType, Guid caseId,
        [FromBody] CustomerSupportReplyRequest request, CancellationToken ct)
        => HandleResult(await service.AddCustomerSupportReplyAsync(requestType, caseId, request, ct));

    [HttpPost("{requestType}/{caseId:guid}/actions/{operation}")]
    [ProducesResponseType(typeof(SupportCaseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Action(string requestType, Guid caseId, string operation,
        [FromBody] CustomerSupportActionRequest request, CancellationToken ct)
        => HandleResult(await service.ExecuteCustomerSupportActionAsync(requestType, caseId, operation, request, ct));

    private async Task<IActionResult> Create(string requestType, CustomerSupportCreateRequest request, CancellationToken ct)
    {
        var result = await service.CreateCustomerSupportRequestAsync(requestType, request, ct);
        if (!result.IsSuccess) return HandleResult(result);
        return CreatedAtRoute("CustomerSupportRequestDetail",
            new { requestType, caseId = result.Value.Id }, result.Value);
    }
}
