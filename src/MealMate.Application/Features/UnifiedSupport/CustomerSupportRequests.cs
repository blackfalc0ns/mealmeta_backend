using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed record CustomerSupportCreateRequest(
    string TypeCode,
    string Subject,
    string Description,
    string IdempotencyKey,
    string? RelatedResourceType = null,
    Guid? RelatedResourceId = null);

public sealed record CustomerSupportListQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? TypeCode = null);

public sealed record CustomerSupportListItemDto(
    Guid Id,
    string RequestType,
    string? TypeCode,
    string Category,
    string Subject,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    uint Version);

public sealed record CustomerSupportPageDto(
    IReadOnlyList<CustomerSupportListItemDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

public sealed record CustomerSupportReplyRequest(
    string Body,
    uint ExpectedVersion,
    string IdempotencyKey,
    IReadOnlyList<string>? AttachmentKeys = null,
    Guid? TaskId = null);

public sealed record CustomerSupportActionRequest(
    uint ExpectedVersion,
    string IdempotencyKey,
    string? Reason = null);

public sealed partial class SupportCaseService
{
    public Task<Result<CustomerSupportOptionsDto>> GetCustomerSupportOptionsAsync(CancellationToken ct = default)
    {
        if (ActorId is null) return Task.FromResult<Result<CustomerSupportOptionsDto>>(Unauthorized());
        if (_currentUser.UserType != MealMate.Domain.Enums.UserType.Customer)
            return Task.FromResult<Result<CustomerSupportOptionsDto>>(Forbidden());
        return Task.FromResult<Result<CustomerSupportOptionsDto>>(Result.Success(CustomerSupportCatalog.GetOptions()));
    }

    public async Task<Result<SupportCaseDto>> CreateCustomerSupportRequestAsync(
        string requestType,
        CustomerSupportCreateRequest request,
        CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (_currentUser.UserType != MealMate.Domain.Enums.UserType.Customer) return Forbidden();
        if (!CustomerSupportCatalog.TryNormalizeRequestType(requestType, out var normalizedRequestType, out var kind))
            return NotFound();
        if (!CustomerSupportCatalog.TryNormalizeTypeCode(normalizedRequestType, request.TypeCode, out _, out var category))
            return Invalid();

        return await CreateAsync(new CreateSupportCaseRequest(
            SupportCaseChannel.Customer,
            kind,
            category,
            request.Subject,
            request.Description,
            request.IdempotencyKey,
            request.RelatedResourceType,
            request.RelatedResourceId), ct);
    }

    public async Task<Result<CustomerSupportPageDto>> ListCustomerSupportRequestsAsync(
        string requestType,
        CustomerSupportListQuery request,
        CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (_currentUser.UserType != MealMate.Domain.Enums.UserType.Customer) return Forbidden();
        if (!CustomerSupportCatalog.TryNormalizeRequestType(requestType, out var normalizedRequestType, out var kind))
            return NotFound();
        if (request.PageNumber < 1 || request.PageSize is < 1 or > 100
            || (long)request.PageNumber * request.PageSize > int.MaxValue)
            return Invalid();

        SupportCaseStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<SupportCaseStatus>(request.Status.Trim(), true, out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
                return Invalid();
            status = parsedStatus;
        }

        string? typeCode = null;
        string? categoryFilter = null;
        if (!string.IsNullOrWhiteSpace(request.TypeCode))
        {
            if (!CustomerSupportCatalog.TryNormalizeTypeCode(normalizedRequestType, request.TypeCode, out typeCode, out categoryFilter))
                return Invalid();
        }

        var query = _db.SupportCases.AsNoTracking()
            .Where(supportCase => supportCase.Channel == SupportCaseChannel.Customer
                && supportCase.RequesterUserId == ActorId.Value
                && supportCase.Kind == kind);
        if (normalizedRequestType == CustomerSupportCatalog.SuggestionsRequestType)
        {
            var knownSuggestionCategories = CustomerSupportCatalog.Suggestions
                .Select(option => CustomerSupportCatalog.CategoryFor(normalizedRequestType, option.Value)).ToArray();
            query = query.Where(supportCase => knownSuggestionCategories.Contains(supportCase.Category));
        }
        else
        {
            query = query.Where(supportCase => !supportCase.Category.ToLower()
                .StartsWith(CustomerSupportCatalog.SuggestionCategoryPrefix));
        }
        if (categoryFilter is not null)
            query = query.Where(supportCase => supportCase.Category == categoryFilter);
        if (status.HasValue)
            query = query.Where(supportCase => supportCase.Status == status.Value);

        var totalCount = await query.CountAsync(ct);
        var offset = checked((int)((long)(request.PageNumber - 1) * request.PageSize));
        var rows = await query
            .OrderByDescending(supportCase => supportCase.CreatedAtUtc)
            .ThenBy(supportCase => supportCase.Id)
            .Skip(offset)
            .Take(request.PageSize)
            .Select(supportCase => new
            {
                supportCase.Id,
                supportCase.Category,
                supportCase.Subject,
                supportCase.Status,
                supportCase.CreatedAtUtc,
                supportCase.ModifiedAtUtc,
                Version = EF.Property<uint>(supportCase, "xmin")
            })
            .ToListAsync(ct);

        var items = rows.Select(row => new CustomerSupportListItemDto(
            row.Id,
            normalizedRequestType,
            TryGetTypeCode(normalizedRequestType, row.Category),
            row.Category,
            row.Subject,
            row.Status.ToString(),
            row.CreatedAtUtc,
            row.ModifiedAtUtc,
            row.Version)).ToArray();
        return Result.Success(new CustomerSupportPageDto(items, request.PageNumber, request.PageSize, totalCount));
    }

    public async Task<Result<SupportCaseDto>> GetCustomerSupportRequestAsync(
        string requestType,
        Guid caseId,
        CancellationToken ct = default)
    {
        var supportCase = await FindOwnedCustomerSupportRequestAsync(requestType, caseId, ct);
        if (supportCase is null) return ActorId is null ? Unauthorized() : _currentUser.UserType != MealMate.Domain.Enums.UserType.Customer ? Forbidden() : NotFound();
        return await GetAsync(caseId, ct);
    }

    public async Task<Result<SupportCaseDto>> AddCustomerSupportReplyAsync(
        string requestType,
        Guid caseId,
        CustomerSupportReplyRequest request,
        CancellationToken ct = default)
    {
        if (request.TaskId.HasValue || request.AttachmentKeys?.Any(string.IsNullOrWhiteSpace) == true) return Invalid();
        if (await FindOwnedCustomerSupportRequestAsync(requestType, caseId, ct) is null)
            return ActorId is null ? Unauthorized() : _currentUser.UserType != MealMate.Domain.Enums.UserType.Customer ? Forbidden() : NotFound();
        return await AddMessageAsync(caseId, new AddSupportMessageRequest(
            request.Body, request.ExpectedVersion, request.IdempotencyKey, TaskId: null, request.AttachmentKeys), ct);
    }

    public async Task<Result<SupportCaseDto>> ExecuteCustomerSupportActionAsync(
        string requestType,
        Guid caseId,
        string operation,
        CustomerSupportActionRequest request,
        CancellationToken ct = default)
    {
        if (!string.Equals(operation, "close", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(operation, "reopen", StringComparison.OrdinalIgnoreCase))
            return NotFound();
        if (await FindOwnedCustomerSupportRequestAsync(requestType, caseId, ct) is null)
            return ActorId is null ? Unauthorized() : _currentUser.UserType != MealMate.Domain.Enums.UserType.Customer ? Forbidden() : NotFound();
        var normalizedOperation = operation.ToLowerInvariant();
        return await ExecuteActionAsync(caseId, new SupportCaseActionRequest(
            normalizedOperation, request.ExpectedVersion, request.IdempotencyKey, request.Reason), ct);
    }

    private async Task<SupportCase?> FindOwnedCustomerSupportRequestAsync(string requestType, Guid caseId, CancellationToken ct)
    {
        if (ActorId is not { } actorId || _currentUser.UserType != MealMate.Domain.Enums.UserType.Customer
            || !CustomerSupportCatalog.TryNormalizeRequestType(requestType, out var normalizedRequestType, out var kind))
            return null;

        var query = _db.SupportCases.AsNoTracking().Where(supportCase => supportCase.Id == caseId
            && supportCase.RequesterUserId == actorId
            && supportCase.Channel == SupportCaseChannel.Customer
            && supportCase.Kind == kind);
        if (normalizedRequestType == CustomerSupportCatalog.SuggestionsRequestType)
        {
            var knownSuggestionCategories = CustomerSupportCatalog.Suggestions
                .Select(option => CustomerSupportCatalog.CategoryFor(normalizedRequestType, option.Value)).ToArray();
            query = query.Where(supportCase => knownSuggestionCategories.Contains(supportCase.Category));
        }
        else
        {
            query = query.Where(supportCase => !supportCase.Category.ToLower()
                .StartsWith(CustomerSupportCatalog.SuggestionCategoryPrefix));
        }
        return await query.SingleOrDefaultAsync(ct);
    }

    private static string? TryGetTypeCode(string requestType, string category)
    {
        var prefix = requestType == CustomerSupportCatalog.SuggestionsRequestType
            ? CustomerSupportCatalog.SuggestionCategoryPrefix
            : CustomerSupportCatalog.IssueCategoryPrefix;
        return category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && CustomerSupportCatalog.TryNormalizeTypeCode(requestType, category[prefix.Length..], out var typeCode, out _)
                ? typeCode
                : null;
    }
}
