using MealMate.Application.Common.Security;
using MealMate.Infrastructure.Email;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class FamilyOtpDeliveryCapabilityTests
{
    [Theory]
    [InlineData("Production", null)]
    [InlineData("Staging", null)]
    [InlineData("Development", null)]
    public async Task Family_otp_cannot_report_delivery_success_without_a_phone_provider(string environmentName, string? fixedCode)
    {
        var delivery = Create(environmentName, fixedCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => delivery.DeliverAsync(
            "WhatsApp", "+96550000001", "123456", OtpPurposes.FamilyMemberFirstLogin));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Explicit_configured_fixed_code_is_available_for_the_authorized_verification_flow(string environmentName)
        => await Create(environmentName, "123456").DeliverAsync(
            "WhatsApp", "+96550000001", "123456", OtpPurposes.FamilyMemberFirstLogin);

    private static RoutingOtpDeliveryService Create(string environmentName, string? fixedCode)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Otp:FixedCode"] = fixedCode }).Build();
        return new(null!, new LoggingOtpDeliveryService(NullLogger<LoggingOtpDeliveryService>.Instance),
            new SilentOtpDeliveryService(), environment, NullLogger<RoutingOtpDeliveryService>.Instance, configuration);
    }
}
