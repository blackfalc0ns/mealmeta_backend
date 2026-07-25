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

public class DocumentUploadOwnershipTests
{
    [Fact]
    public async Task UploadDocument_forbidden_for_other_users_registration()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var handler = CreateHandler(db, currentUser);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf"));
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

    [Fact]
    public async Task UploadDocument_allowed_for_own_registration()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsReview).Returns(false);
        currentUser.HasPermission(PermissionCodes.DocumentsView).Returns(false);

        var handler = CreateHandler(db, currentUser);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("pdf"));
        var result = await handler.Handle(new UploadDocumentCommand(
            DocumentOwnerType.Registration,
            registration.Id,
            DocumentType.NationalId,
            "id.pdf",
            "application/pdf",
            stream), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.ChangeTracker.Entries<Document>().Should().HaveCount(1);
    }

    private static UploadDocumentCommandHandler CreateHandler(AppDbContext db, ICurrentUser currentUser)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.SaveAsync(
                Arg.Any<Stream>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new StoredFile("key", "id.pdf", "application/pdf", 3, "checksum"));

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        return new UploadDocumentCommandHandler(db, storage, audit, clock, currentUser);
    }

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
