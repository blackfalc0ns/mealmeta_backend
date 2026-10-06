using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class AdminDeliveryManagerRegistrationsTests
{
    [Fact]
    public void DecisionValidator_ShouldFail_WhenActionIsInvalid()
    {
        var command = new AdminDeliveryManagerDecisionCommand(Guid.NewGuid(), "invalid-action", "Some reason");
        var validator = new AdminDeliveryManagerDecisionCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Action));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-changes")]
    public void DecisionValidator_ShouldPass_ForValidActions(string action)
    {
        var command = new AdminDeliveryManagerDecisionCommand(Guid.NewGuid(), action, "Valid reason provided");
        var validator = new AdminDeliveryManagerDecisionCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static async Task<DeliveryManagerRegistration> SeedRegistrationAsync(AppDbContext db)
    {
        var registration = new DeliveryManagerRegistration
        {
            RestaurantId = Guid.NewGuid(),
            FullName = "مسؤول قديم",
            Phone = "+96555552222",
            Email = "legacy.dispatch@burgerfactory.kw",
            NationalId = "296051288888",
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = DateTime.UtcNow,
            ResultingUserId = Guid.NewGuid(),
        };
        db.DeliveryManagerRegistrations.Add(registration);
        db.Documents.Add(new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = registration.Id,
            DocumentType = DocumentType.NationalIdFront,
            FileName = "id-front.jpg",
            ContentType = "image/jpeg",
            StoragePath = "uploads/legacy/id-front.jpg",
            ExpiresAtUtc = DateTime.UtcNow.AddYears(1),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return registration;
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-changes")]
    public async Task DecisionHandler_LegacyActions_ReturnConflict_WithoutMutatingAnything(string action)
    {
        var db = CreateDb();
        var registration = await SeedRegistrationAsync(db);

        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new AdminDeliveryManagerDecisionCommandHandler();

        var result = await handler.Handle(
            new AdminDeliveryManagerDecisionCommand(registration.Id, action, "سبب الأدمن"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("delivery_manager.admin_approval_not_required",
            "delivery managers are onboarded by the restaurant; the admin decision workflow is retired");

        db.ChangeTracker.Clear();
        (await db.DeliveryManagerRegistrations.SingleAsync(x => x.Id == registration.Id)).Status
            .Should().Be(VerificationStatus.Submitted, "no state mutation may happen through the retired workflow");
        (await db.DocumentVerifications.CountAsync()).Should().Be(0, "no document verification may be fabricated");
        (await db.Documents.CountAsync()).Should().Be(1);

        await users.DidNotReceive().UpdateAccountStatusAsync(
            Arg.Any<Guid>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecisionHandler_UnknownRegistration_AlsoReturnsStableConflict()
    {
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new AdminDeliveryManagerDecisionCommandHandler();

        var result = await handler.Handle(
            new AdminDeliveryManagerDecisionCommand(Guid.NewGuid(), "approve", "سبب"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("delivery_manager.admin_approval_not_required");
    }
}
