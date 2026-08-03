using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record PublicRestaurantOptionDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string LegalNameAr,
    string LegalNameEn);

public sealed record GetPublicActiveRestaurantsQuery : IQuery<IReadOnlyList<PublicRestaurantOptionDto>>;

public sealed class GetPublicActiveRestaurantsQueryHandler
    : IQueryHandler<GetPublicActiveRestaurantsQuery, IReadOnlyList<PublicRestaurantOptionDto>>
{
    private readonly IAppDbContext _db;

    public GetPublicActiveRestaurantsQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<PublicRestaurantOptionDto>>> Handle(
        GetPublicActiveRestaurantsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurants = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(r => r.Status == VerificationStatus.Approved || r.Status == VerificationStatus.Submitted)
            .OrderBy(r => r.TradeNameAr)
            .Select(r => new PublicRestaurantOptionDto(
                r.Id,
                r.TradeNameAr,
                r.TradeNameEn,
                r.LegalNameAr,
                r.LegalNameEn))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PublicRestaurantOptionDto>>(restaurants);
    }
}
