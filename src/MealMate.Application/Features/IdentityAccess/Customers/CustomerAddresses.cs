using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Enums;
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
    string? Floor);

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
    string? Floor) : ICommand<CustomerAddressDto>;

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
    string? Floor) : ICommand<CustomerAddressDto>;

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
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m).When(x => x.Longitude.HasValue);
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
        RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180m, 180m).When(x => x.Longitude.HasValue);
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

    public CustomerAddressHandlers(IAppDbContext db, ICurrentUser currentUser, IAuditLogger audit)
        => (_db, _currentUser, _audit) = (db, currentUser, audit);

    public async Task<Result<IReadOnlyList<CustomerAddressDto>>> Handle(GetCustomerAddressesQuery request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;

        var items = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId.Value)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Label)
            .Select(x => Map(x))
            .ToListAsync(ct);
        return items;
    }

    public async Task<Result<CustomerAddressDto>> Handle(CreateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        var makeDefault = request.IsDefault || !await _db.CustomerAddresses.AnyAsync(x => x.UserId == userId.Value, ct);
        if (makeDefault) await ClearDefaultAsync(userId.Value, ct);

        var address = new CustomerAddress
        {
            UserId = userId.Value, Label = request.Label.Trim(), AddressLine = request.AddressLine.Trim(),
            RegionId = request.RegionId, Latitude = request.Latitude, Longitude = request.Longitude,
            Notes = request.Notes?.Trim(), IsDefault = makeDefault, NotifyOnDelivery = request.NotifyOnDelivery,
            Governorate = request.Governorate?.Trim(), Area = request.Area?.Trim(), Block = request.Block?.Trim(),
            Street = request.Street?.Trim(), Building = request.Building?.Trim(), Unit = request.Unit?.Trim(), Floor = request.Floor?.Trim(),
        };
        _db.CustomerAddresses.Add(address);
        await _audit.LogAsync(new AuditEntry("CustomerAddressCreated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Map(address);
    }

    public async Task<Result<CustomerAddressDto>> Handle(UpdateCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == request.Id && x.UserId == userId.Value, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");

        if (request.IsDefault && !address.IsDefault) await ClearDefaultAsync(userId.Value, ct);
        address.Label = request.Label.Trim(); address.AddressLine = request.AddressLine.Trim();
        address.RegionId = request.RegionId; address.Latitude = request.Latitude; address.Longitude = request.Longitude;
        address.Notes = request.Notes?.Trim(); address.IsDefault = request.IsDefault; address.NotifyOnDelivery = request.NotifyOnDelivery;
        address.Governorate = request.Governorate?.Trim(); address.Area = request.Area?.Trim(); address.Block = request.Block?.Trim();
        address.Street = request.Street?.Trim(); address.Building = request.Building?.Trim(); address.Unit = request.Unit?.Trim(); address.Floor = request.Floor?.Trim();
        await _audit.LogAsync(new AuditEntry("CustomerAddressUpdated", nameof(CustomerAddress), address.Id, SourceChannel: "customer-app"), ct);
        return Map(address);
    }

    public async Task<Result> Handle(DeleteCustomerAddressCommand request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.IsFailure) return userId.Error;
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == request.Id && x.UserId == userId.Value, ct);
        if (address is null) return Error.NotFound(MessageCodes.Auth.UserNotFound, "Address not found.");
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

    private static CustomerAddressDto Map(CustomerAddress x) => new(x.Id, x.Label, x.AddressLine, x.RegionId, x.Latitude, x.Longitude, x.Notes, x.IsDefault, x.NotifyOnDelivery, x.Governorate, x.Area, x.Block, x.Street, x.Building, x.Unit, x.Floor);
}
