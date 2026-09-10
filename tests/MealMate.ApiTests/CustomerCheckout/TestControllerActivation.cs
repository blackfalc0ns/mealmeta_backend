using MealMate.Api.Controllers.V1;
using MealMate.Api.Security;
using MealMate.Application;
using MealMate.Application.Common.Abstractions;
using MealMate.Infrastructure;
using MealMate.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.CustomerCheckout;

public class TestControllerActivation
{
    private readonly IServiceProvider _serviceProvider;

    public TestControllerActivation()
    {
        var services = new ServiceCollection();
        var inMemConfig = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MealMateDb"] = "Host=localhost;Database=dummy;Username=dummy;Password=dummy",
            ["Email:SecretsEncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemConfig).Build();
        var env = Substitute.For<IWebHostEnvironment>();

        services.AddApplication();
        services.AddInfrastructure(config, env);
        services.AddPersistence(config);
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(env);
        services.AddSingleton<IHostEnvironment>(env);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(typeof(CustomerSubscriptionFlowController))]
    [InlineData(typeof(CustomerFamilySubscriptionFlowController))]
    [InlineData(typeof(CustomerCheckoutController))]
    [InlineData(typeof(MyFatoorahPaymentReturnController))]
    [InlineData(typeof(MyFatoorahWebhookController))]
    public void Test_Controllers_CanBeActivatedByDi(Type controllerType)
    {
        using var scope = _serviceProvider.CreateScope();
        var factory = ActivatorUtilities.CreateFactory(controllerType, Type.EmptyTypes);
        var instance = factory(scope.ServiceProvider, null);
        Assert.NotNull(instance);
    }
}
