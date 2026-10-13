using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

namespace MealMate.Application.Features.FamilyCheckout;

public interface IFamilySubscriptionFlowService
{
    Task<Result<FamilyDraftMetadataDto>> GetMetadataAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<FamilyLookupDto>>> GetRelationshipsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerBundleCardDto>>> GetBundlesAsync(Guid programId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerDurationOptionDto>>> GetDurationsAsync(Guid programId, Guid? bundleId, Guid customerUserId, bool includeFriday, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerTierOptionDto>>> GetTiersAsync(Guid programId, Guid? bundleId, Guid customerUserId, int? durationDays, bool includeFriday, Guid? familyMemberId = null, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> GetCurrentAsync(CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> UpsertGroupAsync(UpsertFamilyGroupRequest request, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> ConvertFromIndividualAsync(ConvertFromIndividualRequest? request, CancellationToken ct = default);
    Task<Result<bool>> AbandonDraftAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<FamilySizePackageDto>>> GetPackagesAsync(CancellationToken ct = default);
    Task<Result<FamilyCapacityUpgradeDto>> UpgradeCapacityAsync(FamilyCapacityUpgradeRequest request, CancellationToken ct = default);
    Task<Result<FamilyMemberLookupDto>> LookupMemberAsync(FamilyMemberLookupRequest request, CancellationToken ct = default);
    Task<Result<bool>> SaveMemberAllergiesAsync(Guid memberId, SaveFamilyMemberAllergiesRequest request, CancellationToken ct = default);
    Task<Result<FamilyMemberAllergiesDto>> GetMemberAllergiesAsync(Guid memberId, CancellationToken ct = default);
    Task<Result<bool>> SaveMemberDislikedFoodsAsync(Guid memberId, SaveFamilyMemberDislikedFoodsRequest request, CancellationToken ct = default);
    Task<Result<FamilyMemberDislikedFoodsDto>> GetMemberDislikedFoodsAsync(Guid memberId, CancellationToken ct = default);
    Task<Result<bool>> SaveMemberDietaryAsync(Guid memberId, SaveFamilyMemberDietaryRequest request, CancellationToken ct = default);
    Task<Result<FamilyMemberDietaryDto>> GetMemberDietaryAsync(Guid memberId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerAddressDto>>> GetMemberAddressesAsync(Guid memberId, CancellationToken ct = default);
    Task<Result<CustomerAddressDto>> CreateMemberAddressAsync(Guid memberId, CustomerAddressRequest request, CancellationToken ct = default);
    Task<Result<CustomerAddressDto>> UpdateMemberAddressAsync(Guid memberId, Guid addressId, CustomerAddressRequest request, CancellationToken ct = default);
    Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageAsync(Guid memberId, PreviewAddressCoverageRequest request, CancellationToken ct = default);
    Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageByIdAsync(Guid memberId, Guid addressId, int? page = null, int? pageSize = null, CancellationToken ct = default);
    Task<Result<CustomerAddressDto>> SetMemberDefaultAddressAsync(Guid memberId, Guid addressId, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> AddMemberAsync(FamilyMemberWriteDto request, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> UpdateMemberAsync(Guid memberId, FamilyMemberWriteDto request, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> DeleteMemberAsync(Guid memberId, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> SetMemberPlanAsync(Guid memberId, SetFamilyMemberPlanRequest request, CancellationToken ct = default);
    Task<Result<FamilyGroupScreenDto>> SetMemberStartDateAsync(Guid memberId, DateOnly startDate, CancellationToken ct = default);
    Task<Result<FamilyCheckoutSummaryDto>> GetSummaryAsync(Guid? memberId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default);
    Task<Result<FamilyCheckoutStartedDto>> StartCheckoutAsync(FamilyStartCheckoutRequest request, CancellationToken ct = default);
    Task<Result<FamilyCheckoutStartedDto>> StartRenewalCheckoutAsync(Guid renewalRequestId, FamilyRenewalCheckoutRequest request, CancellationToken ct = default);
}
