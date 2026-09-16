using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerProfileService
{
    Task<Result<InfluencerPartnerProfileDto>> GetProfileAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerLinksDto>> GetLinksAsync(Guid userId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<BankCatalogItemDto>>> GetBanksAsync(CancellationToken ct = default);
    Task<Result<InfluencerBankInfoDto>> GetBankAccountAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerBankInfoDto>> UpdateBankAccountAsync(Guid userId, UpdateInfluencerBankAccountCommand command, CancellationToken ct = default);
    Task<Result> DeleteBankAccountAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerSocialLinksResponseDto>> UpdateSocialLinksAsync(Guid userId, UpdateInfluencerSocialLinksCommand command, CancellationToken ct = default);
}
