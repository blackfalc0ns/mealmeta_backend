using System.Net;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Entities;
using MealMate.Infrastructure.Services;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MealMate.UnitTests.Settings;

public sealed class MyFatoorahPaymentMethodsDiagnosticsTests
{
    [Fact]
    public async Task ProviderRejection_LogsStatusWithoutLeakingResponseOrToken()
    {
        var logger = new RecordingLogger();
        await using var db = CreateDb();
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.Unauthorized,
            "{\"IsSuccess\":false,\"Message\":\"The token does not have the required permissions!\",\"Data\":null,\"UnexpectedSecret\":\"private-provider-value\"}"));
        var service = CreateService(db, http, logger);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetPaymentMethodsAsync(118.25m));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        var log = Assert.Single(logger.Messages);
        Assert.Contains("401", log);
        Assert.Contains("InitiatePayment", log);
        Assert.DoesNotContain("private-api-token", log);
        Assert.DoesNotContain("private-provider-value", log);
    }

    [Fact]
    public async Task InvalidJson_LogsFailureBeforeItReachesTheGeneric502Handler()
    {
        var logger = new RecordingLogger();
        await using var db = CreateDb();
        using var http = new HttpClient(new ResponseHandler(HttpStatusCode.OK, "<html>upstream error</html>"));
        var service = CreateService(db, http, logger);

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.GetPaymentMethodsAsync(118.25m));

        Assert.Contains("InitiatePayment", Assert.Single(logger.Messages));
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private static MyFatoorahPaymentService CreateService(AppDbContext db, HttpClient http, RecordingLogger logger)
    {
        db.MyFatoorahGatewayConfigs.Add(new MyFatoorahGatewayConfig(
            "MyFatoorah", true, "https://apitest.myfatoorah.com", "protected-token", "",
            true, true, true, "KWD", "KWT", "****"));
        db.SaveChanges();
        var protector = Substitute.For<IEmailSecretProtector>();
        protector.Decrypt("protected-token").Returns("private-api-token");
        return new(http, logger, db, protector, new ConfigurationBuilder().Build());
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class RecordingLogger : ILogger<MyFatoorahPaymentService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
