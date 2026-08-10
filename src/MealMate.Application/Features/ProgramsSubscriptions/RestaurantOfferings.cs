using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record RestaurantOfferingDto(Guid ProgramId, Guid BundleId, bool IsActive);

public sealed record GetRestaurantOfferingsQuery(Guid RestaurantId)
    : IQuery<IReadOnlyList<RestaurantOfferingDto>>;

public sealed record SaveRestaurantOfferingsCommand(
    Guid RestaurantId,
    IReadOnlyList<RestaurantOfferingDto> Offerings)
    : ICommand<IReadOnlyList<RestaurantOfferingDto>>;

public sealed class SaveRestaurantOfferingsCommandValidator : AbstractValidator<SaveRestaurantOfferingsCommand>
{
    public SaveRestaurantOfferingsCommandValidator()
    {
        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleForEach(x => x.Offerings).ChildRules(item =>
        {
            item.RuleFor(x => x.ProgramId).NotEmpty();
            item.RuleFor(x => x.BundleId).NotEmpty();
        });
    }
}

public sealed class RestaurantOfferingsHandlers :
    IQueryHandler<GetRestaurantOfferingsQuery, IReadOnlyList<RestaurantOfferingDto>>,
    ICommandHandler<SaveRestaurantOfferingsCommand, IReadOnlyList<RestaurantOfferingDto>>
{
    private readonly IAppDbContext _db;

    public RestaurantOfferingsHandlers(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<RestaurantOfferingDto>>> Handle(
        GetRestaurantOfferingsQuery request, CancellationToken cancellationToken)
    {
        var result = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.RestaurantId == request.RestaurantId && x.IsActive)
            .Select(x => new RestaurantOfferingDto(x.ProgramId, x.BundleId, x.IsActive))
            .ToListAsync(cancellationToken);
        return result;
    }

    public async Task<Result<IReadOnlyList<RestaurantOfferingDto>>> Handle(
        SaveRestaurantOfferingsCommand request, CancellationToken cancellationToken)
    {
        var distinct = request.Offerings
            .Where(x => x.IsActive)
            .DistinctBy(x => new { x.ProgramId, x.BundleId })
            .ToList();

        var validPairs = await _db.Bundles.AsNoTracking()
            .Where(b => b.IsActive && distinct.Select(x => x.BundleId).Contains(b.Id))
            .Select(b => new { b.ProgramId, BundleId = b.Id })
            .ToListAsync(cancellationToken);

        if (validPairs.Count != distinct.Count || distinct.Any(x => !validPairs.Contains(new { x.ProgramId, x.BundleId })))
            return Error.Validation("restaurant_offerings.invalid_bundle", "Every selected bundle must belong to an active program.");

        var existing = await _db.RestaurantProgramBundleOffers
            .Where(x => x.RestaurantId == request.RestaurantId)
            .ToListAsync(cancellationToken);
        _db.RestaurantProgramBundleOffers.RemoveRange(existing);
        var saved = distinct.Select(x => new RestaurantProgramBundleOffer
        {
            RestaurantId = request.RestaurantId,
            ProgramId = x.ProgramId,
            BundleId = x.BundleId,
            IsActive = true,
        }).ToList();
        _db.RestaurantProgramBundleOffers.AddRange(saved);
        await _db.SaveChangesAsync(cancellationToken);
        return saved.Select(x => new RestaurantOfferingDto(x.ProgramId, x.BundleId, true)).ToList();
    }
}
