using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerProfileService
{
    Task<Result<InfluencerPartnerProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerLinksDto>> GetLinksAsync(Guid userId, CancellationToken ct = default);
    Task<Result> UpdateSocialLinksAsync(Guid userId, UpdateInfluencerSocialLinksCommand command, CancellationToken ct = default);
}
