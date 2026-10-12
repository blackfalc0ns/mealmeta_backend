using FluentAssertions;
using MealMate.Infrastructure.VoiceCalls;
using Microsoft.Extensions.Options;
using MealMate.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallTurnReadinessTests
{
    [Fact]
    public void TlsIsNotAdvertisedUntilExplicitlyConfigured() => new CoturnOptions().IsTlsActive().Should().BeFalse();

    [Fact]
    public void MissingUserCannotGetInventedTurnIdentity()
    {
        var service = new TurnCredentialService(Options.Create(new CoturnOptions()));
        FluentActions.Invoking(() => service.GenerateCredentials(" ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProductionRejectsCheckedInDevelopmentTurnSecret()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build(), environment);
        using var provider = services.BuildServiceProvider();
        FluentActions.Invoking(() => provider.GetRequiredService<IOptions<CoturnOptions>>().Value)
            .Should().Throw<OptionsValidationException>();
    }
}
