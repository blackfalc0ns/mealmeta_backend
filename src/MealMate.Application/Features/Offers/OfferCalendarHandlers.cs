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

public sealed record AddOfferToCalendarCommand(Guid OfferId, DateOnly ScheduledDate) : ICommand<Guid>;

public sealed record RemoveOfferFromCalendarCommand(Guid OfferId, DateOnly ScheduledDate) : ICommand<Guid>;

/// <summary>Body of POST /customer/offers/{offerId}/calendar binding "yyyy-MM-dd" as a DateOnly.</summary>
public sealed record AddOfferToCalendarRequest(DateOnly ScheduledDate);

public sealed record ListOfferCalendarQuery : IQuery<OfferCalendarResponse>;

public record OfferCalendarEntryDto(DateOnly ScheduledDate, OfferDto Offer);

public record OfferCalendarResponse(int TotalCount, List<OfferCalendarEntryDto> Entries);

public sealed class AddOfferToCalendarCommandHandler : ICommandHandler<AddOfferToCalendarCommand, Guid>
{
    public const string DefaultSource = "offer-detail";

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AddOfferToCalendarCommandHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(AddOfferToCalendarCommand request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Customer authentication is required to schedule offers.");
        }

        // Same saveable-offer rule as the saved-offers flow: existing (non-deleted) and Active.
        var offerId = await _db.Offers.AsNoTracking()
            .Where(o => o.Id == request.OfferId && o.Status == OfferStatus.Active)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (offerId is null)
        {
            return Error.NotFound("offers.customer.unavailable", string.Empty);
        }

        // The unique (CustomerUserId, OfferId, ScheduledDate) index keeps soft-deleted rows occupied,
        // so re-adding the same offer/day revives the row instead of inserting a duplicate.
        var row = await _db.CustomerOfferCalendarEntries
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerId
                && x.OfferId == offerId.Value
                && x.ScheduledDate == request.ScheduledDate, cancellationToken);
        if (row is not null && !row.IsDeleted)
        {
            return row.Id;
        }

        if (row is not null)
        {
            row.IsDeleted = false;
            row.DeletedAtUtc = null;
            row.DeletedByUserId = null;
            row.CreatedAtUtc = _clock.UtcNow;
            row.Status = CustomerOfferCalendarEntryStatus.Active;
            row.Source = DefaultSource;
        }
        else
        {
            row = new CustomerOfferCalendarEntry
            {
                CustomerUserId = customerId,
                OfferId = offerId.Value,
                ScheduledDate = request.ScheduledDate,
                Status = CustomerOfferCalendarEntryStatus.Active,
                Source = DefaultSource,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.CustomerOfferCalendarEntries.Add(row);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }
}

public sealed class RemoveOfferFromCalendarCommandHandler : ICommandHandler<RemoveOfferFromCalendarCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RemoveOfferFromCalendarCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(RemoveOfferFromCalendarCommand request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Customer authentication is required to edit the offer calendar.");
        }

        var row = await _db.CustomerOfferCalendarEntries
            .FirstOrDefaultAsync(x => x.CustomerUserId == customerId
                && x.OfferId == request.OfferId
                && x.ScheduledDate == request.ScheduledDate, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("offer.calendar_not_booked", $"Offer '{request.OfferId}' is not scheduled on that calendar date by this customer.");
        }

        // The row state is tracked and the soft delete is applied by the auditable-entity
        // interceptor (hard-delete=false), matching the other AuditableEntity tables.
        _db.CustomerOfferCalendarEntries.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.OfferId;
    }
}

public sealed class ListOfferCalendarQueryHandler : IQueryHandler<ListOfferCalendarQuery, OfferCalendarResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ListOfferCalendarQueryHandler(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<OfferCalendarResponse>> Handle(ListOfferCalendarQuery request, CancellationToken cancellationToken)
    {
        if (!CustomerCommandGuard.TryGetCustomerId(_currentUser, out var customerId))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "The offer calendar is scoped to the authenticated customer.");
        }

        // Binding simplest rule: entries scheduled before today's UTC date never appear on
        // the calendar; detecting "completed" days for those is deliberate future work.
        var today = _clock.TodayUtc;
        var rows = await _db.CustomerOfferCalendarEntries.AsNoTracking()
            .Where(x => x.CustomerUserId == customerId
                && x.Status == CustomerOfferCalendarEntryStatus.Active
                && x.ScheduledDate >= today)
            .OrderBy(x => x.ScheduledDate)
            .ThenBy(x => x.OfferId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new OfferCalendarResponse(0, []);
        }

        var offerIds = rows.Select(x => x.OfferId).Distinct().ToList();
        var offers = await _db.Offers.AsNoTracking()
            .Include(o => o.EligiblePrograms)
            .Include(o => o.EligibleBundles)
            .Include(o => o.PackageTargets)
            .Include(o => o.EligibleRestaurants)
            .Where(o => offerIds.Contains(o.Id) && o.Status == OfferStatus.Active)
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        var savedOfferIds = await CustomerSavedOfferMapping.LoadSavedOfferIdsAsync(_db, customerId, cancellationToken);

        var entries = rows
            .Where(x => offers.ContainsKey(x.OfferId))
            .Select(x => new OfferCalendarEntryDto(
                x.ScheduledDate,
                CustomerSavedOfferMapping.MapCustomerOffer(offers[x.OfferId], savedOfferIds.Contains(x.OfferId))))
            .ToList();

        return new OfferCalendarResponse(entries.Count, entries);
    }
}
