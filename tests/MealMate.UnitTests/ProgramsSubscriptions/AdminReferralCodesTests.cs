using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class AdminReferralCodesTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static (AppDbContext Db, IAuditLogger Audit, IDateTimeProvider Clock) Create()
    {
        var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return (db, audit, clock);
    }

    [Fact]
    public async Task Create_Succeeds_AndAudits()
    {
        var (db, audit, clock) = Create();
        var ownerId = Guid.NewGuid();
        var handler = new AdminReferralCodeCommandsHandler(db, audit, clock);

        var result = await handler.Handle(new CreateAdminReferralCodeCommand("SAVE10", ownerId, 10m, 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("SAVE10");
        result.Value.OwnerUserId.Should().Be(ownerId);
        result.Value.RewardPercent.Should().Be(10m);
        result.Value.MaxUses.Should().Be(50);
        result.Value.UsedCount.Should().Be(0);
        result.Value.IsActive.Should().BeTrue();

        var saved = await db.ReferralCodes.SingleAsync(x => x.Code == "SAVE10");
        saved.RewardPercent!.Value.Should().Be(0.10m);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "ReferralCodeCreated" &&
                e.EntityType == nameof(ReferralCode) &&
                e.SourceChannel == "admin-dashboard"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Fails_OnDuplicateCode()
    {
        var (db, audit, clock) = Create();
        db.ReferralCodes.Add(new ReferralCode
        {
            Code = "EXISTING",
            OwnerUserId = Guid.NewGuid(),
            RewardPercent = Percentage.FromPercent(15m),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var handler = new AdminReferralCodeCommandsHandler(db, audit, clock);
        var result = await handler.Handle(new CreateAdminReferralCodeCommand("existing", Guid.NewGuid(), 10m, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("referral.code_duplicate");
    }

    [Fact]
    public async Task Create_Fails_OnInvalidPercent()
    {
        var (db, audit, clock) = Create();
        var handler = new AdminReferralCodeCommandsHandler(db, audit, clock);

        var result = await handler.Handle(new CreateAdminReferralCodeCommand("INVALID", Guid.NewGuid(), 150m, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("referral.invalid_percent");
    }

    [Fact]
    public async Task List_FiltersByQuery()
    {
        var (db, audit, clock) = Create();
        db.ReferralCodes.AddRange(
            new ReferralCode { Code = "AHMED20", OwnerUserId = Guid.NewGuid(), RewardPercent = Percentage.FromPercent(20m), IsActive = true, CreatedAtUtc = clock.UtcNow },
            new ReferralCode { Code = "SARA15", OwnerUserId = Guid.NewGuid(), RewardPercent = Percentage.FromPercent(15m), IsActive = true, CreatedAtUtc = clock.UtcNow },
            new ReferralCode { Code = "AHMED50", OwnerUserId = Guid.NewGuid(), RewardPercent = Percentage.FromPercent(50m), IsActive = false, CreatedAtUtc = clock.UtcNow }
        );
        await db.SaveChangesAsync();

        var handler = new GetAdminReferralCodesListQueryHandler(db);
        var result = await handler.Handle(new GetAdminReferralCodesListQuery(1, 20, "AHMED"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.All(x => x.Code.Contains("AHMED")).Should().BeTrue();
    }

    [Fact]
    public async Task SetStatus_TogglesActive_AndAudits()
    {
        var (db, audit, clock) = Create();
        var referral = new ReferralCode
        {
            Code = "TOGGLE",
            OwnerUserId = Guid.NewGuid(),
            RewardPercent = Percentage.FromPercent(10m),
            IsActive = true,
        };
        db.ReferralCodes.Add(referral);
        await db.SaveChangesAsync();

        var handler = new AdminReferralCodeCommandsHandler(db, audit, clock);
        var result = await handler.Handle(new SetReferralCodeStatusCommand(referral.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();

        var updated = await db.ReferralCodes.FindAsync(referral.Id);
        updated!.IsActive.Should().BeFalse();

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "ReferralCodeStatusChanged" &&
                e.EntityType == nameof(ReferralCode) &&
                (bool)e.Before! == true &&
                (bool)e.After! == false &&
                e.SourceChannel == "admin-dashboard"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAttributions_ReturnsPagedAttributions()
    {
        var (db, _, clock) = Create();
        var referral = new ReferralCode
        {
            Code = "ATTRIB10",
            OwnerUserId = Guid.NewGuid(),
            RewardPercent = Percentage.FromPercent(10m),
            IsActive = true,
        };
        db.ReferralCodes.Add(referral);

        var customerId = Guid.NewGuid();
        db.ReferralAttributions.Add(new ReferralAttribution
        {
            Code = "ATTRIB10",
            ReferralCodeId = referral.Id,
            ReferrerUserId = referral.OwnerUserId,
            ReferredCustomerUserId = customerId,
            AttributedAtUtc = clock.UtcNow,
            RewardPercentSnapshot = 10m,
        });
        await db.SaveChangesAsync();

        var handler = new GetAdminReferralAttributionsQueryHandler(db);
        var result = await handler.Handle(new GetAdminReferralAttributionsQuery(referral.Id, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].ReferredCustomerUserId.Should().Be(customerId);
        result.Value.Items[0].Code.Should().Be("ATTRIB10");
    }
}
