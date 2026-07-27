using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class TotpAuthenticatorServiceTests : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TotpAuthenticatorService _totp;

    public TotpAuthenticatorServiceTests()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddDataProtection();
        services.AddLogging();
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddSingleton<IDomainEventDispatcher>(_ => Substitute.For<IDomainEventDispatcher>());
        services.AddOptions<TotpOptions>().Configure(o => o.Issuer = "MealMate Admin");

        var provider = services.BuildServiceProvider();
        _scope = provider.CreateScope();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _totp = new TotpAuthenticatorService(
            _userManager,
            _scope.ServiceProvider.GetRequiredService<IOptions<TotpOptions>>());
    }

    public void Dispose() => _scope.Dispose();

    private async Task<ApplicationUser> CreateAdminAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "admin@test.com",
            Email = "admin@test.com",
            FullName = "Admin",
            UserType = UserType.Admin,
            AccountStatus = AccountStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            EmailConfirmed = true,
            LockoutEnabled = true,
        };
        (await _userManager.CreateAsync(user, "Password1!Aa")).Succeeded.Should().BeTrue();
        return user;
    }

    [Fact]
    public async Task GetOrCreateSetup_is_idempotent_and_builds_otpauth_uri()
    {
        var user = await CreateAdminAsync();

        var first = await _totp.GetOrCreateSetupAsync(user.Id, CancellationToken.None);
        var second = await _totp.GetOrCreateSetupAsync(user.Id, CancellationToken.None);

        first.SharedKey.Should().NotBeNullOrWhiteSpace();
        first.SharedKey.Should().Be(second.SharedKey);
        first.AuthenticatorUri.Should().Contain("otpauth://totp/");
        first.AuthenticatorUri.Should().Contain("MealMate%20Admin");
        first.AuthenticatorUri.Should().Contain("admin%40test.com");
    }

    [Fact]
    public async Task CompleteEnrollmentAsync_enables_two_factor_and_generates_recovery_codes()
    {
        var user = await CreateAdminAsync();
        await _totp.GetOrCreateSetupAsync(user.Id, CancellationToken.None);

        var enrollment = await _totp.CompleteEnrollmentAsync(user.Id, CancellationToken.None);
        enrollment.IsSuccess.Should().BeTrue();
        enrollment.Value.Should().HaveCount(10);
        (await _totp.IsEnabledAsync(user.Id, CancellationToken.None)).Should().BeTrue();

        var secondAttempt = await _totp.CompleteEnrollmentAsync(user.Id, CancellationToken.None);
        secondAttempt.IsFailure.Should().BeTrue();
        secondAttempt.Error.Code.Should().Be(MessageCodes.Auth.TotpSetupNotAllowed);
    }

    [Fact]
    public async Task RedeemRecoveryCode_is_single_use()
    {
        var user = await CreateAdminAsync();
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.ToList();
        codes.Should().HaveCount(2);

        (await _totp.RedeemRecoveryCodeAsync(user.Id, codes[0], CancellationToken.None)).Should().BeTrue();
        (await _totp.RedeemRecoveryCodeAsync(user.Id, codes[0], CancellationToken.None)).Should().BeFalse();
        (await _totp.RedeemRecoveryCodeAsync(user.Id, codes[1], CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ResetAsync_disables_two_factor_and_rotates_authenticator_key()
    {
        var user = await CreateAdminAsync();
        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var keyBeforeReset = await _userManager.GetAuthenticatorKeyAsync(user);

        await _totp.ResetAsync(user.Id, CancellationToken.None);

        (await _totp.IsEnabledAsync(user.Id, CancellationToken.None)).Should().BeFalse();
        var keyAfterReset = await _userManager.GetAuthenticatorKeyAsync(
            (await _userManager.FindByIdAsync(user.Id.ToString()))!);
        keyAfterReset.Should().NotBeNullOrWhiteSpace();
        keyAfterReset.Should().NotBe(keyBeforeReset);
    }
}
