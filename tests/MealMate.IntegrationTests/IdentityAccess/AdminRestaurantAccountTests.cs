using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
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

public class AdminRestaurantAccountTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Admin registration detail aggregates documents
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAdminRegistrationDetail_aggregates_documents_with_review_metadata()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        var doc = new Document
        {
            OwnerType = DocumentOwnerType.Registration,
            OwnerId = registration.Id,
            DocumentType = DocumentType.NationalId,
            FileName = "id.pdf",
            ContentType = "application/pdf",
            StoragePath = "documents/test",
            SizeBytes = 100,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var reviewerId = Guid.NewGuid();
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
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([new UserIdentityDto(reviewerId, "Admin Reviewer", "reviewer@mealmate.test")]);

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registration.Id.Should().Be(registration.Id);
        result.Value.Documents.Should().HaveCount(1);
        result.Value.Documents[0].DocumentType.Should().Be(DocumentType.NationalId);
        result.Value.Documents[0].ContentType.Should().Be("application/pdf");
        result.Value.Documents[0].SizeBytes.Should().Be(100);
        result.Value.Documents[0].CreatedAtUtc.Should().BeCloseTo(doc.CreatedAtUtc, TimeSpan.FromSeconds(1));
        result.Value.Documents[0].LatestStatus.Should().Be(VerificationStatus.Approved);
        result.Value.Documents[0].ReviewedByUserId.Should().Be(reviewerId);
        result.Value.Documents[0].ReviewedByName.Should().Be("Admin Reviewer");
        result.Value.Registration.CommercialRegistrationNumber.Should().Be("****2345");
        result.Value.Registration.Iban.Should().Be("****7890");
        result.Value.SensitiveDataMasked.Should().BeTrue();
        result.Value.CanRevealSensitive.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_owner_can_view_own_registration()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserIdentityDto>());

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registration.Id.Should().Be(registration.Id);
        result.Value.Documents.Should().BeEmpty();
        result.Value.Registration.CommercialRegistrationNumber.Should().Be("****2345");
        result.Value.SensitiveDataMasked.Should().BeTrue();
        result.Value.CanRevealSensitive.Should().BeFalse();
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_forbidden_for_non_owner_non_admin()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.RestaurantOwner]);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserIdentityDto>());

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_owner_cannot_reveal_sensitive_data()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var users = Substitute.For<IUserAccountService>();

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id, IncludeSensitive: true),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_view_only_admin_gets_masked_data_and_cannot_reveal()
    {
        var registration = CreateRestaurantRegistration(Guid.NewGuid());
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.OperationsAdmin]);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        var users = Substitute.For<IUserAccountService>();

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var masked = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id),
            CancellationToken.None);
        var reveal = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id, IncludeSensitive: true),
            CancellationToken.None);

        masked.IsSuccess.Should().BeTrue();
        masked.Value.Registration.CommercialRegistrationNumber.Should().Be("****2345");
        masked.Value.Registration.Iban.Should().Be("****7890");
        masked.Value.SensitiveDataMasked.Should().BeTrue();
        masked.Value.CanRevealSensitive.Should().BeFalse();
        reveal.IsFailure.Should().BeTrue();
        reveal.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_super_admin_can_explicitly_reveal_sensitive_data()
    {
        var registration = CreateRestaurantRegistration(Guid.NewGuid());
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        var users = Substitute.For<IUserAccountService>();

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id, IncludeSensitive: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registration.CommercialRegistrationNumber.Should().Be("CR12345");
        result.Value.Registration.Iban.Should().Be("KW74CBKU0000000000001234567890");
        result.Value.SensitiveDataMasked.Should().BeFalse();
        result.Value.CanRevealSensitive.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdminRegistrationDetail_approver_can_explicitly_reveal_sensitive_data()
    {
        var registration = CreateRestaurantRegistration(Guid.NewGuid());
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.OperationsAdmin]);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(true);
        var users = Substitute.For<IUserAccountService>();

        var handler = new GetAdminRestaurantRegistrationDetailQueryHandler(db, currentUser, users);
        var result = await handler.Handle(
            new GetAdminRestaurantRegistrationDetailQuery(registration.Id, IncludeSensitive: true),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Registration.CommercialRegistrationNumber.Should().Be("CR12345");
        result.Value.SensitiveDataMasked.Should().BeFalse();
        result.Value.CanRevealSensitive.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Approval does NOT activate owner account
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_does_not_activate_owner_account()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        registration.Status = VerificationStatus.Submitted;
        registration.ContactEmail = null;

        await using var db = CreateDbContext(ctx =>
        {
            ctx.RestaurantRegistrations.Add(registration);

            foreach (var fieldKey in RegistrationFieldKeys.RequiredForApproval(registration))
            {
                ctx.RestaurantRegistrationFieldReviews.Add(new RestaurantRegistrationFieldReview
                {
                    RegistrationId = registration.Id,
                    FieldKey = fieldKey,
                    Status = VerificationStatus.Approved,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }

            // Add all required docs as approved
            foreach (var docType in new[]
                     {
                         DocumentType.CommercialRegistration,
                         DocumentType.CommercialLicense,
                         DocumentType.FoodLicense,
                         DocumentType.AuthorizedSignatoryId,
                         DocumentType.AuthorizedSignatoryAuthorization,
                         DocumentType.BankIbanProof,
                         DocumentType.ProfileImage,
                         DocumentType.BranchLicense,
                     })
            {
                var doc = new Document
                {
                    OwnerType = DocumentOwnerType.Registration,
                    OwnerId = registration.Id,
                    DocumentType = docType,
                    FileName = docType is DocumentType.ProfileImage or DocumentType.BranchPhoto ? $"{docType}.png" : $"{docType}.pdf",
                    ContentType = docType is DocumentType.ProfileImage or DocumentType.BranchPhoto ? "image/png" : "application/pdf",
                    StoragePath = $"documents/{docType}",
                    SizeBytes = 100,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Approved,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                ctx.Documents.Add(doc);
            }
        });

        var users = Substitute.For<IUserAccountService>();
        users.GetRolesAsync(ownerId, Arg.Any<CancellationToken>())
            .Returns([AppRoles.RestaurantOwner]);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantDecisionCommandHandler(db, users, currentUser, audit, clock);
        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "approve", "All docs verified"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(VerificationStatus.Approved);

        // SetRestaurantIdAsync should be called
        await users.Received(1).SetRestaurantIdAsync(ownerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        // UpdateAccountStatusAsync should NOT be called (approval does not activate account)
        await users.DidNotReceive().UpdateAccountStatusAsync(
            Arg.Any<Guid>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());

        // Profile should be non-operational (check change tracker since UnitOfWorkBehavior saves in production)
        await db.SaveChangesAsync();
        var profile = await db.RestaurantProfiles.FirstAsync();
        profile.IsOperationallyActive.Should().BeFalse();
        profile.VerificationStatus.Should().Be(VerificationStatus.Approved);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reject/request-changes require non-blank bounded reasons
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_requires_non_blank_reason()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        registration.Status = VerificationStatus.Submitted;
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantDecisionCommandHandler(db, users, currentUser, audit, clock);
        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "reject", "   "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task RequestChanges_requires_non_blank_reason()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        registration.Status = VerificationStatus.Submitted;
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantDecisionCommandHandler(db, users, currentUser, audit, clock);
        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "request-changes", ""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task Reject_rejects_reason_exceeding_max_length()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        registration.Status = VerificationStatus.Submitted;
        await using var db = CreateDbContext(ctx => ctx.RestaurantRegistrations.Add(registration));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantDecisionCommandHandler(db, users, currentUser, audit, clock);
        var longReason = new string('x', 2001);
        var result = await handler.Handle(
            new AdminRestaurantDecisionCommand(registration.Id, "reject", longReason), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.MaxLength);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Admin restaurant lifecycle (activate/suspend/reactivate)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_sets_operationally_active_activates_owner_and_audits()
    {
        var profile = CreateApprovedRestaurantProfile();
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = CreateUsersWithOwner(profile.UserId, AccountStatus.Registered);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "Ready to operate"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsOperationallyActive.Should().BeTrue();

        // Initial activate must set owner AccountStatus.Active
        await users.Received(1).UpdateAccountStatusAsync(
            profile.UserId, AccountStatus.Active, Arg.Any<CancellationToken>());

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(a => a.Action == "RestaurantActivated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspend_sets_inactive_and_suspends_owner_account()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = true;
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = CreateUsersWithOwner(profile.UserId, AccountStatus.Active);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "suspend", "Policy violation"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsOperationallyActive.Should().BeFalse();

        await users.Received(1).UpdateAccountStatusAsync(
            profile.UserId, AccountStatus.Suspended, Arg.Any<CancellationToken>());

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(a => a.Action == "RestaurantSuspended" && a.Reason == "Policy violation"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reactivate_sets_active_and_reactivates_owner_account()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = false;
        profile.ActivatedAtUtc = DateTime.UtcNow.AddDays(-10);
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = CreateUsersWithOwner(profile.UserId, AccountStatus.Suspended);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "reactivate", "Issue resolved"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsOperationallyActive.Should().BeTrue();

        await users.Received(1).UpdateAccountStatusAsync(
            profile.UserId, AccountStatus.Active, Arg.Any<CancellationToken>());

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(a => a.Action == "RestaurantReactivated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lifecycle_requires_non_blank_reason()
    {
        var profile = CreateApprovedRestaurantProfile();
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", ""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task Activate_fails_if_already_active()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = true;
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Suspend_fails_if_already_suspended()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = false;
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "suspend", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Activate_fails_if_not_approved()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.VerificationStatus = VerificationStatus.Draft;
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // State machine: activate / reactivate guards
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_fails_if_previously_activated()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = false;
        profile.ActivatedAtUtc = DateTime.UtcNow.AddDays(-5);
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Reactivate_fails_if_never_activated()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = false;
        profile.ActivatedAtUtc = null;
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "reactivate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Reactivate_fails_if_not_approved()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.VerificationStatus = VerificationStatus.Draft;
        profile.IsOperationallyActive = false;
        profile.ActivatedAtUtc = DateTime.UtcNow.AddDays(-5);
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "reactivate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    [Fact]
    public async Task Reactivate_fails_if_already_active()
    {
        var profile = CreateApprovedRestaurantProfile();
        profile.IsOperationallyActive = true;
        profile.ActivatedAtUtc = DateTime.UtcNow.AddDays(-5);
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "reactivate", "test"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidState);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Reason trimming
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lifecycle_reason_is_trimmed_before_audit()
    {
        var profile = CreateApprovedRestaurantProfile();
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "  padded reason  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(a => a.Reason == "padded reason"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lifecycle_whitespace_only_reason_is_rejected_after_trim()
    {
        var profile = CreateApprovedRestaurantProfile();
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", "   "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.Required);
    }

    [Fact]
    public async Task Lifecycle_max_length_checked_on_trimmed_reason()
    {
        var profile = CreateApprovedRestaurantProfile();
        await using var db = CreateDbContext(ctx => ctx.RestaurantProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = new AdminRestaurantLifecycleCommandHandler(
            db, users, currentUser, audit, clock, Substitute.For<IAuthSessionService>());
        // 2001 chars after trimming (leading/trailing spaces are trimmed but inner content exceeds max)
        var longReason = "  " + new string('x', 2001) + "  ";
        var result = await handler.Handle(
            new AdminRestaurantLifecycleCommand(profile.Id, "activate", longReason), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Validation.MaxLength);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static RestaurantRegistration CreateRestaurantRegistration(Guid? ownerId) => new()
    {
        SubmittedByUserId = ownerId,
        LegalNameAr = "Test",
        LegalNameEn = "Test",
        TradeNameAr = "Test",
        TradeNameEn = "Test",
        ContactPhone = "0500000000",
        ContactEmail = "owner@test.com",
        CommercialRegistrationNumber = "CR12345",
        CommercialLicenseNumber = "CL12345",
        FoodLicenseNumber = "FL12345",
        CompanyCivilId = "123456789012",
        AuthorizedSignatoryCivilId = "987654321012",
        Iban = "KW74CBKU0000000000001234567890",
        BankAccountNumber = "1234567890",
        SwiftCode = "CBKUKWKW",
        Status = VerificationStatus.Draft,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static RestaurantProfile CreateApprovedRestaurantProfile() => new()
    {
        UserId = Guid.NewGuid(),
        LegalNameAr = "Test",
        LegalNameEn = "Test",
        TradeNameAr = "Test",
        TradeNameEn = "Test",
        VerificationStatus = VerificationStatus.Approved,
        IsOperationallyActive = false,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static IUserAccountService CreateUsersWithOwner(Guid ownerUserId, AccountStatus status)
    {
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(ownerUserId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                ownerUserId,
                "owner@test.com",
                null,
                "Owner",
                UserType.Restaurant,
                status,
                null,
                true,
                false,
                DateTime.UtcNow,
                null));
        users.ListUsersByTypeAsync(Arg.Any<UserType>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserAccountDto>());
        return users;
    }

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
