using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.DriverDelivery;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class OrphanPickupPhotoCleanupTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task Cleanup_DeletesUnconfirmedPhotosOlderThan24Hours_And_SetsDeletedAtUtc()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();

        // Orphan: unconfirmed and older than 25 hours
        var orphan = new PickupConditionPhoto
        {
            StorageKey = "uploads/orphan.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-25),
            ConfirmedAtUtc = null,
            DeletedAtUtc = null
        };

        // Fresh: unconfirmed but uploaded 2 hours ago
        var fresh = new PickupConditionPhoto
        {
            StorageKey = "uploads/fresh.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-2),
            ConfirmedAtUtc = null,
            DeletedAtUtc = null
        };

        // Confirmed: confirmed photo older than 30 hours (must NOT be deleted)
        var confirmed = new PickupConditionPhoto
        {
            StorageKey = "uploads/confirmed.jpg",
            ContentType = "image/jpeg",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
            ConfirmedAtUtc = DateTime.UtcNow.AddHours(-29),
            DeletedAtUtc = null
        };

        db.PickupConditionPhotos.AddRange(orphan, fresh, confirmed);
        await db.SaveChangesAsync();

        var service = new OrphanPickupPhotoCleanupService(db, storage, NullLogger<OrphanPickupPhotoCleanupService>.Instance);

        var cleanedCount = await service.RunCleanupOnceAsync(CancellationToken.None);

        cleanedCount.Should().Be(1);
        await storage.Received(1).DeleteAsync("uploads/orphan.jpg", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("uploads/fresh.jpg", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("uploads/confirmed.jpg", Arg.Any<CancellationToken>());

        var updatedOrphan = await db.PickupConditionPhotos.FindAsync(orphan.Id);
        updatedOrphan!.DeletedAtUtc.Should().NotBeNull();
    }
}
