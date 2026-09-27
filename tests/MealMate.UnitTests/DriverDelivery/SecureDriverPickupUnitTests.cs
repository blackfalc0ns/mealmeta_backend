using FluentAssertions;
using MealMate.Domain.DriverDelivery;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.DriverDelivery;

public class SecureDriverPickupUnitTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<MealMate.Application.Common.Abstractions.IDomainEventDispatcher>());
    }

    [Fact]
    public async Task AppDbContext_CanPersistAndQuery_PickupValidation_And_PickupConditionPhoto()
    {
        await using var db = CreateDb();
        var tripId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var driverProfileId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        var validation = new PickupValidation
        {
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            DriverAssignmentId = assignmentId,
            BarcodeValue = "480K-1234",
            TokenHash = "mock-sha256-hash",
            ValidatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            IsManualEntry = false
        };
        db.PickupValidations.Add(validation);

        var photo = new PickupConditionPhoto
        {
            PickupValidationId = validation.Id,
            TripId = tripId,
            BoxId = boxId,
            DriverProfileId = driverProfileId,
            StorageKey = "uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 204800,
            Checksum = "mock-sha256",
            UploadedAtUtc = DateTime.UtcNow
        };
        db.PickupConditionPhotos.Add(photo);

        await db.SaveChangesAsync();

        var queriedValidation = await db.PickupValidations.FirstOrDefaultAsync(v => v.Id == validation.Id);
        queriedValidation.Should().NotBeNull();
        queriedValidation!.TokenHash.Should().Be("mock-sha256-hash");
        queriedValidation.ConsumedAtUtc.Should().BeNull();

        var queriedPhoto = await db.PickupConditionPhotos.FirstOrDefaultAsync(p => p.Id == photo.Id);
        queriedPhoto.Should().NotBeNull();
        queriedPhoto!.StorageKey.Should().Be("uploads/drivers/delivery/pickup-condition/trip/box/photo.jpg");
        queriedPhoto.ConfirmedAtUtc.Should().BeNull();
    }
}
