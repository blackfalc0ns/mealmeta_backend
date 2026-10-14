using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record GetOfferByIdQuery(Guid Id) : IQuery<OfferDto>;

public sealed class GetOfferByIdQueryHandler : IQueryHandler<GetOfferByIdQuery, OfferDto>
{
    private readonly IAppDbContext _context;

    public GetOfferByIdQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<OfferDto>> Handle(GetOfferByIdQuery request, CancellationToken cancellationToken)
    {
        var offer = await _context.Offers
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken);

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", $"Offer with ID '{request.Id}' was not found.");
        }

        return CreateOfferDraftCommandHandler.MapOfferDto(offer);
    }
}
