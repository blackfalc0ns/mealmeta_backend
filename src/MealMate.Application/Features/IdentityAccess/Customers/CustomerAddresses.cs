using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public sealed record AddressServingRestaurantDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string? LogoUrl,
    decimal? Rating);

public sealed record CustomerAddressDto(
    Guid Id,
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null,
    bool IsDeliveryActive = false,
    DateTime? DeliveryEffectiveFromUtc = null,
    LocalizedTextDto? DeliveryChangeNotice = null,
    LocalizedTextDto? RestaurantCoverageNotice = null,
    IReadOnlyList<AddressServingRestaurantDto>? ServingRestaurants = null);

public sealed record GetCustomerAddressesQuery : IQuery<IReadOnlyList<CustomerAddressDto>>;

public sealed record CreateCustomerAddressCommand(
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null) : ICommand<CustomerAddressDto>;

public sealed record UpdateCustomerAddressCommand(
    Guid Id,
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery,
    string? Governorate,
    string? Area,
    string? Block,
    string? Street,
    string? Avenue,
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null) : ICommand<CustomerAddressDto>;

public sealed record DeleteCustomerAddressCommand(Guid Id) : ICommand;

public sealed record AddressCoveragePreviewDto(
    bool CanSetAsDefault,
    bool IsCurrentSubscriptionRestaurantCovered,
    string? UncoveredRestaurantNameAr,
    string? UncoveredRestaurantNameEn,
    string? ConflictMessage,
    Guid? ResolvedRegionId,
    string? ResolvedRegionNameAr,
    string? ResolvedRegionNameEn,
    string? Governorate,
    string? Area,
    string? Block,
    decimal? Latitude,
    decimal? Longitude,
    LocalizedTextDto? RestaurantCoverageNotice,
    IReadOnlyList<AddressServingRestaurantDto> ServingRestaurants,
    bool HasActiveSubscription = false);

public sealed record CustomerAddressRequest(
    string Label,
    string AddressLine,
    Guid? RegionId,
    decimal? Latitude,
    decimal? Longitude,
    string? Notes,
    bool IsDefault,
    bool NotifyOnDelivery = true,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    string? Street = null,
    string? Avenue = null,
    string? Building = null,
    string? Unit = null,
    string? Floor = null,
    Guid? BlockRegionId = null);

public sealed record PreviewAddressCoverageRequest(
    Guid? AddressId = null,
    Guid? RegionId = null,
    Guid? BlockRegionId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    bool IsDefault = true,
    int? Page = null,
    int? PageSize = null);

public sealed record PreviewCustomerAddressCoverageCommand(
    Guid? AddressId = null,
    Guid? RegionId = null,
    Guid? BlockRegionId = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    string? Governorate = null,
    string? Area = null,
    string? Block = null,
    bool IsDefault = true,
    int? Page = null,
    int? PageSize = null) : ICommand<AddressCoveragePreviewDto>;

public sealed record SetDefaultCustomerAddressCommand(Guid Id) : ICommand<CustomerAddressDto>;

public interface ICustomerAddressBook
{
    Task<Result<IReadOnlyList<CustomerAddressDto>>> ListAsync(Guid userId, CancellationToken ct);
    Task<Result<CustomerAddressDto>> CreateAsync(Guid userId, CreateCustomerAddressCommand request, CancellationToken ct);
    Task<Result<CustomerAddressDto>> UpdateAsync(Guid userId, UpdateCustomerAddressCommand request, CancellationToken ct);
    Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken ct);
    Task<Result<AddressCoveragePreviewDto>> PreviewCoverageAsync(Guid userId, PreviewCustomerAddressCoverageCommand request, CancellationToken ct);
    Task<Result<CustomerAddressDto>> SetDefaultAsync(Guid userId, Guid id, CancellationToken ct);
}

public sealed class CustomerAddressCommandValidator : AbstractValidator<CreateCustomerAddressCommand>
{
    public CustomerAddressCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        RuleFor(x => x.Governorate).MaximumLength(100).When(x => x.Governorate is not null);
        RuleFor(x => x.Area).MaximumLength(100).When(x => x.Area is not null);
        RuleFor(x => x.Block).MaximumLength(50).When(x => x.Block is not null);
        RuleFor(x => x.Street).MaximumLength(150).When(x => x.Street is not null);
        RuleFor(x => x.Avenue).MaximumLength(50).When(x => x.Avenue is not null);
        RuleFor(x => x.Building).MaximumLength(50).When(x => x.Building is not null);
        RuleFor(x => x.Unit).MaximumLength(50).When(x => x.Unit is not null);
        RuleFor(x => x.Floor).MaximumLength(50).When(x => x.Floor is not null);
        RuleFor(x => x.Latitude)
            .InclusiveBetween(KuwaitGeoBounds.MinLatitude, KuwaitGeoBounds.MaxLatitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude)
            .InclusiveBetween(KuwaitGeoBounds.MinLongitude, KuwaitGeoBounds.MaxLongitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .When(x => x.Longitude.HasValue);
        RuleFor(x => x)
            .Must(cmd => (cmd.Latitude.HasValue && cmd.Longitude.HasValue && KuwaitGeoBounds.IsWithin(cmd.Latitude, cmd.Longitude))
                         || (!cmd.Latitude.HasValue && !cmd.Longitude.HasValue && (cmd.RegionId.HasValue || cmd.BlockRegionId.HasValue || !string.IsNullOrWhiteSpace(cmd.Area))))
            .WithErrorCode(MessageCodes.Validation.RequiredField)
            .WithMessage("Coordinates must be inside Kuwait, or a valid region/area must be provided.");
    }
}

public sealed class UpdateCustomerAddressCommandValidator : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressCommandValidator()
    {
        RuleFor(x => x.Label).NotEmpty().MaximumLength(80);
        RuleFor(x => x.AddressLine).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(500).When(x => x.Notes is not null);
        RuleFor(x => x.Governorate).MaximumLength(100).When(x => x.Governorate is not null);
        RuleFor(x => x.Area).MaximumLength(100).When(x => x.Area is not null);
        RuleFor(x => x.Block).MaximumLength(50).When(x => x.Block is not null);
        RuleFor(x => x.Street).MaximumLength(150).When(x => x.Street is not null);
        RuleFor(x => x.Avenue).MaximumLength(50).When(x => x.Avenue is not null);
        RuleFor(x => x.Building).MaximumLength(50).When(x => x.Building is not null);
        RuleFor(x => x.Unit).MaximumLength(50).When(x => x.Unit is not null);
        RuleFor(x => x.Floor).MaximumLength(50).When(x => x.Floor is not null);
        RuleFor(x => x.Latitude)
            .InclusiveBetween(KuwaitGeoBounds.MinLatitude, KuwaitGeoBounds.MaxLatitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude)
            .InclusiveBetween(KuwaitGeoBounds.MinLongitude, KuwaitGeoBounds.MaxLongitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .When(x => x.Longitude.HasValue);
        RuleFor(x => x)
            .Must(cmd => (cmd.Latitude.HasValue && cmd.Longitude.HasValue && KuwaitGeoBounds.IsWithin(cmd.Latitude, cmd.Longitude))
                         || (!cmd.Latitude.HasValue && !cmd.Longitude.HasValue))
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .WithMessage("Coordinates must be inside Kuwait if provided, or both omitted to retain or resolve from region.");
    }
}

public sealed class CustomerAddressHandlers :
    ICustomerAddressBook,
    IQueryHandler<GetCustomerAddressesQuery, IReadOnlyList<CustomerAddressDto>>,
    ICommandHandler<CreateCustomerAddressCommand, CustomerAddressDto>,
    ICommandHandler<UpdateCustomerAddressCommand, CustomerAddressDto>,
    ICommandHandler<DeleteCustomerAddressCommand>,
    ICommandHandler<PreviewCustomerAddressCoverageCommand, AddressCoveragePreviewDto>,
    ICommandHandler<SetDefaultCustomerAddressCommand, CustomerAddressDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly IDateTimeProvider _clock;

    public CustomerAddressHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        ICustomerDeliveryAddressService deliveryAddresses,
        IDateTimeProvider clock)
        => (_db, _currentUser, _audit, _deliveryAddresses, _clock) = (db, currentUser, audit, deliveryAddresses, clock);

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> Handle(GetCustomerAddressesQuery request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await ListAsync(userId.Value, ct);
    }

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> ListAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId, ct);
        await _deliveryAddresses.TryApplyPendingAsync(profile, ct);
        var items = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label)
            .ToListAsync(ct);
        return items.Select(x => Map(x, profile)).ToList();
    }

    public async Task<Result<CustomerAddressDto>> Handle(CreateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await CreateAsync(userId.Value, request, ct);
    }

    public async Task<Result<CustomerAddressDto>> CreateAsync(Guid userId, CreateCustomerAddressCommand request, CancellationToken ct)
    {
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId, ct);
        var location = await ResolveLocationAsync(request.RegionId, request.BlockRegionId, request.Governorate, request.Area, request.Block, ct);
        if (location.IsFailure) return location.Error;

        var lat = request.Latitude ?? location.Value.CentroidLatitude;
        var lng = request.Longitude ?? location.Value.CentroidLongitude;

        if (!KuwaitGeoBounds.IsWithin(lat, lng))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "Latitude and longitude are required and must be inside Kuwait.");
        }

        var hadAddresses = _db.CustomerAddresses.Local.Any(x => x.UserId == userId)
            || await _db.CustomerAddresses.AnyAsync(x => x.UserId == userId, ct);
        var makeDefault = request.IsDefault || !hadAddresses;

        if (makeDefault && location.Value.AreaId.HasValue)
        {
            var coverageCheck = await ValidateActiveSubscriptionRestaurantCoverageAsync(userId, location.Value.AreaId.Value, ct);
            if (coverageCheck.IsFailure) return coverageCheck.Error;
        }

        if (makeDefault) await ClearDefaultAsync(userId, ct);

        var address = new CustomerAddress
        {
            UserId = userId, Label = request.Label.Trim(), AddressLine = request.AddressLine.Trim(),
            RegionId = location.Value.AreaId, Latitude = lat, Longitude = lng,
            Notes = request.Notes?.Trim(), IsDefault = makeDefault, NotifyOnDelivery = request.NotifyOnDelivery,
            Governorate = location.Value.Governorate, Area = location.Value.Area, Block = location.Value.Block, BlockRegionId = location.Value.BlockId,
            Street = request.Street?.Trim(), Avenue = request.Avenue?.Trim(), Building = request.Building?.Trim(), Unit = request.Unit?.Trim(), Floor = request.Floor?.Trim(),
        };
        _db.CustomerAddresses.Add(address);

        if (makeDefault || !profile.ActiveDeliveryAddressId.HasValue)
            await _deliveryAddresses.InitializeActiveDeliveryAsync(profile, address, ct);

        await _audit.LogAsync(new AuditEntry("CustomerAddressCreated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);

        LocalizedTextDto? coverageNotice = null;
        IReadOnlyList<AddressServingRestaurantDto>? servingRestaurants = null;
        if (makeDefault && address.RegionId is { } areaId && areaId != Guid.Empty)
        {
            var coverageInfo = await ValidateActiveSubscriptionCoverageInfoAsync(userId, areaId, ct);
            (coverageNotice, servingRestaurants) = await LoadServingRestaurantsAsync(areaId, ct, coverageInfo.SubscriptionPoolRestaurantIds);
        }

        return Map(address, profile, coverageNotice, servingRestaurants);
    }

    public async Task<Result<CustomerAddressDto>> Handle(UpdateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await UpdateAsync(userId.Value, request, ct);
    }

    public async Task<Result<CustomerAddressDto>> UpdateAsync(Guid userId, UpdateCustomerAddressCommand request, CancellationToken ct)
    {
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId, ct);
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == request.Id && x.UserId == userId, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");
        var location = await ResolveLocationAsync(request.RegionId, request.BlockRegionId, request.Governorate, request.Area, request.Block, ct);
        if (location.IsFailure) return location.Error;

        var regionChanged = location.Value.AreaId.HasValue && location.Value.AreaId != address.RegionId;
        var lat = request.Latitude ?? (regionChanged ? (location.Value.CentroidLatitude ?? address.Latitude) : (address.Latitude ?? location.Value.CentroidLatitude));
        var lng = request.Longitude ?? (regionChanged ? (location.Value.CentroidLongitude ?? address.Longitude) : (address.Longitude ?? location.Value.CentroidLongitude));

        if (!KuwaitGeoBounds.IsWithin(lat, lng))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "Latitude and longitude are required and must be inside Kuwait.");
        }

        var becomingDefault = request.IsDefault && !address.IsDefault;
        var wasActiveDelivery = profile.ActiveDeliveryAddressId == address.Id;
        var contentChanged = CustomerDeliveryAddressService.HasDeliveryContentChanged(address, request);

        if ((becomingDefault || wasActiveDelivery || request.IsDefault) && location.Value.AreaId.HasValue)
        {
            var coverageCheck = await ValidateActiveSubscriptionRestaurantCoverageAsync(userId, location.Value.AreaId.Value, ct);
            if (coverageCheck.IsFailure) return coverageCheck.Error;
        }

        if (becomingDefault) await ClearDefaultAsync(userId, ct);

        address.Label = request.Label.Trim(); address.AddressLine = request.AddressLine.Trim();
        address.RegionId = location.Value.AreaId; address.Latitude = lat; address.Longitude = lng;
        address.Notes = request.Notes?.Trim(); address.IsDefault = request.IsDefault; address.NotifyOnDelivery = request.NotifyOnDelivery;
        address.Governorate = location.Value.Governorate; address.Area = location.Value.Area; address.Block = location.Value.Block; address.BlockRegionId = location.Value.BlockId;
        address.Street = request.Street?.Trim(); address.Avenue = request.Avenue?.Trim(); address.Building = request.Building?.Trim(); address.Unit = request.Unit?.Trim(); address.Floor = request.Floor?.Trim();

        if (becomingDefault || wasActiveDelivery || (request.IsDefault && contentChanged))
            await _deliveryAddresses.InitializeActiveDeliveryAsync(profile, address, ct);

        await _audit.LogAsync(new AuditEntry("CustomerAddressUpdated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);

        LocalizedTextDto? coverageNotice = null;
        IReadOnlyList<AddressServingRestaurantDto>? servingRestaurants = null;
        if ((becomingDefault || wasActiveDelivery || request.IsDefault) && address.RegionId is { } updateAreaId && updateAreaId != Guid.Empty)
        {
            var coverageInfo = await ValidateActiveSubscriptionCoverageInfoAsync(userId, updateAreaId, ct);
            (coverageNotice, servingRestaurants) = await LoadServingRestaurantsAsync(updateAreaId, ct, coverageInfo.SubscriptionPoolRestaurantIds);
        }

        return Map(address, profile, coverageNotice, servingRestaurants);
    }

    public async Task<Result> Handle(DeleteCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await DeleteAsync(userId.Value, request.Id, ct);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId, ct);
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");

        if (address.IsDefault || profile.ActiveDeliveryAddressId == address.Id)
            return Error.Conflict(
                "address.active_delivery_cannot_delete",
                "Cannot delete the default or currently active delivery address. Set another address as default first, then delete this address.");

        if (profile.PendingDeliveryAddressId == address.Id)
        {
            profile.PendingDeliveryAddressId = null;
            profile.PendingDeliveryAddressEffectiveFromUtc = null;
        }

        _db.CustomerAddresses.Remove(address);
        await _audit.LogAsync(new AuditEntry("CustomerAddressDeleted", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Result.Success();
    }

    public async Task<Result<AddressCoveragePreviewDto>> Handle(PreviewCustomerAddressCoverageCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await PreviewCoverageAsync(userId.Value, request, ct);
    }

    public async Task<Result<AddressCoveragePreviewDto>> PreviewCoverageAsync(Guid userId, PreviewCustomerAddressCoverageCommand request, CancellationToken ct)
    {
        CustomerAddress? existingAddress = null;
        if (request.AddressId.HasValue)
        {
            existingAddress = await _db.CustomerAddresses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.AddressId.Value && x.UserId == userId, ct);
            if (existingAddress is null)
                return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");
        }

        var regionId = request.RegionId ?? existingAddress?.RegionId;
        var blockRegionId = request.BlockRegionId ?? existingAddress?.BlockRegionId;
        var govName = request.Governorate ?? existingAddress?.Governorate;
        var areaName = request.Area ?? existingAddress?.Area;
        var blockName = request.Block ?? existingAddress?.Block;
        var lat = request.Latitude ?? existingAddress?.Latitude;
        var lng = request.Longitude ?? existingAddress?.Longitude;

        var location = await ResolveLocationAsync(regionId, blockRegionId, govName, areaName, blockName, ct);
        if (location.IsFailure) return location.Error;

        lat ??= location.Value.CentroidLatitude;
        lng ??= location.Value.CentroidLongitude;

        if (!location.Value.AreaId.HasValue || location.Value.AreaId.Value == Guid.Empty)
            return Error.Validation("address.area_required", "Select a valid delivery area.");

        var areaId = location.Value.AreaId.Value;
        var coverageInfo = await ValidateActiveSubscriptionCoverageInfoAsync(userId, areaId, ct);

        var (notice, servingRestaurants) = await LoadServingRestaurantsAsync(
            areaId, ct, coverageInfo.SubscriptionPoolRestaurantIds);

        var canSetAsDefault = coverageInfo.IsCovered;
        var conflictMsg = !coverageInfo.IsCovered
            ? (coverageInfo.UncoveredRestaurantNameAr != null
                ? $"مطعم اشتراكك الحالي ({coverageInfo.UncoveredRestaurantNameAr}) لا يدعم التوصيل إلى هذه المنطقة."
                : "لا توجد مطاعم في باقة اشتراكك الحالية تغطي هذه المنطقة.")
            : null;

        if (request.Page is > 0 && request.PageSize is > 0)
        {
            servingRestaurants = servingRestaurants.Skip((request.Page.Value - 1) * request.PageSize.Value).Take(request.PageSize.Value).ToList();
        }

        return new AddressCoveragePreviewDto(
            CanSetAsDefault: canSetAsDefault,
            IsCurrentSubscriptionRestaurantCovered: coverageInfo.IsCovered,
            UncoveredRestaurantNameAr: coverageInfo.UncoveredRestaurantNameAr,
            UncoveredRestaurantNameEn: coverageInfo.UncoveredRestaurantNameEn,
            ConflictMessage: conflictMsg,
            ResolvedRegionId: areaId,
            ResolvedRegionNameAr: location.Value.Area,
            ResolvedRegionNameEn: location.Value.AreaNameEn,
            Governorate: location.Value.Governorate,
            Area: location.Value.Area,
            Block: location.Value.Block,
            Latitude: lat,
            Longitude: lng,
            RestaurantCoverageNotice: notice,
            ServingRestaurants: servingRestaurants,
            HasActiveSubscription: coverageInfo.SubscriptionPoolRestaurantIds != null);
    }

    public async Task<Result<CustomerAddressDto>> Handle(SetDefaultCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        return await SetDefaultAsync(userId.Value, request.Id, ct);
    }

    public async Task<Result<CustomerAddressDto>> SetDefaultAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId, ct);
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");

        if (address.RegionId is { } areaId && areaId != Guid.Empty)
        {
            var coverageCheck = await ValidateActiveSubscriptionRestaurantCoverageAsync(userId, areaId, ct);
            if (coverageCheck.IsFailure) return coverageCheck.Error;
        }

        if (!address.IsDefault)
        {
            await ClearDefaultAsync(userId, ct);
            address.IsDefault = true;
        }

        await _deliveryAddresses.InitializeActiveDeliveryAsync(profile, address, ct);
        await _audit.LogAsync(new AuditEntry("CustomerAddressSetDefault", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);

        LocalizedTextDto? coverageNotice = null;
        IReadOnlyList<AddressServingRestaurantDto>? servingRestaurants = null;
        if (address.RegionId is { } targetAreaId && targetAreaId != Guid.Empty)
        {
            var coverageInfo = await ValidateActiveSubscriptionCoverageInfoAsync(userId, targetAreaId, ct);
            (coverageNotice, servingRestaurants) = await LoadServingRestaurantsAsync(targetAreaId, ct, coverageInfo.SubscriptionPoolRestaurantIds);
        }

        return Map(address, profile, coverageNotice, servingRestaurants);
    }

    private async Task ClearDefaultAsync(Guid userId, CancellationToken ct)
    {
        var defaults = await _db.CustomerAddresses.Where(x => x.UserId == userId && x.IsDefault).ToListAsync(ct);
        defaults.ForEach(x => x.IsDefault = false);
    }

    private Result<Guid> CurrentUserId()
    {
        if (_currentUser.UserId is not { } id)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        return _currentUser.UserType is UserType.Customer
            ? id
            : Error.Forbidden(MessageCodes.Auth.InvalidCredentials, "This endpoint is only available to customer accounts.");
    }

    private sealed record ResolvedLocation(
        Guid? AreaId,
        Guid? BlockId,
        string? Governorate,
        string? Area,
        string? Block,
        decimal? CentroidLatitude = null,
        decimal? CentroidLongitude = null,
        string? AreaNameEn = null,
        string? GovernorateNameEn = null);

    private async Task<Result<ResolvedLocation>> ResolveLocationAsync(
        Guid? regionId,
        Guid? blockRegionId,
        string? governorateName,
        string? areaName,
        string? blockName,
        CancellationToken ct)
    {
        if (!regionId.HasValue && !blockRegionId.HasValue)
        {
            var areaQuery = TrimOrNull(areaName);
            if (areaQuery is not null)
            {
                var matchedArea = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.IsActive && x.Type == RegionType.Area && (x.NameAr == areaQuery || x.NameEn == areaQuery || x.Code == areaQuery), ct);
                if (matchedArea is not null)
                {
                    regionId = matchedArea.Id;
                }
            }
        }

        if (!regionId.HasValue && !blockRegionId.HasValue)
            return new ResolvedLocation(null, null, TrimOrNull(governorateName), TrimOrNull(areaName), TrimOrNull(blockName), null, null);

        var ids = new[] { regionId, blockRegionId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var regions = await _db.Regions.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(ct);
        var area = regions.FirstOrDefault(x => x.Type == RegionType.Area);
        var block = regions.FirstOrDefault(x => x.Type == RegionType.Block);
        var governorate = regions.FirstOrDefault(x => x.Type == RegionType.Governorate);

        if (area is null && block is not null)
            area = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == block.ParentRegionId && x.IsActive && x.Type == RegionType.Area, ct);

        if (area is null && governorate is not null)
        {
            if (blockRegionId.HasValue)
                area = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == blockRegionId && x.IsActive && x.Type == RegionType.Area && x.ParentRegionId == governorate.Id, ct);
            var areaQuery = TrimOrNull(areaName);
            if (area is null && areaQuery is not null)
                area = await MatchChildRegionAsync(governorate.Id, RegionType.Area, areaQuery, ct);
            if (area is null)
                return Error.Validation("address.area_required", "Select a delivery area, not a governorate.");
        }

        if (area is null)
            return Error.Validation("address.area_invalid", "The selected area is invalid.");

        governorate ??= await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == area.ParentRegionId && x.IsActive && x.Type == RegionType.Governorate, ct);
        if (governorate is null)
            return Error.Validation("address.governorate_invalid", "The selected area's governorate is invalid.");

        if (block is null && blockRegionId.HasValue && blockRegionId != area.Id && blockRegionId != governorate.Id)
        {
            block = await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == blockRegionId && x.ParentRegionId == area.Id && x.IsActive && x.Type == RegionType.Block, ct);
            if (block is null)
                return Error.Validation("address.block_invalid", "The selected block does not belong to the selected area.");
        }

        var blockQuery = TrimOrNull(blockName);
        if (block is null && blockQuery is not null)
            block = await MatchChildRegionAsync(area.Id, RegionType.Block, blockQuery, ct);

        var centroidLat = block?.CentroidLatitude ?? area.CentroidLatitude ?? governorate?.CentroidLatitude;
        var centroidLng = block?.CentroidLongitude ?? area.CentroidLongitude ?? governorate?.CentroidLongitude;

        return new ResolvedLocation(
            area.Id,
            block?.Id,
            governorate?.NameAr,
            area.NameAr,
            block?.NameAr ?? blockQuery,
            centroidLat,
            centroidLng,
            area.NameEn,
            governorate?.NameEn);
    }

    private async Task<Region?> MatchChildRegionAsync(Guid parentId, RegionType type, string name, CancellationToken ct)
        => await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.ParentRegionId == parentId && x.IsActive && x.Type == type
            && (x.NameAr == name || x.NameEn == name || x.Code == name), ct);

    private sealed record SubscriptionCoverageValidationResult(
        bool IsCovered,
        string? UncoveredRestaurantNameAr = null,
        string? UncoveredRestaurantNameEn = null,
        Error? Error = null,
        IReadOnlyList<Guid>? SubscriptionPoolRestaurantIds = null);

    private async Task<SubscriptionCoverageValidationResult> ValidateActiveSubscriptionCoverageInfoAsync(
        Guid userId,
        Guid newAreaId,
        CancellationToken ct)
    {
        var activeStatuses = new[]
        {
            SubscriptionStatus.Active,
            SubscriptionStatus.Paid,
            SubscriptionStatus.Scheduled,
            SubscriptionStatus.Frozen,
            SubscriptionStatus.CancellationPending,
            SubscriptionStatus.Upgraded
        };

        var activeSubscriptionIds = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == userId && activeStatuses.Contains(x.Status))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (activeSubscriptionIds.Count == 0)
            return new SubscriptionCoverageValidationResult(IsCovered: true);

        var poolRestaurantIds = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .Where(s => activeSubscriptionIds.Contains(s.SubscriptionId))
            .Select(s => s.RestaurantId)
            .Distinct()
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var upcomingDays = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => activeSubscriptionIds.Contains(d.SubscriptionId)
                && d.ServiceDate >= today
                && d.Status != SubscriptionDayStatus.Delivered
                && d.Status != SubscriptionDayStatus.Cancelled)
            .Select(d => new { d.Id, d.RestaurantId })
            .ToListAsync(ct);

        var upcomingRestaurantIds = upcomingDays
            .Where(d => d.RestaurantId != null && d.RestaurantId != Guid.Empty)
            .Select(d => d.RestaurantId!.Value)
            .Distinct()
            .ToList();

        if (upcomingRestaurantIds.Count == 0 && upcomingDays.Count > 0)
        {
            var upcomingDayIds = upcomingDays.Select(d => d.Id).ToList();
            upcomingRestaurantIds = await _db.DailyRestaurantSelections.AsNoTracking()
                .Where(s => upcomingDayIds.Contains(s.SubscriptionDayId))
                .Select(s => s.RestaurantId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (upcomingRestaurantIds.Count == 0 && activeSubscriptionIds.Count > 0)
        {
            upcomingRestaurantIds = await _db.SubscriptionDayMealSnapshots.AsNoTracking()
                .Where(s => activeSubscriptionIds.Contains(s.SubscriptionId) && s.RestaurantId != Guid.Empty)
                .Select(s => s.RestaurantId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (upcomingRestaurantIds.Count == 0 && activeSubscriptionIds.Count > 0)
        {
            upcomingRestaurantIds = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
                .Where(s => activeSubscriptionIds.Contains(s.SubscriptionId) && s.IsInitiallySelected && s.RestaurantId != Guid.Empty)
                .Select(s => s.RestaurantId)
                .Distinct()
                .ToListAsync(ct);
        }

        if (poolRestaurantIds.Count == 0 && upcomingRestaurantIds.Count > 0)
        {
            poolRestaurantIds = upcomingRestaurantIds.ToList();
        }

        if (upcomingRestaurantIds.Count == 0)
        {
            if (poolRestaurantIds.Count > 0)
            {
                var poolCovered = await _db.RestaurantServiceRegions.AsNoTracking()
                    .AnyAsync(r => poolRestaurantIds.Contains(r.RestaurantId) && r.RegionId == newAreaId && r.IsActive, ct);
                if (!poolCovered)
                {
                    return new SubscriptionCoverageValidationResult(
                        IsCovered: false,
                        Error: Error.Conflict(
                            "address.restaurant_does_not_service_region",
                            "No restaurants in your current subscription package deliver to this area."),
                        SubscriptionPoolRestaurantIds: poolRestaurantIds);
                }
            }

            return new SubscriptionCoverageValidationResult(
                IsCovered: true,
                SubscriptionPoolRestaurantIds: poolRestaurantIds);
        }

        var coveredRestaurantIds = await _db.RestaurantServiceRegions.AsNoTracking()
            .Where(r => upcomingRestaurantIds.Contains(r.RestaurantId)
                && r.RegionId == newAreaId
                && r.IsActive)
            .Select(r => r.RestaurantId)
            .Distinct()
            .ToListAsync(ct);

        var missingCoverage = upcomingRestaurantIds.Except(coveredRestaurantIds).ToList();
        if (missingCoverage.Count > 0)
        {
            var uncoveredRestaurant = await _db.RestaurantProfiles.AsNoTracking()
                .Where(r => missingCoverage.Contains(r.Id))
                .Select(r => new { r.TradeNameAr, r.TradeNameEn })
                .FirstOrDefaultAsync(ct);

            var nameAr = uncoveredRestaurant?.TradeNameAr ?? "مطعم اشتراكك الحالي";
            var nameEn = uncoveredRestaurant?.TradeNameEn ?? "subscribed restaurant";

            return new SubscriptionCoverageValidationResult(
                IsCovered: false,
                UncoveredRestaurantNameAr: nameAr,
                UncoveredRestaurantNameEn: nameEn,
                Error: Error.Conflict(
                    "address.restaurant_does_not_service_region",
                    $"Cannot change address because your subscribed restaurant ({nameAr}) does not deliver to this new address."),
                SubscriptionPoolRestaurantIds: poolRestaurantIds);
        }

        return new SubscriptionCoverageValidationResult(
            IsCovered: true,
            SubscriptionPoolRestaurantIds: poolRestaurantIds);
    }

    private async Task<Result> ValidateActiveSubscriptionRestaurantCoverageAsync(
        Guid userId,
        Guid newAreaId,
        CancellationToken ct)
    {
        var info = await ValidateActiveSubscriptionCoverageInfoAsync(userId, newAreaId, ct);
        return info.IsCovered ? Result.Success() : info.Error!;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private async Task<(LocalizedTextDto Notice, IReadOnlyList<AddressServingRestaurantDto> Restaurants)> LoadServingRestaurantsAsync(
        Guid areaId, CancellationToken ct, IReadOnlyList<Guid>? poolRestaurantIds = null)
    {
        var notice = CustomerDeliveryAddressService.BuildRestaurantCoverageNotice();

        var query = _db.RestaurantServiceRegions.AsNoTracking()
            .Where(x => x.RegionId == areaId && x.IsActive);

        if (poolRestaurantIds is { Count: > 0 })
        {
            query = query.Where(x => poolRestaurantIds.Contains(x.RestaurantId));
        }

        var restaurantIds = await query
            .Select(x => x.RestaurantId)
            .Distinct()
            .ToListAsync(ct);

        if (restaurantIds.Count == 0)
            return (notice, Array.Empty<AddressServingRestaurantDto>());

        var restaurants = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => restaurantIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .ToListAsync(ct);

        if (restaurants.Count == 0)
            return (notice, Array.Empty<AddressServingRestaurantDto>());

        var activeIds = restaurants.Select(x => x.Id).ToList();

        var logoDocuments = await _db.Documents.AsNoTracking()
            .Where(x => activeIds.Contains(x.OwnerId)
                && x.OwnerType == DocumentOwnerType.Restaurant
                && x.DocumentType == DocumentType.ProfileImage)
            .Select(x => new { x.OwnerId, x.StoragePath })
            .ToListAsync(ct);
        var logos = logoDocuments.GroupBy(x => x.OwnerId).ToDictionary(x => x.Key, x => x.First().StoragePath);

        var ratingRows = await _db.RestaurantRatings.AsNoTracking()
            .Where(x => activeIds.Contains(x.RestaurantId))
            .Select(x => new { x.RestaurantId, x.Score })
            .ToListAsync(ct);
        var ratings = ratingRows.GroupBy(x => x.RestaurantId)
            .ToDictionary(x => x.Key, x => Math.Round((decimal)x.Average(r => r.Score), 1));

        var list = restaurants.Select(r => new AddressServingRestaurantDto(
            r.Id,
            r.TradeNameAr,
            r.TradeNameEn,
            logos.GetValueOrDefault(r.Id),
            ratings.TryGetValue(r.Id, out var rating) ? rating : null)).ToList();

        return (notice, list);
    }

    private static CustomerAddressDto Map(
        CustomerAddress address,
        CustomerProfile profile,
        LocalizedTextDto? restaurantCoverageNotice = null,
        IReadOnlyList<AddressServingRestaurantDto>? servingRestaurants = null)
        => new(
            address.Id,
            address.Label,
            address.AddressLine,
            address.RegionId,
            address.Latitude,
            address.Longitude,
            address.Notes,
            address.IsDefault,
            address.NotifyOnDelivery,
            address.Governorate,
            address.Area,
            address.Block,
            address.Street,
            address.Avenue,
            address.Building,
            address.Unit,
            address.Floor,
            address.BlockRegionId,
            IsDeliveryActive: profile.ActiveDeliveryAddressId == address.Id,
            DeliveryEffectiveFromUtc: profile.PendingDeliveryAddressId == address.Id
                ? profile.PendingDeliveryAddressEffectiveFromUtc
                : null,
            DeliveryChangeNotice: profile.PendingDeliveryAddressId == address.Id
                && profile.PendingDeliveryAddressEffectiveFromUtc is { } effectiveFrom
                ? CustomerDeliveryAddressService.BuildDeliveryChangeNotice(effectiveFrom)
                : null,
            RestaurantCoverageNotice: restaurantCoverageNotice,
            ServingRestaurants: servingRestaurants);
}
