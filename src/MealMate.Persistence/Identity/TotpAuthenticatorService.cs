using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MealMate.Persistence.Identity;

public sealed class TotpAuthenticatorService : ITotpAuthenticatorService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TotpOptions _options;

    public TotpAuthenticatorService(
        UserManager<ApplicationUser> userManager,
        IOptions<TotpOptions> options)
    {
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user?.TwoFactorEnabled == true;
    }

    public async Task<TotpSetupDto> GetOrCreateSetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user)
                  ?? throw new InvalidOperationException("Authenticator key was not created.");
        }

        var accountName = user.Email ?? user.UserName ?? user.Id.ToString();
        return new TotpSetupDto(
            FormatSharedKey(key),
            BuildAuthenticatorUri(user, key),
            _options.Issuer,
            accountName);
    }

    public async Task<bool> VerifyCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var normalized = TotpCodeNormalizer.Normalize(code);
        if (!string.IsNullOrWhiteSpace(_options.FixedCode)
            && string.Equals(normalized, _options.FixedCode, StringComparison.Ordinal))
        {
            return true;
        }

        return await _userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            normalized);
    }

    public async Task<Result<IReadOnlyList<string>>> CompleteEnrollmentAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        if (user.TwoFactorEnabled)
        {
            return Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "TOTP is already enabled.");
        }

        var enabled = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return Error.Validation(
                MessageCodes.Auth.TotpEnrollmentFailed,
                "Unable to complete authenticator enrollment. Sign in again to retry.");
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return (recoveryCodes ?? Array.Empty<string>()).ToList();
    }

    public async Task<bool> RedeemRecoveryCodeAsync(Guid userId, string recoveryCode, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var normalized = recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, normalized);
        return result.Succeeded;
    }

    public async Task ResetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId);
        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
    }

    private async Task<ApplicationUser> RequireUserAsync(Guid userId)
    {
        return await _userManager.FindByIdAsync(userId.ToString())
               ?? throw new InvalidOperationException($"User {userId} not found.");
    }

    private string BuildAuthenticatorUri(ApplicationUser user, string unformattedKey)
    {
        var issuer = Uri.EscapeDataString(_options.Issuer);
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        return $"otpauth://totp/{issuer}:{account}?secret={unformattedKey}&issuer={issuer}&digits=6";
    }

    private static string FormatSharedKey(string unformattedKey)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < unformattedKey.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(unformattedKey[i]));
        }

        return builder.ToString();
    }
}
