using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.DocumentTemplates;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.ApiTests.Support;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using MealMate.Persistence.Interceptors;
using MealMate.Persistence.Outbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using DomainProgram = MealMate.Domain.ProgramsSubscriptions.Program;

namespace MealMate.ApiTests.OrderLifecycle;

public sealed class TestDriverPushNotifier : IDriverPushNotifier
{
    public ConcurrentBag<(Guid DriverUserId, Guid BoxId, string BoxCode, string ZoneName)> BoxAssignments { get; } = new();
    public volatile bool ShouldThrowOnPush = false;

    public Task NotifyBoxAssignedAsync(Guid driverUserId, string boxCode, string zoneName, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowOnPush)
            throw new HttpRequestException("Simulated FCM network failure for driver push notification.");

        BoxAssignments.Add((driverUserId, Guid.Empty, boxCode, zoneName));
        return Task.CompletedTask;
    }

    public Task NotifyBoxAssignedAsync(Guid driverUserId, Guid boxId, string boxCode, string zoneName, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowOnPush)
            throw new HttpRequestException("Simulated FCM network failure for driver push notification.");

        BoxAssignments.Add((driverUserId, boxId, boxCode, zoneName));
        return Task.CompletedTask;
    }

    public Task NotifyTripAssignedAsync(Guid driverUserId, string tripCode, int boxesCount, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyTripAssignedAsync(Guid driverUserId, Guid tripId, string tripCode, int boxesCount, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyKitchenReadyForPickupAsync(Guid driverUserId, string tripCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task NotifyKitchenReadyForPickupAsync(Guid driverUserId, Guid tripId, string tripCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class TestPayzahPaymentService : IPaymentGatewayService
{
    public Func<string, string, CancellationToken, Task<PaymentGatewayStatusResponse?>>? StatusHandler { get; set; }
    public ConcurrentDictionary<string, string> PaymentCustomerReferences { get; } = new();
    public ConcurrentDictionary<string, decimal> PaymentInvoiceValues { get; } = new();

    public Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<PaymentGatewayMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PaymentGatewayMethod>>([
            new(1, "KNET", "كي نت", "KNet", 0.25m, invoiceValue + 0.25m, "KWD", true)
        ]);

    public Task<PaymentCreationReceipt> ExecutePaymentAsync(decimal invoiceValue, string customerName, string customerEmail, string customerMobile,
        string callbackUrl, string errorUrl, string? customerReference = null, int paymentMethodId = 1, CancellationToken cancellationToken = default)
    {
        var paymentId = "PAY-" + Guid.NewGuid().ToString("N")[..8];
        if (!string.IsNullOrWhiteSpace(customerReference))
        {
            PaymentCustomerReferences["pz-" + paymentId] = customerReference;
        }
        PaymentInvoiceValues["pz-" + paymentId] = invoiceValue;
        return Task.FromResult(new PaymentCreationReceipt(paymentId, $"https://test-gateway.mealmate.local/pay/{paymentId}?paymentId=pz-{paymentId}", paymentId));
    }

    public Task<PaymentGatewayStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default)
    {
        if (keyType == "TrackId") key = "pz-" + key;
        if (StatusHandler != null)
        {
            return StatusHandler(key, keyType, cancellationToken);
        }

        var custRef = PaymentCustomerReferences.TryGetValue(key, out var r) ? r : key;
        var amount = PaymentInvoiceValues.TryGetValue(key, out var v) ? v : 78m;

        return Task.FromResult<PaymentGatewayStatusResponse?>(new PaymentGatewayStatusResponse(
            InvoiceId: "INV-" + key,
            InvoiceStatus: "Paid",
            InvoiceValue: amount,
            CustomerName: "Test Customer",
            CustomerEmail: "customer@mealmate.local",
            CustomerMobile: "96590000001",
            TransactionId: "TX-" + key,
            PaymentMethod: "KNET",
            CreatedDateUtc: DateTime.UtcNow,
            CustomerReference: custRef,
            CurrencyCode: "KWD",
            PaymentId: key));
    }


}

public sealed class OrderLifecycleEndToEndFixture : IAsyncLifetime
{
    private MealMate.Testing.PostgresTestDatabase _container = null!;
    private string? _supportStorageRoot;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public TestPayzahPaymentService PaymentService { get; } = new();
    public TestDriverPushNotifier PushNotifier { get; } = new();
    public SupportEvidenceTestScanner? EvidenceScannerOverride { get; private set; }
    public TestClock Clock { get; } = new();
    public string ConnectionString => _container.GetConnectionString();
    public string SupportStorageRoot => _supportStorageRoot ?? throw new InvalidOperationException("Test support storage root is not initialized.");

    public async Task InitializeAsync()
    {
        try
        {
            _container = new();

            await _container.StartAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_container.GetConnectionString())
                .Options;

            await using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
            await db.Database.MigrateAsync();

            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:MealMateDb", _container.GetConnectionString());
                builder.UseSetting("Seed:Identity", "false");
                builder.UseSetting("Email:SecretsEncryptionKey", Convert.ToBase64String(new byte[32]));
                _supportStorageRoot = Path.Combine(Path.GetTempPath(), "mealmate-support-tests", Guid.NewGuid().ToString("N"));
                builder.UseSetting("Storage:LocalRoot", _supportStorageRoot);
                builder.UseSetting("Storage:DocumentEncryptionKey", Convert.ToBase64String(new byte[32]));
                builder.ConfigureTestServices(services =>
                {
                    if (EvidenceScannerOverride is not null)
                    {
                        services.RemoveAll<IEvidenceMalwareScanner>();
                        services.AddSingleton<IEvidenceMalwareScanner>(EvidenceScannerOverride);
                    }
                    services.RemoveAll<IDateTimeProvider>();
                    services.AddSingleton<IDateTimeProvider>(Clock);
                    // Keep the container connection local to this host. A process-wide override
                    // lets other concurrently running API hosts consume this fixture's Outbox.
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                    services.AddDbContext<AppDbContext>((provider, dbOptions) =>
                    {
                        dbOptions.UseNpgsql(_container.GetConnectionString(), npgsql =>
                        {
                            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                        });
                        dbOptions.AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>());
                    });

                    // Remove background IHostedService to ensure deterministic manual outbox ticking in test steps
                    var hostedServiceDescriptors = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                    foreach (var descriptor in hostedServiceDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    var paymentDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPaymentGatewayService));
                    if (paymentDescriptor != null) services.Remove(paymentDescriptor);
                    services.AddSingleton<IPaymentGatewayService>(PaymentService);

                    var pushDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDriverPushNotifier));
                    if (pushDescriptor != null) services.Remove(pushDescriptor);
                    services.AddSingleton<IDriverPushNotifier>(PushNotifier);
                });
            });
        }
        catch (Exception ex)
        {
            throw new XunitException("Failed to initialize PostgreSQL container or test host.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
        if (_supportStorageRoot is not null)
        {
            var approvedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mealmate-support-tests"));
            var resolvedTarget = Path.GetFullPath(_supportStorageRoot);
            var targetLeaf = Path.GetFileName(resolvedTarget);
            if (string.Equals(Path.GetDirectoryName(resolvedTarget), approvedRoot, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(targetLeaf, "N", out _) && Directory.Exists(resolvedTarget))
                Directory.Delete(resolvedTarget, recursive: true);
        }
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    public HttpClient CreateClientForUser(TokenUser user)
    {
        using var scope = Factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var token = tokenService.CreateAccessToken(user);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    public SupportEvidenceTestScanner EnableSupportEvidenceTestScanner()
        => EvidenceScannerOverride ??= new SupportEvidenceTestScanner();
}

public sealed record FixtureEntities(
    Country Country,
    Tenant Tenant,
    Region Region,
    DomainProgram Program,
    Bundle Bundle,
    RestaurantProfile RestaurantA,
    RestaurantProfile RestaurantB,
    Meal MealA,
    ApplicationUser CustomerUser,
    CustomerProfile CustomerProfile,
    CustomerAddress CustomerAddress,
    ApplicationUser KitchenStaffA,
    ApplicationUser KitchenStaffB,
    ApplicationUser DispatcherA,
    ApplicationUser DispatcherB,
    ApplicationUser DriverUserA,
    DriverProfile DriverProfileA,
    ApplicationUser DriverUserB,
    DriverProfile DriverProfileB,
    DateOnly StartDate);
