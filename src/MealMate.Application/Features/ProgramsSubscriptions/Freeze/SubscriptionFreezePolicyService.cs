using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using Microsoft.Extensions.Caching.Memory;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

// Compatibility adapter: the legacy editor publishes the same versioned operational policy.
public sealed class SubscriptionFreezePolicyService : ISubscriptionFreezePolicyService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICurrentUser? _user;
    private readonly IDateTimeProvider? _clock;
    public SubscriptionFreezePolicyService(IAppDbContext db, IAuditLogger audit, ICurrentUser? user = null, IDateTimeProvider? clock = null)
    { _db = db; _audit = audit; _user = user; _clock = clock; }
    public async Task<Result<SubscriptionFreezePolicyDocument>> GetPolicyAsync(CancellationToken ct = default)
        => Result.Success(await SubscriptionFreezePolicyDefaults.ReadAsync(_db, ct));
    public async Task<Result<SubscriptionFreezePolicyDocument>> UpdatePolicyAsync(SubscriptionFreezePolicyDocument document, CancellationToken ct = default)
    {
        var error = SubscriptionFreezePolicyDefaults.Validate(document);
        if (error is not null) return Error.Validation("validation.invalid", error);
        if (_user?.UserId is not { } actor || actor == Guid.Empty) return Error.Unauthorized("admin.required", "Authenticated administrator is required.");
        var service = new OperationalPolicyService(_db, new MemoryCache(new MemoryCacheOptions()), _audit, _clock);
        var current = await service.GetSettingsAsync(ct);
        var updated = current with { Freeze = current.Freeze with { MaxDays = document.DefaultDurationDays, LeadDaysNotice = document.StartDelayDays, ExtendSubscriptionEnd = document.ExtendSubscriptionEnd }, ChangeReason = "Updated through legacy freeze policy editor" };
        var result = await service.UpdateSettingsAsync(updated, actor, ct);
        if (result.IsFailure) return Error.Conflict("policy.publication_failed", result.Error);
        return Result.Success(await SubscriptionFreezePolicyDefaults.ReadAsync(_db, ct));
    }
}
