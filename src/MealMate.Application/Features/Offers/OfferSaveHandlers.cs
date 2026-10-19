using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Offers.DTOs;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Commands;

public sealed record SaveOfferCommand(Guid OfferId) : ICommand<Guid>;

public sealed record UnsaveOfferCommand(Guid OfferId) : ICommand<Guid>;

internal static class CustomerCommandGuard
{
    public static bool TryGetCustomerId(ICurrentUser user, out Guid customerId)
    {
        customerId = user.UserId ?? Guid.Empty;
        return user.IsAuthenticated
            && user.UserType == UserType.Customer
            && customerId != Guid.Empty;
    }
}

internal static class CustomerSavedOfferMapping
{
    public static async Task<HashSet<Guid>> LoadSavedOfferIdsAsync(
        IAppDbContext db,
        Guid customerUserId,
        CancellationToken cancellationToken)
    {
        if (customerUserId == Guid.Empty)
        {
            return [];
        }

        var ids = await db.CustomerSavedOffers.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId)
            .Select(x => x.OfferId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public static OfferDto MapCustomerOffer(Offer offer, bool isSaved) =>
        CreateOfferDraftCommandHandler.MapOfferDto(offer) with { IsSaved = isSaved };
}

public sealed class SaveOfferCommandHandler : ICommandHandler<SaveOfferCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SaveOfferCommandHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(SaveOfferCommand request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Customer authentication is required to save offers.");
        }

        // Saveable offers match the customer listing visibility rule: existing and Active.
        var offerId = await _db.Offers.AsNoTracking()
            .Where(o => o.Id == request.OfferId && o.Status == OfferStatus.Active)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (offerId is null)
        {
            return Error.NotFound("offers.customer.unavailable", string.Empty);
        }

        var savedRow = await _db.CustomerSavedOffers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerId && x.OfferId == offerId.Value, cancellationToken);
        if (savedRow is not null)
        {
            if (!savedRow.IsDeleted)
            {
                return savedRow.OfferId;
            }

            // The unique (CustomerUserId, OfferId) index keeps soft-deleted rows occupied,
            // so re-saving revives the row as a fresh save instead of inserting a duplicate.
            savedRow.IsDeleted = false;
            savedRow.DeletedAtUtc = null;
            savedRow.DeletedByUserId = null;
            savedRow.CreatedAtUtc = _clock.UtcNow;
        }
        else
        {
            _db.CustomerSavedOffers.Add(new CustomerSavedOffer
            {
                CustomerUserId = customerId,
                OfferId = offerId.Value,
                CreatedAtUtc = _clock.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return offerId.Value;
    }
}

public sealed class UnsaveOfferCommandHandler : ICommandHandler<UnsaveOfferCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public UnsaveOfferCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(UnsaveOfferCommand request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Customer authentication is required to unsave offers.");
        }

        var row = await _db.CustomerSavedOffers
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerId && x.OfferId == request.OfferId, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("offer.not_saved", $"Offer '{request.OfferId}' is not saved by this customer.");
        }

        _db.CustomerSavedOffers.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.OfferId;
    }
}

public sealed record GetSavedOffersQuery : IQuery<GetSavedOffersResponse>;

public record GetSavedOffersResponse(int TotalCount, List<OfferDto> Offers);

public sealed class GetSavedOffersQueryHandler : IQueryHandler<GetSavedOffersQuery, GetSavedOffersResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetSavedOffersQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<GetSavedOffersResponse>> Handle(GetSavedOffersQuery request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Saved offers are scoped to the authenticated customer.");
        }

        var rows = await _db.CustomerSavedOffers.AsNoTracking()
            .Where(x => x.CustomerUserId == customerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.OfferId })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new GetSavedOffersResponse(0, []);
        }

        var offerIds = rows.Select(x => x.OfferId).Distinct().ToList();
        var offers = await _db.Offers.AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.PackageTargets)
            .Include(o => o.EligibleRestaurants)
            .Where(o => offerIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var dtos = rows
            .Where(x => offers.ContainsKey(x.OfferId))
            .Select(x => CustomerSavedOfferMapping.MapCustomerOffer(offers[x.OfferId], isSaved: true))
            .ToList();

        return new GetSavedOffersResponse(dtos.Count, dtos);
    }
}
