using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class OrderHandoverAuditTests
{
    [Fact]
    public void OrderHandover_SecondAttempt_SupersedesPreviousAttempt()
    {
        var dailyOrderId = Guid.NewGuid();
        var initialHandoverId = Guid.NewGuid();

        var firstAttempt = new OrderHandover
        {
            DailyOrderId = dailyOrderId,
            AttemptNumber = 1,
            Status = "Revoked",
            HandedOverAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        var secondAttempt = new OrderHandover
        {
            DailyOrderId = dailyOrderId,
            AttemptNumber = 2,
            SupersedesHandoverId = firstAttempt.Id,
            Status = "Completed",
            HandedOverAtUtc = DateTime.UtcNow
        };

        Assert.Equal(1, firstAttempt.AttemptNumber);
        Assert.Equal(2, secondAttempt.AttemptNumber);
        Assert.Equal(firstAttempt.Id, secondAttempt.SupersedesHandoverId);
        Assert.Equal(firstAttempt.DailyOrderId, secondAttempt.DailyOrderId);
    }

    [Fact]
    public void OrderHandover_Revocation_AppendsImmutableRevocationRecord()
    {
        var handover = new OrderHandover
        {
            DailyOrderId = Guid.NewGuid(),
            AttemptNumber = 1,
            Status = "Completed",
            HandedOverAtUtc = DateTime.UtcNow.AddHours(-2)
        };

        var userId = Guid.NewGuid();
        var revocationTime = DateTime.UtcNow;
        var revocation = new OrderHandoverRevocation
        {
            HandoverId = handover.Id,
            RevokedAtUtc = revocationTime,
            RevokedByUserId = userId,
            Reason = "Driver vehicle broke down after loading box"
        };

        handover.Revocations.Add(revocation);
        handover.Status = "Revoked";
        handover.RevokedAtUtc = revocationTime;
        handover.RevokedBy = userId.ToString();
        handover.RevocationReason = revocation.Reason;

        Assert.Single(handover.Revocations);
        Assert.Equal(handover.Id, handover.Revocations.First().HandoverId);
        Assert.Equal("Revoked", handover.Status);
        Assert.Equal("Driver vehicle broke down after loading box", handover.RevocationReason);
    }

    [Fact]
    public void ModelConfiguration_HasUniqueDailyOrderAttemptIndex_AndConfiguresRevocations()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options, null!);

        var handoverEntity = db.Model.FindEntityType(typeof(OrderHandover));
        Assert.NotNull(handoverEntity);

        var attemptIndex = handoverEntity.GetIndexes()
            .FirstOrDefault(idx => idx.Properties.Select(p => p.Name).SequenceEqual(new[] { "DailyOrderId", "AttemptNumber" }));
        
        Assert.NotNull(attemptIndex);
        Assert.True(attemptIndex.IsUnique, "DailyOrderId and AttemptNumber index must be unique");

        var revocationEntity = db.Model.FindEntityType(typeof(OrderHandoverRevocation));
        Assert.NotNull(revocationEntity);
    }
}
