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

public class DocumentReviewReasonTests
{
    [Fact]
    public async Task ReviewDocument_approve_with_blank_reason_is_allowed()
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
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);

        var handler = CreateHandler(db, currentUser);
        var result = await handler.Handle(
            new ReviewDocumentCommand(document.Id, Approve: true, Reason: "   "),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LatestStatus.Should().Be(VerificationStatus.Approved);
        result.Value.RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task ReviewDocument_reject_with_blank_reason_is_rejected()
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
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);

        var handler = CreateHandler(db, currentUser);
        var result = await handler.Handle(
            new ReviewDocumentCommand(document.Id, Approve: false, Reason: ""),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task ReviewDocument_trimmed_reason_is_stored_and_audited()
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
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);

        var audit = Substitute.For<IAuditLogger>();
        var handler = CreateHandler(db, currentUser, audit);

        var untrimmedReason = "  Valid reason with spaces  ";
        var expectedTrimmed = "Valid reason with spaces";

        var result = await handler.Handle(
            new ReviewDocumentCommand(document.Id, Approve: true, Reason: untrimmedReason),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RejectionReason.Should().Be(expectedTrimmed);

        // Verify the verification was stored with trimmed reason (tracked, pre-SaveChanges)
        var verification = db.ChangeTracker.Entries<DocumentVerification>()
            .Select(e => e.Entity)
            .First(v => v.DocumentId == document.Id);
        verification.Reason.Should().Be(expectedTrimmed);

        // Verify audit was called with trimmed reason
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Reason == expectedTrimmed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewDocument_overlength_reason_is_rejected()
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
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);

        var handler = CreateHandler(db, currentUser);
        var longReason = new string('x', 2001);

        var result = await handler.Handle(
            new ReviewDocumentCommand(document.Id, Approve: false, Reason: longReason),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.MaxLength);
    }

    [Fact]
    public async Task ReviewDocument_reason_exactly_2000_chars_is_accepted()
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
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(true);

        var handler = CreateHandler(db, currentUser);
        var exactLengthReason = new string('y', 2000);

        var result = await handler.Handle(
            new ReviewDocumentCommand(document.Id, Approve: true, Reason: exactLengthReason),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RejectionReason.Should().Be(exactLengthReason);
    }

    private static ReviewDocumentCommandHandler CreateHandler(
        AppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger? audit = null)
    {
        audit ??= Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        return new ReviewDocumentCommandHandler(db, currentUser, audit, clock);
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
