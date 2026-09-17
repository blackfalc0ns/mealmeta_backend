using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record GetOffersListQuery(
    OfferStatus? Status = null,
    PageRequest? Page = null,
    string? Search = null
) : IQuery<PagedResult<OfferDto>>;

public sealed class GetOffersListQueryHandler : IQueryHandler<GetOffersListQuery, PagedResult<OfferDto>>
{
    private readonly IAppDbContext _context;

    public GetOffersListQueryHandler(IAppDbContext context) => _context = context;

    public async Task<Result<PagedResult<OfferDto>>> Handle(GetOffersListQuery request, CancellationToken cancellationToken)
    {
        var pageReq = request.Page ?? new PageRequest();
        var query = _context.Offers.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(o => o.Status == request.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(o => o.NameAr.ToLower().Contains(search) || o.NameEn.ToLower().Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(CreateOfferDraftCommandHandler.MapOfferDto).ToList();

        return new PagedResult<OfferDto>(dtos, total, pageReq.NormalizedPage, pageReq.NormalizedPageSize);
    }
}
