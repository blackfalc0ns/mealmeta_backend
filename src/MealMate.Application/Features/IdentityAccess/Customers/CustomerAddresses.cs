using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

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
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null,
    bool IsDeliveryActive = false,
    DateTime? DeliveryEffectiveFromUtc = null,
    LocalizedTextDto? DeliveryChangeNotice = null);

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
    string? Building,
    string? Unit,
    string? Floor,
    Guid? BlockRegionId = null) : ICommand<CustomerAddressDto>;

public sealed record DeleteCustomerAddressCommand(Guid Id) : ICommand;

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
        RuleFor(x => x.Building).MaximumLength(50).When(x => x.Building is not null);
        RuleFor(x => x.Unit).MaximumLength(50).When(x => x.Unit is not null);
        RuleFor(x => x.Floor).MaximumLength(50).When(x => x.Floor is not null);
        RuleFor(x => x.Latitude)
            .NotNull()
            .WithErrorCode(MessageCodes.Validation.RequiredField)
            .InclusiveBetween(KuwaitGeoBounds.MinLatitude, KuwaitGeoBounds.MaxLatitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Longitude)
            .NotNull()
            .WithErrorCode(MessageCodes.Validation.RequiredField)
            .InclusiveBetween(KuwaitGeoBounds.MinLongitude, KuwaitGeoBounds.MaxLongitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .Must((cmd, _) => KuwaitGeoBounds.IsWithin(cmd.Latitude, cmd.Longitude))
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .WithMessage("Coordinates must be inside Kuwait.");
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
        RuleFor(x => x.Building).MaximumLength(50).When(x => x.Building is not null);
        RuleFor(x => x.Unit).MaximumLength(50).When(x => x.Unit is not null);
        RuleFor(x => x.Floor).MaximumLength(50).When(x => x.Floor is not null);
        RuleFor(x => x.Latitude)
            .NotNull()
            .WithErrorCode(MessageCodes.Validation.RequiredField)
            .InclusiveBetween(KuwaitGeoBounds.MinLatitude, KuwaitGeoBounds.MaxLatitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Longitude)
            .NotNull()
            .WithErrorCode(MessageCodes.Validation.RequiredField)
            .InclusiveBetween(KuwaitGeoBounds.MinLongitude, KuwaitGeoBounds.MaxLongitude)
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .Must((cmd, _) => KuwaitGeoBounds.IsWithin(cmd.Latitude, cmd.Longitude))
            .WithErrorCode(MessageCodes.Validation.InvalidFormat)
            .WithMessage("Coordinates must be inside Kuwait.");
    }
}

public sealed class CustomerAddressHandlers :
    IQueryHandler<GetCustomerAddressesQuery, IReadOnlyList<CustomerAddressDto>>,
    ICommandHandler<CreateCustomerAddressCommand, CustomerAddressDto>,
    ICommandHandler<UpdateCustomerAddressCommand, CustomerAddressDto>,
    ICommandHandler<DeleteCustomerAddressCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;

    public CustomerAddressHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        ICustomerDeliveryAddressService deliveryAddresses)
        => (_db, _currentUser, _audit, _deliveryAddresses) = (db, currentUser, audit, deliveryAddresses);

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> Handle(GetCustomerAddressesQuery request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId.Value, ct);
        await _deliveryAddresses.TryApplyPendingAsync(profile, ct);
        var items = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId.Value)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label)
            .ToListAsync(ct);
        return items.Select(x => Map(x, profile)).ToList();
    }

    public async Task<Result<CustomerAddressDto>> Handle(CreateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        if (!KuwaitGeoBounds.IsWithin(request.Latitude, request.Longitude))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "Latitude and longitude are required and must be inside Kuwait.");
        }

        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId.Value, ct);
        var location = await ResolveLocationAsync(request.RegionId, request.BlockRegionId, request.Governorate, request.Area, request.Block, ct);
        if (location.IsFailure) return location.Error;
        var hadAddresses = _db.CustomerAddresses.Local.Any(x => x.UserId == userId.Value)
            || await _db.CustomerAddresses.AnyAsync(x => x.UserId == userId.Value, ct);
        var makeDefault = request.IsDefault || !hadAddresses;
        if (makeDefault) await ClearDefaultAsync(userId.Value, ct);

        var address = new CustomerAddress
        {
            UserId = userId.Value, Label = request.Label.Trim(), AddressLine = request.AddressLine.Trim(),
            RegionId = location.Value.AreaId, Latitude = request.Latitude, Longitude = request.Longitude,
            Notes = request.Notes?.Trim(), IsDefault = makeDefault, NotifyOnDelivery = request.NotifyOnDelivery,
            Governorate = location.Value.Governorate, Area = location.Value.Area, Block = location.Value.Block, BlockRegionId = location.Value.BlockId,
            Street = request.Street?.Trim(), Building = request.Building?.Trim(), Unit = request.Unit?.Trim(), Floor = request.Floor?.Trim(),
        };
        _db.CustomerAddresses.Add(address);

        if (!hadAddresses || !profile.ActiveDeliveryAddressId.HasValue)
            await _deliveryAddresses.InitializeActiveDeliveryAsync(profile, address, ct);
        else if (makeDefault)
            await _deliveryAddresses.ScheduleDeliveryChangeAsync(profile, address.Id, ct);

        await _audit.LogAsync(new AuditEntry("CustomerAddressCreated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Map(address, profile);
    }

    public async Task<Result<CustomerAddressDto>> Handle(UpdateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        if (!KuwaitGeoBounds.IsWithin(request.Latitude, request.Longitude))
        {
            return Error.Validation(
                MessageCodes.Validation.InvalidFormat,
                "Latitude and longitude are required and must be inside Kuwait.");
        }

        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId.Value, ct);
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == request.Id && x.UserId == userId.Value, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");
        var location = await ResolveLocationAsync(request.RegionId, request.BlockRegionId, request.Governorate, request.Area, request.Block, ct);
        if (location.IsFailure) return location.Error;

        var becomingDefault = request.IsDefault && !address.IsDefault;
        var wasActiveDelivery = profile.ActiveDeliveryAddressId == address.Id;
        var contentChanged = CustomerDeliveryAddressService.HasDeliveryContentChanged(address, request);

        if (becomingDefault) await ClearDefaultAsync(userId.Value, ct);

        address.Label = request.Label.Trim(); address.AddressLine = request.AddressLine.Trim();
        address.RegionId = location.Value.AreaId; address.Latitude = request.Latitude; address.Longitude = request.Longitude;
        address.Notes = request.Notes?.Trim(); address.IsDefault = request.IsDefault; address.NotifyOnDelivery = request.NotifyOnDelivery;
        address.Governorate = location.Value.Governorate; address.Area = location.Value.Area; address.Block = location.Value.Block; address.BlockRegionId = location.Value.BlockId;
        address.Street = request.Street?.Trim(); address.Building = request.Building?.Trim(); address.Unit = request.Unit?.Trim(); address.Floor = request.Floor?.Trim();

        if (becomingDefault)
        {
            if (!profile.ActiveDeliveryAddressId.HasValue)
                await _deliveryAddresses.InitializeActiveDeliveryAsync(profile, address, ct);
            else if (profile.ActiveDeliveryAddressId != address.Id)
                await _deliveryAddresses.ScheduleDeliveryChangeAsync(profile, address.Id, ct);
        }
        else if ((wasActiveDelivery || profile.PendingDeliveryAddressId == address.Id) && contentChanged)
            await _deliveryAddresses.ScheduleDeliveryChangeAsync(profile, address.Id, ct);

        await _audit.LogAsync(new AuditEntry("CustomerAddressUpdated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Map(address, profile);
    }

    public async Task<Result> Handle(DeleteCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        var profile = await _deliveryAddresses.GetOrCreateProfileAsync(userId.Value, ct);
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == request.Id && x.UserId == userId.Value, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");

        if (address.IsDefault || profile.ActiveDeliveryAddressId == address.Id)
            return Error.Conflict(
                "address.active_delivery_cannot_delete",
                "Cannot delete the default or currently active delivery address. Set another default address and wait 72 hours for it to activate, then delete the old one.");

        if (profile.PendingDeliveryAddressId == address.Id)
        {
            profile.PendingDeliveryAddressId = null;
            profile.PendingDeliveryAddressEffectiveFromUtc = null;
        }

        _db.CustomerAddresses.Remove(address);
        await _audit.LogAsync(new AuditEntry("CustomerAddressDeleted", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Result.Success();
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

    private sealed record ResolvedLocation(Guid? AreaId, Guid? BlockId, string? Governorate, string? Area, string? Block);

    private async Task<Result<ResolvedLocation>> ResolveLocationAsync(
        Guid? regionId,
        Guid? blockRegionId,
        string? governorateName,
        string? areaName,
        string? blockName,
        CancellationToken ct)
    {
        if (!regionId.HasValue && !blockRegionId.HasValue)
            return new ResolvedLocation(null, null, TrimOrNull(governorateName), TrimOrNull(areaName), TrimOrNull(blockName));

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

        return new ResolvedLocation(area.Id, block?.Id, governorate.NameAr, area.NameAr, block?.NameAr ?? blockQuery);
    }

    private async Task<Region?> MatchChildRegionAsync(Guid parentId, RegionType type, string name, CancellationToken ct)
        => await _db.Regions.AsNoTracking().FirstOrDefaultAsync(x =>
            x.ParentRegionId == parentId && x.IsActive && x.Type == type
            && (x.NameAr == name || x.NameEn == name || x.Code == name), ct);

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static CustomerAddressDto Map(CustomerAddress address, CustomerProfile profile)
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
                : null);
}
