using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public interface IFamilyMemberFirstLoginService
{
    Task<bool> IsPendingSetupAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<LoginResultDto>> BeginAsync(UserAccountDto user, IReadOnlyList<string> roles, string? deviceInfo,
        string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResultDto>> VerifyOtpAsync(string challengeToken, string code, string? deviceInfo,
        string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AuthResponseDto>> SetPasswordAsync(string challengeToken, string newPassword, string? deviceInfo,
        string? ipAddress, CancellationToken cancellationToken = default);
}

public sealed class FamilyMemberFirstLoginService(
    IAppDbContext db,
    IUserAccountService users,
    IOtpService otp,
    IAuthLoginChallengeService challenges,
    IAuthSessionService sessions,
    IAuditLogger audit,
    IUnitOfWork unitOfWork,
    IApplicationTransactionExecutor? transactions = null,
    IApplicationLockManager? locks = null) : IFamilyMemberFirstLoginService
{
    public Task<bool> IsPendingSetupAsync(Guid userId, CancellationToken cancellationToken = default)
        => db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking().AnyAsync(access => access.LinkedCustomerUserId == userId
            && access.RequiresPasswordSetup, cancellationToken);

    public Task<Result<LoginResultDto>> BeginAsync(UserAccountDto user, IReadOnlyList<string> roles,
        string? deviceInfo, string? ipAddress, CancellationToken cancellationToken = default)
        => RunUserTransactionAsync(user.Id,
            ct => BeginCoreAsync(user, roles, deviceInfo, ipAddress, ct), cancellationToken);

    private async Task<Result<LoginResultDto>> BeginCoreAsync(UserAccountDto user, IReadOnlyList<string> roles,
        string? deviceInfo, string? ipAddress, CancellationToken cancellationToken)
    {
        if (user.UserType != UserType.Customer || user.AccountStatus != AccountStatus.Active)
            return Error.Forbidden(MessageCodes.Family.PasswordSetupRequired);

        var access = await FindPendingAccessAsync(user.Id, cancellationToken);
        if (access is null || !access.HasSeparateAccount || access.IsPaused)
            return Error.Forbidden(access?.IsPaused == true ? "family.access_paused" : "family.password_setup_required");
        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Error.Conflict("family.member_phone_required");

        var otpResult = await otp.SendAsync(new OtpSendRequest(
            user.PhoneNumber,
            "WhatsApp",
            OtpPurposes.FamilyMemberFirstLogin,
            user.Id), cancellationToken);
        if (otpResult.IsFailure) return otpResult.Error;

        var challenge = await challenges.CreateAsync(new CreateLoginChallengeRequest(
            user.Id, LoginChallengeSteps.FamilyVerifyOtp, deviceInfo, ipAddress), cancellationToken);
        await audit.LogAsync(new AuditEntry("FamilyMemberFirstLoginOtpRequested", "User", user.Id,
            After: new { Purpose = OtpPurposes.FamilyMemberFirstLogin }, SourceChannel: "customer-app"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return LoginResultDto.FromPendingChallenge(user, roles, challenge.RawToken,
            LoginChallengeSteps.FamilyVerifyOtp, challenge.ExpiresAtUtc);
    }

    public Task<Result<LoginResultDto>> VerifyOtpAsync(string challengeToken, string code, string? deviceInfo,
        string? ipAddress, CancellationToken cancellationToken = default)
        => RunLockedTransactionAsync(challengeToken, LoginChallengeSteps.FamilyVerifyOtp,
            ct => VerifyOtpCoreAsync(challengeToken, code, deviceInfo, ipAddress, ct), cancellationToken);

    private async Task<Result<LoginResultDto>> VerifyOtpCoreAsync(string challengeToken, string code, string? deviceInfo,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var resolved = await challenges.ResolveAsync(challengeToken, LoginChallengeSteps.FamilyVerifyOtp, cancellationToken);
        if (resolved.IsFailure) return InvalidChallenge();

        var user = await users.FindByIdAsync(resolved.Value.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Customer || user.AccountStatus != AccountStatus.Active
            || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return Error.Forbidden(MessageCodes.Family.PasswordSetupRequired);

        var access = await FindPendingAccessAsync(user.Id, cancellationToken);
        if (access is null || !access.HasSeparateAccount || access.IsPaused)
            return Error.Forbidden(access?.IsPaused == true ? "family.access_paused" : "family.password_setup_required");

        var otpResult = await otp.VerifyAsync(new OtpVerifyRequest(
            user.PhoneNumber, OtpPurposes.FamilyMemberFirstLogin, code.Trim()), cancellationToken);
        if (otpResult.IsFailure)
        {
            await challenges.RecordFailedAttemptAsync(resolved.Value, cancellationToken);
            return otpResult.Error;
        }

        var consumed = await challenges.TryConsumeAsync(resolved.Value, cancellationToken);
        if (consumed.IsFailure) return InvalidChallenge();

        await users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);
        var setPassword = await challenges.CreateAsync(new CreateLoginChallengeRequest(
            user.Id, LoginChallengeSteps.FamilySetPassword, deviceInfo, ipAddress), cancellationToken);
        await audit.LogAsync(new AuditEntry("FamilyMemberFirstLoginOtpVerified", "User", user.Id,
            SourceChannel: "customer-app"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var updatedUser = user with { PhoneNumberConfirmed = true };
        return LoginResultDto.FromPendingChallenge(updatedUser,
            await users.GetRolesAsync(user.Id, cancellationToken), setPassword.RawToken,
            LoginChallengeSteps.FamilySetPassword, setPassword.ExpiresAtUtc);
    }

    public Task<Result<AuthResponseDto>> SetPasswordAsync(string challengeToken, string newPassword,
        string? deviceInfo, string? ipAddress, CancellationToken cancellationToken = default)
        => RunLockedTransactionAsync(challengeToken, LoginChallengeSteps.FamilySetPassword,
            ct => SetPasswordCoreAsync(challengeToken, newPassword, deviceInfo, ipAddress, ct), cancellationToken);

    private async Task<Result<AuthResponseDto>> SetPasswordCoreAsync(string challengeToken, string newPassword,
        string? deviceInfo, string? ipAddress, CancellationToken cancellationToken)
    {
        var resolved = await challenges.ResolveAsync(challengeToken, LoginChallengeSteps.FamilySetPassword, cancellationToken);
        if (resolved.IsFailure) return InvalidChallenge();

        var user = await users.FindByIdAsync(resolved.Value.UserId, cancellationToken);
        if (user is null || user.UserType != UserType.Customer || user.AccountStatus != AccountStatus.Active)
            return Error.Forbidden(MessageCodes.Family.PasswordSetupRequired);
        var access = await FindPendingAccessAsync(user.Id, cancellationToken);
        if (access is null || !access.HasSeparateAccount || access.IsPaused)
            return Error.Forbidden(access?.IsPaused == true ? "family.access_paused" : "family.password_setup_required");

        var consumed = await challenges.TryConsumeAsync(resolved.Value, cancellationToken);
        if (consumed.IsFailure) return InvalidChallenge();

        var resetToken = await users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var reset = await users.ResetPasswordAsync(user.Id, resetToken, newPassword, cancellationToken);
        if (reset.IsFailure) return reset.Error;

        access.RequiresPasswordSetup = false;
        await users.SetHasPasswordAsync(user.Id, true, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sessions.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var session = await sessions.IssueSessionAsync(user.Id, deviceInfo, ipAddress, cancellationToken);
        if (session.IsFailure) return session.Error;

        await audit.LogAsync(new AuditEntry("FamilyMemberFirstLoginCompleted", "User", user.Id,
            SourceChannel: "customer-app"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AuthMapping.ToResponse(session.Value);
    }

    private Task<FamilyMemberAccess?> FindPendingAccessAsync(Guid userId, CancellationToken cancellationToken)
        => db.FamilyMemberAccesses
            .Include(access => access.FamilyMember)
            .ThenInclude(member => member.FamilyGroup)
            .FirstOrDefaultAsync(access => access.LinkedCustomerUserId == userId
                && access.RequiresPasswordSetup && access.RemovedAtUtc == null
                && access.FamilyMember.Role == FamilyMemberRole.Member && !access.FamilyMember.IsDeleted
                && !access.FamilyMember.FamilyGroup.IsDeleted
                && access.FamilyMember.FamilyGroup.Status == FamilyGroupStatus.Active
                && !access.IsPaused, cancellationToken);

    private async Task<Result<T>> RunLockedTransactionAsync<T>(string challengeToken, string step,
        Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        if (transactions is null) return await action(cancellationToken);

        var resolved = await challenges.ResolveAsync(challengeToken, step, cancellationToken);
        if (resolved.IsFailure) return InvalidChallenge();

        async Task<Result<T>> RunLocked(CancellationToken transactionToken)
        {
            if (locks is not null)
            {
                var groupId = await db.FamilyMemberAccesses.AsNoTracking()
                    .Where(access => access.LinkedCustomerUserId == resolved.Value.UserId && access.RequiresPasswordSetup)
                    .Select(access => access.FamilyMember.FamilyGroupId)
                    .FirstOrDefaultAsync(transactionToken);
                if (groupId != Guid.Empty) await locks.LockFamilyGroupAsync(groupId, transactionToken);
            }
            return await action(transactionToken);
        }

        // Failed OTP attempts are state changes that must be retained. Other failed
        // setup operations use ExecuteResultAsync so password/challenge writes roll back.
        return step == LoginChallengeSteps.FamilyVerifyOtp
            ? await transactions.ExecuteAsync(RunLocked, cancellationToken)
            : await transactions.ExecuteResultAsync(RunLocked, cancellationToken);
    }

    private async Task<Result<T>> RunUserTransactionAsync<T>(Guid userId,
        Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken)
    {
        if (transactions is null) return await action(cancellationToken);

        return await transactions.ExecuteResultAsync(async transactionToken =>
        {
            if (locks is not null)
            {
                var groupId = await db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
                    .Where(access => access.LinkedCustomerUserId == userId && access.RequiresPasswordSetup)
                    .Select(access => access.FamilyMember.FamilyGroupId)
                    .FirstOrDefaultAsync(transactionToken);
                if (groupId != Guid.Empty) await locks.LockFamilyGroupAsync(groupId, transactionToken);
            }
            return await action(transactionToken);
        }, cancellationToken);
    }

    private static Error InvalidChallenge() => Error.Validation(MessageCodes.Auth.FamilyFirstLoginChallengeInvalid);
}

public sealed record VerifyFamilyMemberFirstLoginOtpCommand(
    string LoginChallengeToken, string Code, string? DeviceInfo = null, string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class VerifyFamilyMemberFirstLoginOtpCommandValidator : AbstractValidator<VerifyFamilyMemberFirstLoginOtpCommand>
{
    public VerifyFamilyMemberFirstLoginOtpCommandValidator()
    {
        RuleFor(request => request.LoginChallengeToken).NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(request => request.Code).NotEmpty().WithMessageCode(MessageCodes.Validation.OtpCodeInvalid)
            .Length(6).WithMessageCode(MessageCodes.Validation.OtpCodeInvalid);
    }
}

public sealed class VerifyFamilyMemberFirstLoginOtpCommandHandler(IFamilyMemberFirstLoginService flow)
    : ICommandHandler<VerifyFamilyMemberFirstLoginOtpCommand, LoginResultDto>
{
    public Task<Result<LoginResultDto>> Handle(VerifyFamilyMemberFirstLoginOtpCommand request, CancellationToken cancellationToken)
        => flow.VerifyOtpAsync(request.LoginChallengeToken, request.Code, request.DeviceInfo, request.IpAddress, cancellationToken);
}

public sealed record SetFamilyMemberFirstLoginPasswordCommand(
    string LoginChallengeToken, string NewPassword, string? DeviceInfo = null, string? IpAddress = null) : ICommand<AuthResponseDto>;

public sealed class SetFamilyMemberFirstLoginPasswordCommandValidator : AbstractValidator<SetFamilyMemberFirstLoginPasswordCommand>
{
    public SetFamilyMemberFirstLoginPasswordCommandValidator()
    {
        RuleFor(request => request.LoginChallengeToken).NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(request => request.NewPassword).NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField)
            .MinimumLength(8)
            .WithMessageCode(MessageCodes.Validation.PasswordMinimumLength)
            .Matches("[A-Za-z]").WithMessageCode(MessageCodes.Auth.PasswordTooWeak)
            .Matches("[0-9]").WithMessageCode(MessageCodes.Auth.PasswordTooWeak);
    }
}

public sealed class SetFamilyMemberFirstLoginPasswordCommandHandler(IFamilyMemberFirstLoginService flow)
    : ICommandHandler<SetFamilyMemberFirstLoginPasswordCommand, AuthResponseDto>
{
    public Task<Result<AuthResponseDto>> Handle(SetFamilyMemberFirstLoginPasswordCommand request, CancellationToken cancellationToken)
        => flow.SetPasswordAsync(request.LoginChallengeToken, request.NewPassword, request.DeviceInfo, request.IpAddress, cancellationToken);
}
