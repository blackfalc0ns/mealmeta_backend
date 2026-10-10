using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MealMate.ApiTests;

/// <summary>
/// HTTP contract tests do not run database polling workers. Database-backed
/// fixtures manage their own containers and exercise outbox processing explicitly.
/// </summary>
public sealed class EndpointTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Seed:Identity", "false");
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services.Where(s => s.ServiceType == typeof(IHostedService)).ToList())
            {
                services.Remove(descriptor);
            }
        });
    }
}
