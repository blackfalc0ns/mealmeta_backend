using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport.Storage;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MealMate.IntegrationTests.ComplaintsSupport;

public sealed class SupportEvidenceCleanupLockTests
{
    [Fact]
    public async Task PostgreSql_AttachmentClaimWinningCleanupLockPreventsPayloadDeletion()
    {
        await using var database = new MealMate.Testing.PostgresTestDatabase();
        await database.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.GetConnectionString(), npgsql =>
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;
        var requesterId = Guid.NewGuid();
        var upload = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/cleanup-race.pdf",
            FileName = "cleanup-race.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Scope = "support",
            Folder = "evidence",
            UploadedByUserId = requesterId,
            UploadedAtUtc = DateTime.UtcNow.AddHours(-25),
        };
        var supportCase = SupportCase.Create(SupportCaseChannel.Customer, SupportCaseKind.GeneralRequest,
            SupportCaseOwnerTeam.PlatformSupport, "other", "Attachment race", "Support request",
            requesterId, DateTime.UtcNow);
        await using (var setup = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>()))
        {
            await setup.Database.MigrateAsync();
            setup.SupportCases.Add(supportCase);
            setup.UploadFileRecords.Add(upload);
            await setup.SaveChangesAsync();
        }

        var storage = Substitute.For<IFileStorage>();
        await using var claimant = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        await using var cleaner = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        await using var claimTransaction = await claimant.Database.BeginTransactionAsync();
        await new PostgresApplicationLockManager(claimant).LockSupportEvidenceUploadAsync(upload.Id);
        claimant.SupportCaseMessages.Add(new SupportCaseMessage
        {
            SupportCaseId = supportCase.Id,
            AuthorUserId = requesterId,
            Visibility = SupportCaseMessageVisibility.Participant,
            Body = "I am attaching this file.",
            AttachmentKeys = [upload.StorageKey],
            CreatedAtUtc = DateTime.UtcNow,
        });
        await claimant.SaveChangesAsync();

        var cleanup = new OrphanSupportEvidenceCleanupService(cleaner, storage,
            new PostgresApplicationLockManager(cleaner), new PostgresApplicationTransactionExecutor(cleaner),
            NullLogger<OrphanSupportEvidenceCleanupService>.Instance);
        var cleanupTask = cleanup.RunCleanupOnceAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        cleanupTask.IsCompleted.Should().BeFalse("cleanup should wait for the in-flight attachment claim lock");

        await claimTransaction.CommitAsync();
        var deletedCount = await cleanupTask.WaitAsync(TimeSpan.FromSeconds(5));

        deletedCount.Should().Be(0);
        await storage.DidNotReceive().DeleteAsync(upload.StorageKey, Arg.Any<CancellationToken>());
        var persistedUpload = await cleaner.UploadFileRecords.AsNoTracking().SingleAsync(row => row.Id == upload.Id);
        persistedUpload.SupportPayloadDeletedAtUtc.Should().BeNull();
        (await cleaner.SupportCaseMessages.AnyAsync(message => message.AttachmentKeys.Contains(upload.StorageKey))).Should().BeTrue();
    }
}
