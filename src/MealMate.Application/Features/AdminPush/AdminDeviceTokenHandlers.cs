using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.AdminPush;

public sealed record RegisterAdminDeviceTokenCommand(
    string Token,
    MobilePlatform Platform,
    string? DeviceId) : ICommand;

public sealed class RegisterAdminDeviceTokenCommandHandler
    : ICommandHandler<RegisterAdminDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RegisterAdminDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        RegisterAdminDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var token = request.Token.Trim();
        var now = _clock.UtcNow;

        var existingByToken = await _db.AdminDeviceTokens
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
            var existingByDevice = await _db.AdminDeviceTokens
                .Where(x => x.UserId == userId && x.DeviceId == deviceId)
                .ToListAsync(cancellationToken);

            foreach (var row in existingByDevice)
            {
                row.IsActive = false;
                row.ModifiedAtUtc = now;
            }
        }

        _db.AdminDeviceTokens.Add(new AdminDeviceToken
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

public sealed class RegisterAdminDeviceTokenCommandValidator
    : AbstractValidator<RegisterAdminDeviceTokenCommand>
{
    public RegisterAdminDeviceTokenCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Platform).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(128);
    }
}

public sealed record UnregisterAdminDeviceTokenCommand(string Token) : ICommand;

public sealed class UnregisterAdminDeviceTokenCommandHandler
    : ICommandHandler<UnregisterAdminDeviceTokenCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UnregisterAdminDeviceTokenCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(
        UnregisterAdminDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var row = await _db.AdminDeviceTokens
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Token == request.Token.Trim(), cancellationToken);

        if (row is null)
            return Result.Success();

        row.IsActive = false;
        row.ModifiedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SendAdminTestPushCommand(string? DirectToken = null) : ICommand<AdminPushTestResultDto>;

public sealed class SendAdminTestPushCommandHandler
    : ICommandHandler<SendAdminTestPushCommand, AdminPushTestResultDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly IAdminPushTestService _testPush;

    public SendAdminTestPushCommandHandler(
        ICurrentUser currentUser,
        IAdminPushTestService testPush)
    {
        _currentUser = currentUser;
        _testPush = testPush;
    }

    public async Task<Result<AdminPushTestResultDto>> Handle(
        SendAdminTestPushCommand request,
        CancellationToken cancellationToken)
    {
        var directToken = string.IsNullOrWhiteSpace(request.DirectToken) ? null : request.DirectToken.Trim();
        if (_currentUser.UserId is not { } userId && directToken is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated and no direct device token provided.");

        return await _testPush.SendTestAsync(_currentUser.UserId, directToken, cancellationToken);
    }
}
