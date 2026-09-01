using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerPush;

public sealed record RegisterCustomerDeviceTokenCommand(
    string Token,
    MobilePlatform Platform,
    string? DeviceId) : ICommand;

public sealed class RegisterCustomerDeviceTokenCommandHandler
    : ICommandHandler<RegisterCustomerDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RegisterCustomerDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        RegisterCustomerDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var token = request.Token.Trim();
        var now = _clock.UtcNow;

        var existingByToken = await _db.CustomerDeviceTokens
            .FirstOrDefaultAsync(x => x.Token == token, cancellationToken);

        if (existingByToken is not null)
        {
            existingByToken.UserId = userId;
            existingByToken.Platform = request.Platform;
            existingByToken.DeviceId = request.DeviceId?.Trim();
            existingByToken.IsActive = true;
            existingByToken.LastSeenAtUtc = now;
            existingByToken.ModifiedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            var deviceId = request.DeviceId.Trim();
            var existingByDevice = await _db.CustomerDeviceTokens
                .Where(x => x.UserId == userId && x.DeviceId == deviceId)
                .ToListAsync(cancellationToken);

            foreach (var row in existingByDevice)
            {
                row.IsActive = false;
                row.ModifiedAtUtc = now;
            }
        }

        _db.CustomerDeviceTokens.Add(new CustomerDeviceToken
        {
            UserId = userId,
            Token = token,
            Platform = request.Platform,
            DeviceId = request.DeviceId?.Trim(),
            IsActive = true,
            LastSeenAtUtc = now,
            CreatedAtUtc = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class RegisterCustomerDeviceTokenCommandValidator
    : AbstractValidator<RegisterCustomerDeviceTokenCommand>
{
    public RegisterCustomerDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(128);
    }
}

public sealed record UnregisterCustomerDeviceTokenCommand(string Token) : ICommand;

public sealed class UnregisterCustomerDeviceTokenCommandHandler
    : ICommandHandler<UnregisterCustomerDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UnregisterCustomerDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        UnregisterCustomerDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var row = await _db.CustomerDeviceTokens
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == request.Token.Trim(), cancellationToken);

        if (row is null)
            return Result.Success();

        row.IsActive = false;
        row.ModifiedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class UnregisterCustomerDeviceTokenCommandValidator
    : AbstractValidator<UnregisterCustomerDeviceTokenCommand>
{
    public UnregisterCustomerDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
    }
}
