using System.Globalization;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MealMate.Api.Resources;
using System.Reflection;

namespace MealMate.ApiTests.Localization;

public sealed class DriverProfileHeaderTests
{
    [Theory]
    [InlineData("Accept-Language", "ar-KW", "en", "ar")]
    [InlineData("Accept-Language", "en-US", "ar", "en")]
    [InlineData("Language", "ar", "en", "ar")]
    [InlineData("X-Language", "en", "ar", "en")]
    [InlineData("Accept-Language", "fr;q=1,ar;q=0.8", "en", "ar")]
    [InlineData("Accept-Language", "invalid", "ar", "en")]
    public async Task Profile_UsesHeaderInsteadOfQueryString(string header, string language, string query, string expected)
    {
        var original = CultureInfo.CurrentUICulture;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/driver/profile";
        context.Request.QueryString = new QueryString($"?culture={query}&ui-culture={query}");
        context.Request.Headers[header] = language;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(query);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        using var provider = services.BuildServiceProvider();
        try
        {
            var middleware = new BilingualJsonMiddleware(_ =>
            {
                Assert.Equal(expected, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
                var messages = new ResxLocalizedMessageProvider(provider.GetRequiredService<IStringLocalizer<Messages>>());
                Assert.Equal(expected == "ar" ? "متصل" : "Online", messages.GetMessage("driver.profile.status.Online"));
                Assert.Equal(expected == "ar" ? "رخصة القيادة" : "Driving license", messages.GetMessage("driver.profile.document.DrivingLicense"));
                return Task.CompletedTask;
            });
            await middleware.InvokeAsync(context);
            Assert.Equal(query, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally { CultureInfo.CurrentUICulture = original; }
    }

    [Fact]
    public void Profile_RequiresDriverRoleAndDoesNotAcceptDriverId()
    {
        var controller = typeof(DriverProfileController);
        Assert.Equal("Driver", controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("api/v1/driver/profile", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        var action = controller.GetMethod(nameof(DriverProfileController.GetProfile))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.DoesNotContain(action.GetParameters(), p => p.Name == "driverId");
    }
}
