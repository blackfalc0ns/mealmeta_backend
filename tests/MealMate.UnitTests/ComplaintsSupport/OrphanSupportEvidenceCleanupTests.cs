using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.UnifiedSupport.Storage;
using MealMate.Domain.Common;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class OrphanSupportEvidenceCleanupTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static OrphanSupportEvidenceCleanupService CreateService(AppDbContext db, IFileStorage storage)
    {
        var transactions = Substitute.For<IApplicationTransactionExecutor>();
        transactions.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.Arg<CancellationToken>()));
        return new OrphanSupportEvidenceCleanupService(db, storage, Substitute.For<IApplicationLockManager>(), transactions,
            NullLogger<OrphanSupportEvidenceCleanupService>.Instance);
    }

    [Fact]
    public async Task Cleanup_DeletesUnreferencedEvidenceOlderThan24Hours_AndKeepsReferencedAndFresh()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();

        // Orphan: unreferenced and older than 25 hours
        var orphan = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/orphan.pdf",
            FileName = "orphan.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-25),
        };

        // Fresh: unreferenced but uploaded only 2 hours ago
        var fresh = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/fresh.pdf",
            FileName = "fresh.pdf",
            ContentType = "application/pdf",
            SizeBytes = 2048,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-2),
        };

        // Referenced: older than 30 hours, but referenced by a SupportCaseMessage
        var referenced = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/referenced.pdf",
            FileName = "referenced.pdf",
            ContentType = "application/pdf",
            SizeBytes = 4096,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
        };

        var message = new SupportCaseMessage
        {
            SupportCaseId = Guid.NewGuid(),
            AuthorUserId = Guid.NewGuid(),
            Visibility = SupportCaseMessageVisibility.Participant,
            Body = "Please check attached document",
            AttachmentKeys = ["documents/support/evidence/referenced.pdf"],
            CreatedAtUtc = DateTime.UtcNow.AddHours(-29),
        };

        db.UploadFileRecords.AddRange(orphan, fresh, referenced);
        db.SupportCaseMessages.Add(message);
        db.SupportEvidenceScanReceipts.Add(new SupportEvidenceScanReceipt
        {
            UploadFileRecordId = referenced.Id,
            Sha256 = "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824",
            SizeBytes = referenced.SizeBytes,
            ContentType = referenced.ContentType,
            Verdict = nameof(EvidenceScanVerdict.Rejected),
            Provider = "clamd",
            FailureCode = "content_invalid",
            ScannedAtUtc = DateTime.UtcNow.AddHours(-25),
            ValidationVersion = 1,
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, storage);

        var cleanedCount = await service.RunCleanupOnceAsync();

        cleanedCount.Should().Be(1);
        await storage.Received(1).DeleteAsync("documents/support/evidence/orphan.pdf", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("documents/support/evidence/fresh.pdf", Arg.Any<CancellationToken>());
        await storage.DidNotReceive().DeleteAsync("documents/support/evidence/referenced.pdf", Arg.Any<CancellationToken>());

        var remainingKeys = await db.UploadFileRecords.Select(r => r.StorageKey).ToListAsync();
        remainingKeys.Should().NotContain("documents/support/evidence/orphan.pdf");
        remainingKeys.Should().Contain("documents/support/evidence/fresh.pdf");
        remainingKeys.Should().Contain("documents/support/evidence/referenced.pdf");
    }

    [Fact]
    public async Task Cleanup_DeletesQuarantinedPayload_WhenScanVerdictIsRejected_AndRetainsReceipt()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();

        var rejectedRecord = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/malicious.pdf",
            FileName = "malicious.pdf",
            ContentType = "application/pdf",
            SizeBytes = 8192,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-26),
        };

        var scanReceipt = new SupportEvidenceScanReceipt
        {
            UploadFileRecordId = rejectedRecord.Id,
            Sha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            SizeBytes = 8192,
            ContentType = "application/pdf",
            Verdict = nameof(EvidenceScanVerdict.Rejected),
            Provider = "clamd",
            FailureCode = "Win.Test.EICAR_HDB-1",
            ScannedAtUtc = DateTime.UtcNow.AddHours(-25),
            ValidationVersion = 1,
        };

        db.UploadFileRecords.Add(rejectedRecord);
        db.SupportEvidenceScanReceipts.Add(scanReceipt);
        await db.SaveChangesAsync();

        var service = CreateService(db, storage);

        var cleanedCount = await service.RunCleanupOnceAsync();

        cleanedCount.Should().Be(1);
        await storage.Received(1).DeleteAsync("documents/support/evidence/malicious.pdf", Arg.Any<CancellationToken>());

        // The auditable scan receipt must NOT be deleted
        var preservedReceipt = await db.SupportEvidenceScanReceipts.SingleOrDefaultAsync(r => r.UploadFileRecordId == rejectedRecord.Id);
        preservedReceipt.Should().NotBeNull();
        preservedReceipt!.Verdict.Should().Be(nameof(EvidenceScanVerdict.Rejected));
        preservedReceipt.FailureCode.Should().Be("Win.Test.EICAR_HDB-1");

        var secondRunCount = await service.RunCleanupOnceAsync();
        secondRunCount.Should().Be(0);
        await storage.Received(1).DeleteAsync("documents/support/evidence/malicious.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_SkipsReferencedRecordsBeforeApplyingBatchLimit()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();
        var oldReferencedUploads = Enumerable.Range(0, 101).Select(index => new UploadFileRecord
        {
            StorageKey = $"documents/support/evidence/referenced-{index:D3}.pdf",
            FileName = "referenced.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
        }).ToArray();
        var messages = oldReferencedUploads.Select(upload => new SupportCaseMessage
        {
            SupportCaseId = Guid.NewGuid(),
            AuthorUserId = Guid.NewGuid(),
            Visibility = SupportCaseMessageVisibility.Participant,
            Body = "Attached document",
            AttachmentKeys = [upload.StorageKey],
            CreatedAtUtc = DateTime.UtcNow.AddHours(-29),
        }).ToArray();
        var orphan = new UploadFileRecord
        {
            StorageKey = "documents/support/evidence/orphan-after-referenced.pdf",
            FileName = "orphan.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
        };
        db.UploadFileRecords.AddRange(oldReferencedUploads);
        db.UploadFileRecords.Add(orphan);
        db.SupportCaseMessages.AddRange(messages);
        await db.SaveChangesAsync();

        var service = CreateService(db, storage);

        var cleanedCount = await service.RunCleanupOnceAsync();

        cleanedCount.Should().Be(1);
        await storage.Received(1).DeleteAsync(orphan.StorageKey, Arg.Any<CancellationToken>());
        foreach (var referenced in oldReferencedUploads)
            await storage.DidNotReceive().DeleteAsync(referenced.StorageKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cleanup_RetriesFailedDeletesWithoutStarvingLaterCandidates()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();
        var uploads = Enumerable.Range(0, 101).Select(index => new UploadFileRecord
        {
            StorageKey = $"documents/support/evidence/failure-{index:D3}.pdf",
            FileName = "failure.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Scope = "support",
            Folder = "evidence",
            UploadedAtUtc = DateTime.UtcNow.AddHours(-30),
        }).ToArray();
        db.UploadFileRecords.AddRange(uploads);
        await db.SaveChangesAsync();
        storage.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new IOException("storage unavailable"));

        var service = CreateService(db, storage);

        await service.RunCleanupOnceAsync();
        storage.Received(100).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await service.RunCleanupOnceAsync();

        storage.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IFileStorage.DeleteAsync))
            .Select(call => call.GetArguments()[0])
            .Distinct()
            .Should().HaveCount(101);
    }

    [Fact]
    public async Task Cleanup_PropagatesCancellationBeforeProcessingCandidates()
    {
        await using var db = CreateDb();
        var storage = Substitute.For<IFileStorage>();
        var service = CreateService(db, storage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunCleanupOnceAsync(cancellation.Token));
        await storage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
