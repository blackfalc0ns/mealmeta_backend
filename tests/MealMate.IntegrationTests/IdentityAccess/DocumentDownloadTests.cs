using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class DocumentDownloadTests
{
    [Fact]
    public async Task Download_allowed_for_owner()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        var expectedStream = new MemoryStream(Encoding.UTF8.GetBytes("file-content"));
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns(expectedStream);

        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileName.Should().Be(document.FileName);
        result.Value.ContentType.Should().Be(document.ContentType);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(a => a.Action == "DocumentDownloaded" && a.EntityId == document.Id),
            Arg.Any<CancellationToken>());

        result.Value.Dispose();
    }

    [Fact]
    public async Task Download_allowed_for_documents_view_permission()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(true);

        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Dispose();
    }

    [Fact]
    public async Task Download_allowed_for_documents_review_permission()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Dispose();
    }

    [Fact]
    public async Task Download_allowed_for_super_admin()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Dispose();
    }

    [Fact]
    public async Task Download_forbidden_for_non_owner_without_permissions()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);

        // Storage should NOT be accessed
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // No audit for denied access
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Download_not_found_for_missing_document()
    {
        await using var db = CreateDbContext(_ => { });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);

        var storage = Substitute.For<IFileStorage>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("documents.not_found");
    }

    [Fact]
    public async Task Download_returns_file_unavailable_when_storage_file_is_missing()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new FileNotFoundException());
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("documents.file_unavailable");
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Download_does_not_leak_storage_path()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        document.StoragePath = "secret/internal/path/that-should-not-leak.pdf";
        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(document.StoragePath, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([0x25, 0x50, 0x44, 0x46]));

        var audit = Substitute.For<IAuditLogger>();

        var handler = new DownloadDocumentQueryHandler(db, currentUser, storage, audit);
        var result = await handler.Handle(new DownloadDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The result should contain the original filename, NOT the storage path
        result.Value.FileName.Should().Be(document.FileName);
        result.Value.FileName.Should().NotContain("secret/internal");

        result.Value.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static Document CreateDocument(DocumentOwnerType ownerType, Guid ownerId) => new()
    {
        OwnerType = ownerType,
        OwnerId = ownerId,
        DocumentType = DocumentType.NationalId,
        FileName = "id.pdf",
        ContentType = "application/pdf",
        StoragePath = "documents/test",
        SizeBytes = 100,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static RestaurantRegistration CreateRestaurantRegistration(Guid ownerId) => new()
    {
        SubmittedByUserId = ownerId,
        LegalNameAr = "Test",
        LegalNameEn = "Test",
        TradeNameAr = "Test",
        TradeNameEn = "Test",
        ContactPhone = "0500000000",
        ContactEmail = "owner@test.com",
        Status = VerificationStatus.Draft,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static AppDbContext CreateDbContext(Action<AppDbContext> seed)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        seed(db);
        db.SaveChanges();
        return db;
    }
}
