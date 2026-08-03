using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class MyRestaurantRegistrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Returns newest registration owned by current user
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_newest_registration_for_owner()
    {
        var ownerId = Guid.NewGuid();
        var older = CreateRestaurantRegistration(ownerId, DateTime.UtcNow.AddDays(-10));
        var newer = CreateRestaurantRegistration(ownerId, DateTime.UtcNow);

        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(older);
            ctx.RestaurantRegistrations.Add(newer);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(newer.Id);
        result.Value.Status.Should().Be(VerificationStatus.Submitted);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Document summaries include latest verification by CreatedAtUtc
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Document_summaries_include_latest_verification()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId, DateTime.UtcNow);

        var doc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = registration.Id,
            DocumentType = DocumentType.CommercialLicense,
            FileName = "cr.pdf",
            ContentType = "application/pdf",
            StoragePath = "documents/cr",
            SizeBytes = 2048,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
        };

        // Older verification – rejected
        doc.Verifications.Add(new DocumentVerification
        {
            DocumentId = doc.Id,
            Status = VerificationStatus.Rejected,
            Reason = "Blurry scan",
            ReviewedAtUtc = DateTime.UtcNow.AddDays(-3),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
        });

        // Newer verification – approved (should be the "latest")
        doc.Verifications.Add(new DocumentVerification
        {
            DocumentId = doc.Id,
            Status = VerificationStatus.Approved,
            Reason = "Resubmission accepted",
            ReviewedAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });

        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(doc);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Documents.Should().HaveCount(1);

        var docSummary = result.Value.Documents[0];
        docSummary.DocumentId.Should().Be(doc.Id);
        docSummary.DocumentType.Should().Be(DocumentType.CommercialLicense);
        docSummary.FileName.Should().Be("cr.pdf");
        docSummary.CreatedAtUtc.Should().BeCloseTo(doc.CreatedAtUtc, TimeSpan.FromSeconds(1));

        // Latest verification should be the approved one (newer CreatedAtUtc)
        docSummary.LatestVerificationStatus.Should().Be(VerificationStatus.Approved);
        docSummary.LatestVerificationReason.Should().Be("Resubmission accepted");
        docSummary.LatestVerificationReviewedAtUtc.Should().BeCloseTo(
            DateTime.UtcNow.AddDays(-1), TimeSpan.FromSeconds(2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Safe DTO: never exposes submitted/reviewer user ids, storage keys,
    // content type, size, or other internals.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Safe_dto_does_not_expose_internals()
    {
        var ownerId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId, DateTime.UtcNow);
        registration.ReviewedByUserId = reviewerId;
        registration.ReviewedAtUtc = DateTime.UtcNow;
        registration.ResultingRestaurantId = Guid.NewGuid();

        var doc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = registration.Id,
            DocumentType = DocumentType.NationalId,
            FileName = "id.pdf",
            ContentType = "application/pdf",
            StoragePath = "documents/secret-path",
            SizeBytes = 9999,
            CreatedAtUtc = DateTime.UtcNow,
        };
        doc.Verifications.Add(new DocumentVerification
        {
            Status = VerificationStatus.Approved,
            ReviewedByUserId = reviewerId,
            ReviewedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);
            ctx.Documents.Add(doc);
        });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        // Verify the DTO type does NOT have properties for sensitive fields
        var dtoType = typeof(MyRestaurantRegistrationDto);
        dtoType.GetProperty("SubmittedByUserId").Should().BeNull();
        dtoType.GetProperty("ReviewedByUserId").Should().BeNull();

        var docDtoType = typeof(MyRegistrationDocumentSummaryDto);
        docDtoType.GetProperty("StoragePath").Should().BeNull();
        docDtoType.GetProperty("ContentType").Should().BeNull();
        docDtoType.GetProperty("SizeBytes").Should().BeNull();
        docDtoType.GetProperty("ReviewedByUserId").Should().BeNull();

        // Verify the data is correct
        result.Value.ResultingRestaurantId.Should().Be(registration.ResultingRestaurantId);
        result.Value.Documents[0].DocumentId.Should().Be(doc.Id);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Missing registration → NotFound
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_not_found_when_no_registration_exists()
    {
        await using var db = CreateDbContext(_ => { });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Missing user (unauthenticated) → Unauthorized
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_unauthorized_when_not_authenticated()
    {
        await using var db = CreateDbContext(_ => { });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
    }

    [Fact]
    public async Task Returns_unauthorized_when_user_id_is_null()
    {
        await using var db = CreateDbContext(_ => { });

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Does not return other users' registrations
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_return_other_users_registrations()
    {
        var otherUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(otherUserId, DateTime.UtcNow);

        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(currentUserId);

        var handler = new GetMyRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetMyRestaurantRegistrationQuery(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static RestaurantRegistration CreateRestaurantRegistration(Guid? ownerId, DateTime createdAt) => new()
    {
        SubmittedByUserId = ownerId,
        LegalNameAr = "Test Ar",
        LegalNameEn = "Test En",
        TradeNameAr = "Trade Ar",
        TradeNameEn = "Trade En",
        ContactPhone = "0500000000",
        ContactEmail = "owner@test.com",
        Status = VerificationStatus.Submitted,
        CreatedAtUtc = createdAt,
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
