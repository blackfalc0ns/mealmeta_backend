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
    string? DeviceId) : ICommand<DeviceTokenRegistrationResult>;

public sealed class RegisterCustomerDeviceTokenCommandHandler
    : ICommandHandler<RegisterCustomerDeviceTokenCommand, DeviceTokenRegistrationResult>
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

    public async Task<Result<DeviceTokenRegistrationResult>> Handle(
        RegisterCustomerDeviceTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");

        var token = request.Token.Trim();
        var now = _clock.UtcNow;
        var requestedInstallation = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId.Trim();

        // Serialize the whole read-modify-write per (owner, installation): two racing
        // registrations for one installation must never interleave their route swap.
        var gate = requestedInstallation is null
            ? null
            : VoiceCalls.Services.VoiceCallPushRouteRebinder.RegistrationGate(userId, requestedInstallation);
        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        try
        {
            return await RegisterWithConcurrencyRetryAsync(request, userId, requestedInstallation, now, cancellationToken);
        }
        finally
        {
            gate?.Release();
        }
    }

    private async Task<Result<DeviceTokenRegistrationResult>> RegisterWithConcurrencyRetryAsync(
        RegisterCustomerDeviceTokenCommand request,
        Guid userId,
        string? requestedInstallation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Cross-instance backstop: the token rows carry an xmin concurrency token, so
        // a commit whose snapshot was invalidated by a concurrent registration for the
        // same installation is rejected and retried from a fresh read.
        const int maxConcurrencyAttempts = 3;
        for (var attempt = 0; ; attempt++)
        {
            var existingByToken = await _db.CustomerDeviceTokens
                .FirstOrDefaultAsync(x => x.Token == request.Token.Trim(), cancellationToken);

            List<MealMate.Domain.VoiceCalls.VoiceCallDeviceSession> touchedSessions;
            List<CustomerDeviceToken> touchedRows;
            DeviceTokenRegistrationResult result;

            if (existingByToken is not null)
            {
                // The token row may be claimed by a new account: the previous owner's
                // voice sessions must stop targeting it before ownership moves.
                var detached = await VoiceCalls.Services.VoiceCallPushRouteRebinder.DetachRoutesClaimedByAnotherAccountAsync(
                    _db, existingByToken.Id, userId, now, cancellationToken);
                existingByToken.UserId = userId;
                existingByToken.Platform = request.Platform;
                existingByToken.DeviceId = request.DeviceId?.Trim();
                existingByToken.IsActive = true;
                existingByToken.LastSeenAtUtc = now;
                existingByToken.ModifiedAtUtc = now;
                var rebound = await VoiceCalls.Services.VoiceCallPushRouteRebinder.RebindLiveSessionsAsync(
                    _db, userId, requestedInstallation, request.Platform, existingByToken.Id, now, cancellationToken);
                touchedSessions = detached.Concat(rebound).ToList();
                touchedRows = [existingByToken];
                result = new DeviceTokenRegistrationResult(existingByToken.Id);
            }
            else
            {
                List<CustomerDeviceToken> superseded = [];
                if (!string.IsNullOrWhiteSpace(requestedInstallation))
                {
                    superseded = await _db.CustomerDeviceTokens
                        .Where(x => x.UserId == userId && x.DeviceId == requestedInstallation)
                        .ToListAsync(cancellationToken);

                    foreach (var row in superseded)
                    {
                        row.IsActive = false;
                        row.ModifiedAtUtc = now;
                    }
                }

                var deviceToken = new CustomerDeviceToken
                {
                    UserId = userId,
                    Token = request.Token.Trim(),
                    Platform = request.Platform,
                    DeviceId = requestedInstallation,
                    IsActive = true,
                    LastSeenAtUtc = now,
                    CreatedAtUtc = now,
                };
                _db.CustomerDeviceTokens.Add(deviceToken);

                // Same-atomic-save route swap: live sessions of this installation now point
                // at the new registry row, never at the deactivated predecessors.
                touchedSessions = await VoiceCalls.Services.VoiceCallPushRouteRebinder.RebindLiveSessionsAsync(
                    _db, userId, requestedInstallation, request.Platform, deviceToken.Id, now, cancellationToken);
                touchedRows = [deviceToken, .. superseded];
                result = new DeviceTokenRegistrationResult(deviceToken.Id);
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return Result.Success(result);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxConcurrencyAttempts - 1)
            {
                // A concurrent registration for this installation moved the same rows
                // between our read and commit: discard the stale snapshot and reread.
                foreach (var row in touchedRows)
                {
                    _db.DetachTrackedEntity(row);
                }

                foreach (var session in touchedSessions)
                {
                    _db.DetachTrackedEntity(session);
                }
            }
        }
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
