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
    Task<Result<AdminInfluencerDetailDto>> GetInfluencerDetailAsync(Guid id, CancellationToken ct = default);
    Task<Result<InfluencerDto>> UpdateInfluencerStatusAsync(Guid id, string status, string reason, CancellationToken ct = default);
    Task<Result<InfluencerDto>> UpdateInfluencerCommissionRateAsync(Guid id, decimal ratePercent, string reason, CancellationToken ct = default);
    Task<Result<InfluencerDto>> UpdateInfluencerBankInfoAsync(Guid id, string bankName, string accountHolderName, string iban, CancellationToken ct = default);
}
