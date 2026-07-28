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

public class DocumentReadOwnershipTests
{
    [Fact]
    public async Task GetDocument_allowed_for_non_owner_with_view_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(context =>
        {
            context.RestaurantRegistrations.Add(registration);
            context.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(true);

        var handler = new GetDocumentQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(document.Id);
    }

    [Fact]
    public async Task GetDocument_allowed_for_owner()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(context =>
        {
            context.RestaurantRegistrations.Add(registration);
            context.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);

        var handler = new GetDocumentQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(document.Id);
    }

    [Fact]
    public async Task GetDocument_allowed_for_owner_with_upload_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(context =>
        {
            context.RestaurantRegistrations.Add(registration);
            context.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsUpload).Returns(true);

        var handler = new GetDocumentQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDocumentQuery(document.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(document.Id);
    }

    [Fact]
    public async Task GetDocument_forbidden_for_non_owner_with_upload_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(context =>
        {
            context.RestaurantRegistrations.Add(registration);
            context.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsUpload).Returns(true);

        var handler = new GetDocumentQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDocumentQuery(document.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task ListOwnerDocuments_allowed_for_non_owner_with_view_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var document = CreateDocument(DocumentOwnerType.Registration, registration.Id);
        await using var db = CreateDbContext(context =>
        {
            context.RestaurantRegistrations.Add(registration);
            context.Documents.Add(document);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(true);

        var handler = new ListOwnerDocumentsQueryHandler(db, currentUser);
        var result = await handler.Handle(
            new ListOwnerDocumentsQuery(DocumentOwnerType.Registration, registration.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task UploadDocument_forbidden_for_view_only_non_owner()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(true);

        var storage = Substitute.For<IFileStorage>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new UploadDocumentCommandHandler(db, storage, audit, clock, currentUser);
        await using var stream = new MemoryStream([0x25, 0x50, 0x44, 0x46]);
        var result = await handler.Handle(new UploadDocumentCommand(
            DocumentOwnerType.Registration,
            registration.Id,
            DocumentType.NationalId,
            "id.pdf",
            "application/pdf",
            stream), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

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
