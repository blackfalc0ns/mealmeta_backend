using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Infrastructure;

public sealed class TransactionExecutorTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for TransactionExecutorTests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenExceptionThrown_RollsBackAllMutations()
    {
        await using var db = CreateDb();
        var executor = new PostgresApplicationTransactionExecutor(db);
        var barcode = new MealBarcode
        {
            BarcodeValue = "MM-20260928-TESTROLLBACK",
            SubscriptionDayId = Guid.NewGuid(),
            GeneratedAtUtc = DateTime.UtcNow
        };
        var barcodeId = barcode.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await executor.ExecuteAsync(async ct =>
            {
                db.MealBarcodes.Add(barcode);
                await db.SaveChangesAsync(ct);

                throw new InvalidOperationException("Simulated transaction failure");
            });
        });

        // Verify the record was rolled back and does not exist in the database
        await using var verifyDb = CreateDb();
        var exists = await verifyDb.MealBarcodes.AnyAsync(b => b.Id == barcodeId);
        exists.Should().BeFalse("Rolled-back transaction must leave no persistent records.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenSuccessful_CommitsAllMutations()
    {
        await using var db = CreateDb();
        var executor = new PostgresApplicationTransactionExecutor(db);
        var barcode = new MealBarcode
        {
            BarcodeValue = "MM-20260928-TESTCOMMIT",
            SubscriptionDayId = Guid.NewGuid(),
            GeneratedAtUtc = DateTime.UtcNow
        };
        var barcodeId = barcode.Id;

        var result = await executor.ExecuteAsync(async ct =>
        {
            db.MealBarcodes.Add(barcode);
            await db.SaveChangesAsync(ct);
            return "SUCCESS";
        });

        result.Should().Be("SUCCESS");

        await using var verifyDb = CreateDb();
        var saved = await verifyDb.MealBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId);
        saved.Should().NotBeNull("Committed transaction must persist changes in database.");
        saved!.BarcodeValue.Should().Be("MM-20260928-TESTCOMMIT");
    }
}
