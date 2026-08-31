using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Application.Features.RestaurantOperations;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantLocationDto(
    Guid RestaurantId,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    Guid? PrimaryRegionId,
    IReadOnlyList<RestaurantLocationRegionMarkerDto> ServiceRegionMarkers);

public sealed record RestaurantLocationRegionMarkerDto(
    Guid RegionId,
    string Code,
    string NameAr,
    string NameEn,
    decimal? CentroidLatitude,
    decimal? CentroidLongitude);

public sealed record GetRestaurantLocationQuery(Guid? RestaurantId = null)
    : IQuery<RestaurantLocationDto>;

public sealed record UpdateRestaurantLocationCommand(
    decimal Latitude,
    decimal Longitude,
    string Address,
    Guid? RestaurantId = null)
    : ICommand<RestaurantLocationDto>;

public sealed class UpdateRestaurantLocationCommandValidator : AbstractValidator<UpdateRestaurantLocationCommand>
{
    public UpdateRestaurantLocationCommandValidator()
    {
        RuleFor(x => x.Address)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.Latitude)
            .Must((cmd, _) => RestaurantRegistrationRules.IsWithinKuwaitBounds(cmd.Latitude, cmd.Longitude))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Longitude)
            .Must((cmd, _) => RestaurantRegistrationRules.IsWithinKuwaitBounds(cmd.Latitude, cmd.Longitude))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
    }
}

public sealed class RestaurantLocationHandlers
    : IQueryHandler<GetRestaurantLocationQuery, RestaurantLocationDto>,
      ICommandHandler<UpdateRestaurantLocationCommand, RestaurantLocationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RestaurantLocationHandlers(IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<RestaurantLocationDto>> Handle(
        GetRestaurantLocationQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = RestaurantOperationsScope.ResolveRestaurantId(_currentUser, request.RestaurantId);
        if (restaurantIdResult.IsFailure) return Result.Failure<RestaurantLocationDto>(restaurantIdResult.Error);

        return await MapAsync(restaurantIdResult.Value, cancellationToken);
    }

    public async Task<Result<RestaurantLocationDto>> Handle(
        UpdateRestaurantLocationCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantIdResult = RestaurantOperationsScope.ResolveRestaurantId(_currentUser, request.RestaurantId);
        if (restaurantIdResult.IsFailure) return Result.Failure<RestaurantLocationDto>(restaurantIdResult.Error);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return Error.Validation(MessageCodes.Validation.RequiredField, "Address is required.");
        }

        if (!RestaurantRegistrationRules.IsWithinKuwaitBounds(request.Latitude, request.Longitude))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Location must be within Kuwait bounds.");
        }

        var profile = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(x => x.Id == restaurantIdResult.Value, cancellationToken);
        if (profile is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile was not found.");

        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        profile.ModifiedAtUtc = _clock.UtcNow;

        var registration = await _db.RestaurantRegistrations
            .Where(x => x.ResultingRestaurantId == profile.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is not null)
        {
            registration.Latitude = request.Latitude;
            registration.Longitude = request.Longitude;
            registration.Address = RestaurantRegistrationRules.NormalizeOptional(request.Address);
            registration.ModifiedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await MapAsync(profile.Id, cancellationToken);
    }

    private async Task<Result<RestaurantLocationDto>> MapAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var profile = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == restaurantId, cancellationToken);
        if (profile is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile was not found.");

        var registration = await _db.RestaurantRegistrations
            .AsNoTracking()
            .Where(x => x.ResultingRestaurantId == restaurantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var serviceRegionIds = await _db.RestaurantServiceRegions
            .AsNoTracking()
            .Where(x => x.RestaurantId == restaurantId && x.IsActive)
            .Select(x => x.RegionId)
            .ToListAsync(cancellationToken);

        var markers = await _db.Regions
            .AsNoTracking()
            .Where(x => serviceRegionIds.Contains(x.Id))
            .OrderBy(x => x.NameAr)
            .Select(x => new RestaurantLocationRegionMarkerDto(
                x.Id,
                x.Code,
                x.NameAr,
                x.NameEn,
                x.CentroidLatitude,
                x.CentroidLongitude))
            .ToListAsync(cancellationToken);

        return new RestaurantLocationDto(
            profile.Id,
            registration?.Address,
            profile.Latitude ?? registration?.Latitude,
            profile.Longitude ?? registration?.Longitude,
            profile.PrimaryRegionId ?? registration?.RegionId,
            markers);
    }
}
