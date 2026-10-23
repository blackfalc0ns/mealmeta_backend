using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.ComplaintsSupport;

public sealed class SupportCasePersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private DbContextOptions<AppDbContext> _options = null!;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options;
        _db = new AppDbContext(_options, Substitute.For<IDomainEventDispatcher>());
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task CaseAndParticipantRecordsPersistWithParentConcurrency()
    {
        var requesterId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var supportCase = SupportCase.Create(
            SupportCaseChannel.Customer,
            SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport,
            "order",
            "Missing item",
            "An item was missing.",
            requesterId,
            DateTime.UtcNow,
            restaurantId: restaurantId);
        _db.SupportCases.Add(supportCase);
        await _db.SaveChangesAsync();

        _db.SupportCaseMessages.Add(new SupportCaseMessage
        {
            SupportCaseId = supportCase.Id,
            AuthorUserId = requesterId,
            Visibility = SupportCaseMessageVisibility.Participant,
            Body = "Please check this delivery.",
            CreatedAtUtc = DateTime.UtcNow,
        });
        var partyTask = new SupportCaseTask
        {
            SupportCaseId = supportCase.Id,
            Type = SupportCaseTaskType.RestaurantEvidence,
            RecipientType = SupportCaseTaskRecipientType.Restaurant,
            RestaurantId = restaurantId,
            Subject = "Confirm packing",
            Question = "Was the item packed?",
            CreatedByUserId = managerId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.SupportCaseTasks.Add(partyTask);
        _db.SupportCaseLinks.Add(new SupportCaseLink
        {
            SupportCaseId = supportCase.Id,
            Relation = SupportCaseLinkRelation.OperationalSource,
            ResourceType = "Complaint",
            ResourceId = Guid.NewGuid(),
            CreatedByUserId = managerId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        _db.SupportCaseEvents.Add(new SupportCaseEvent
        {
            SupportCaseId = supportCase.Id,
            ActorUserId = managerId,
            EventType = "Created",
            IdempotencyKey = "support-create-1",
            PayloadHash = new string('a', 64),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var loaded = await _db.SupportCases.AsNoTracking().SingleAsync(x => x.Id == supportCase.Id);
        (await _db.SupportCaseMessages.CountAsync(x => x.SupportCaseId == supportCase.Id)).Should().Be(1);
        (await _db.SupportCaseTasks.SingleAsync(x => x.Id == partyTask.Id)).Question.Should().Be("Was the item packed?");
        (await _db.SupportCaseLinks.CountAsync(x => x.SupportCaseId == supportCase.Id)).Should().Be(1);
        (await _db.SupportCaseEvents.SingleAsync(x => x.IdempotencyKey == "support-create-1")).SupportCaseId.Should().Be(loaded.Id);
    }

    [Fact]
    public async Task StaleCaseMutationIsRejectedByPostgresXmin()
    {
        var supportCase = SupportCase.Create(
            SupportCaseChannel.Customer,
            SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport,
            "account",
            "Account access",
            "Cannot access account.",
            Guid.NewGuid(),
            DateTime.UtcNow);
        _db.SupportCases.Add(supportCase);
        await _db.SaveChangesAsync();

        await using var second = new AppDbContext(_options, Substitute.For<IDomainEventDispatcher>());
        var firstCopy = await _db.SupportCases.SingleAsync(x => x.Id == supportCase.Id);
        var secondCopy = await second.SupportCases.SingleAsync(x => x.Id == supportCase.Id);
        firstCopy.ModifiedAtUtc = DateTime.UtcNow.AddSeconds(1);
        await _db.SaveChangesAsync();
        secondCopy.ModifiedAtUtc = DateTime.UtcNow.AddSeconds(2);

        var save = () => second.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task ChildWritesWithSameTimestampAdvanceParentXminAndRejectStaleWriter()
    {
        var fixedUtc = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        var requesterId = Guid.NewGuid();
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport, "account", "Access", "Cannot sign in", requesterId, fixedUtc);
        _db.SupportCases.Add(supportCase);
        await _db.SaveChangesAsync();

        await using var staleContext = new AppDbContext(_options, Substitute.For<IDomainEventDispatcher>());
        var staleCopy = await staleContext.SupportCases.SingleAsync(x => x.Id == supportCase.Id);
        var before = await _db.SupportCases.Where(x => x.Id == supportCase.Id).Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();

        _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = supportCase.Id, AuthorUserId = requesterId,
            Visibility = SupportCaseMessageVisibility.Participant, Body = "First message", CreatedAtUtc = fixedUtc });
        supportCase.ModifiedAtUtc = fixedUtc;
        supportCase.ModifiedByUserId = requesterId;
        _db.MarkSupportCaseModified(supportCase);
        await _db.SaveChangesAsync();
        var afterFirst = await _db.SupportCases.Where(x => x.Id == supportCase.Id).Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        afterFirst.Should().NotBe(before);

        _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = supportCase.Id, AuthorUserId = requesterId,
            Visibility = SupportCaseMessageVisibility.Participant, Body = "Second message", CreatedAtUtc = fixedUtc });
        supportCase.ModifiedAtUtc = fixedUtc;
        supportCase.ModifiedByUserId = requesterId;
        _db.MarkSupportCaseModified(supportCase);
        await _db.SaveChangesAsync();
        var afterSecond = await _db.SupportCases.Where(x => x.Id == supportCase.Id).Select(x => EF.Property<uint>(x, "xmin")).SingleAsync();
        afterSecond.Should().NotBe(afterFirst);

        staleCopy.ModifiedAtUtc = fixedUtc;
        staleCopy.ModifiedByUserId = requesterId;
        staleContext.MarkSupportCaseModified(staleCopy);
        var save = () => staleContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
    [Fact]
    public async Task MessageCannotReferenceTaskFromAnotherCase()
    {
        var requesterId = Guid.NewGuid();
        var firstCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "order", "First", "First request", requesterId, DateTime.UtcNow);
        var secondCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.Complaint,
            SupportCaseOwnerTeam.PlatformSupport, "order", "Second", "Second request", requesterId, DateTime.UtcNow);
        _db.SupportCases.AddRange(firstCase, secondCase);
        await _db.SaveChangesAsync();
        var task = new SupportCaseTask
        {
            SupportCaseId = firstCase.Id,
            Type = SupportCaseTaskType.RestaurantEvidence,
            RecipientType = SupportCaseTaskRecipientType.Restaurant,
            RestaurantId = Guid.NewGuid(),
            Subject = "Evidence",
            Question = "Please confirm.",
            CreatedByUserId = requesterId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.SupportCaseTasks.Add(task);
        await _db.SaveChangesAsync();

        _db.SupportCaseMessages.Add(new SupportCaseMessage
        {
            SupportCaseId = secondCase.Id,
            SupportCaseTaskId = task.Id,
            AuthorUserId = requesterId,
            Visibility = SupportCaseMessageVisibility.Participant,
            Body = "Cross case message",
            CreatedAtUtc = DateTime.UtcNow,
        });

        var save = () => _db.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

}
