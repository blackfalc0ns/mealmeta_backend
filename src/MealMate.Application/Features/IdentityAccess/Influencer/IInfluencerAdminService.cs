using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

/// <summary>
/// Domain service for administrative influencer operations and management.
/// </summary>
public interface IInfluencerAdminService
{
    Task<Result<IReadOnlyList<AdminInfluencerDto>>> ListInfluencersAsync(CancellationToken ct = default);
    Task<Result<InfluencerDto>> RegisterInfluencerAsync(RegisterInfluencerCommand request, CancellationToken ct = default);
    Task<Result<InfluencerDto>> PromoteCustomerAsync(PromoteCustomerToInfluencerCommand request, CancellationToken ct = default);
    Task<Result<InfluencerDto>> LinkCustomerAsync(Guid influencerProfileId, Guid customerUserId, string reason, CancellationToken ct = default);
    Task<Result<InfluencerDto>> UnlinkCustomerAsync(Guid influencerProfileId, string reason, CancellationToken ct = default);
}
