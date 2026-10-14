using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Referrals;

public sealed record AttributeReferralCommand(string Code) : ICommand<ReferralAttributionDto>;

public sealed record ReferralAttributionDto(
    Guid AttributionId,
    string Code,
    Guid ReferrerUserId,
    Guid ReferredCustomerUserId,
    DateTime AttributedAtUtc);

/// <summary>
/// Attributes the authenticated customer to a referral code, once.
/// Self-referral is rejected; a second attribution for the same customer is a conflict.
/// Attribution alone grants no reward — eligibility follows the approved financial policy
/// at the first qualifying paid subscription.
/// </summary>
public sealed class AttributeReferralCommandHandler : ICommandHandler<AttributeReferralCommand, ReferralAttributionDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AttributeReferralCommandHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ReferralAttributionDto>> Handle(AttributeReferralCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Error.Validation("Referral.CodeRequired", "A referral code is required.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var referredUserId = _currentUser.UserId.Value;

        var existing = await _context.ReferralAttributions
            .FirstOrDefaultAsync(a => a.ReferredCustomerUserId == referredUserId, cancellationToken);
        if (existing is not null)
        {
            return Error.Conflict("Referral.AlreadyAttributed",
                "This customer is already attributed to a referral.");
        }

        var referralCode = await _context.ReferralCodes
            .FirstOrDefaultAsync(r => r.Code == code, cancellationToken);
        if (referralCode is null || !referralCode.IsActive)
        {
            return Error.NotFound("Referral.CodeNotFound", $"Referral code '{code}' was not found or is inactive.");
        }

        if (referralCode.OwnerUserId == referredUserId)
        {
            return Error.Validation("Referral.SelfReferral", "A customer cannot use their own referral code.");
        }

        var attribution = new ReferralAttribution
        {
            Code = code,
            ReferrerUserId = referralCode.OwnerUserId,
            ReferredCustomerUserId = referredUserId,
            ReferralCodeId = referralCode.Id,
            AttributedAtUtc = _clock.UtcNow,
            CreatedAtUtc = _clock.UtcNow,
            CreatedByUserId = referredUserId
        };

        _context.ReferralAttributions.Add(attribution);
        await _context.SaveChangesAsync(cancellationToken);

        return new ReferralAttributionDto(
            attribution.Id,
            attribution.Code,
            attribution.ReferrerUserId,
            attribution.ReferredCustomerUserId,
            attribution.AttributedAtUtc);
    }
}
