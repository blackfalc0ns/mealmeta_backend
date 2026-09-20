using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantPush;

public sealed record RegisterRestaurantDeviceTokenCommand(
    string Token,
    MobilePlatform Platform,
    string? DeviceId) : ICommand;

public sealed class RegisterRestaurantDeviceTokenCommandHandler
    : ICommandHandler<RegisterRestaurantDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RegisterRestaurantDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        RegisterRestaurantDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var restaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var token = request.Token.Trim();
        var now = _clock.UtcNow;

        var existingByToken = await _db.RestaurantDeviceTokens
            .FirstOrDefaultAsync(x => x.Token == token, cancellationToken);

        if (existingByToken is not null)
        {
            existingByToken.UserId = userId;
            existingByToken.RestaurantId = restaurantId ?? existingByToken.RestaurantId;
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
            var existingByDevice = await _db.RestaurantDeviceTokens
                .Where(x => x.UserId == userId && x.DeviceId == deviceId)
                .ToListAsync(cancellationToken);

            foreach (var row in existingByDevice)
            {
                row.IsActive = false;
                row.ModifiedAtUtc = now;
            }
        }

        _db.RestaurantDeviceTokens.Add(new RestaurantDeviceToken
        {
            UserId = userId,
            RestaurantId = restaurantId,
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

public sealed class RegisterRestaurantDeviceTokenCommandValidator
    : AbstractValidator<RegisterRestaurantDeviceTokenCommand>
{
    public RegisterRestaurantDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(128);
    }
}

public sealed record UnregisterRestaurantDeviceTokenCommand(string Token) : ICommand;

public sealed class UnregisterRestaurantDeviceTokenCommandHandler
    : ICommandHandler<UnregisterRestaurantDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UnregisterRestaurantDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        UnregisterRestaurantDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var row = await _db.RestaurantDeviceTokens
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == request.Token.Trim(), cancellationToken);

        if (row is null)
            return Result.Success();

        row.IsActive = false;
        row.ModifiedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SendRestaurantTestPushCommand(string? DirectToken = null) : ICommand<RestaurantPushTestResultDto>;

public sealed class SendRestaurantTestPushCommandHandler
    : ICommandHandler<SendRestaurantTestPushCommand, RestaurantPushTestResultDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantPushTestService _testPush;

    public SendRestaurantTestPushCommandHandler(
        ICurrentUser currentUser,
        IRestaurantPushTestService testPush)
    {
        _currentUser = currentUser;
        _testPush = testPush;
    }

    public async Task<Result<RestaurantPushTestResultDto>> Handle(
        SendRestaurantTestPushCommand request,
        CancellationToken cancellationToken)
    {
        var directToken = string.IsNullOrWhiteSpace(request.DirectToken) ? null : request.DirectToken.Trim();
        if (_currentUser.UserId is not { } userId && directToken is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated and no direct device token provided.");

        return await _testPush.SendTestAsync(_currentUser.UserId, directToken, cancellationToken);
    }
}
