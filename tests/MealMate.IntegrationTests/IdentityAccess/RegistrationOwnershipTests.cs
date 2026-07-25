using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class RegistrationOwnershipTests
{
    [Fact]
    public async Task GetDriverRegistration_forbidden_for_other_user()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId);
        await using var db = CreateDbContext(context => context.DriverRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DriverRegApprove).Returns(false);
        currentUser.HasPermission(PermissionCodes.DriverRegView).Returns(false);

        var handler = new GetDriverRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDriverRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetDriverRegistration_allowed_for_owner()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId);
        await using var db = CreateDbContext(context => context.DriverRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(ownerId);

        var handler = new GetDriverRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDriverRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(registration.Id);
    }

    [Fact]
    public async Task SubmitRestaurantRegistration_forbidden_for_other_user()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));
        var audit = Substitute.For<IAuditLogger>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);

        var handler = new SubmitRestaurantRegistrationCommandHandler(db, currentUser, audit);
        var result = await handler.Handle(new SubmitRestaurantRegistrationCommand(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetRestaurantRegistration_allowed_for_super_admin()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);

        var handler = new GetRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetRestaurantRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetRestaurantRegistration_forbidden_for_non_admin_with_view_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.RestaurantOwner]);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);

        var handler = new GetRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetRestaurantRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetDriverRegistration_forbidden_for_non_admin_with_view_only()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateDriverRegistration(ownerId);
        await using var db = CreateDbContext(context => context.DriverRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.Driver]);
        currentUser.HasPermission(PermissionCodes.DriverRegApprove).Returns(false);
        currentUser.HasPermission(PermissionCodes.DriverRegView).Returns(true);

        var handler = new GetDriverRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDriverRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task GetRestaurantRegistration_allowed_for_view_only_admin()
    {
        var ownerId = Guid.NewGuid();
        var registration = CreateRestaurantRegistration(ownerId);
        await using var db = CreateDbContext(context => context.RestaurantRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.Roles.Returns([AppRoles.OperationsAdmin]);
        currentUser.HasPermission(PermissionCodes.RestaurantRegApprove).Returns(false);
        currentUser.HasPermission(PermissionCodes.RestaurantRegView).Returns(true);

        var handler = new GetRestaurantRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetRestaurantRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(registration.Id);
    }

    [Fact]
    public async Task GetDriverRegistration_forbidden_when_both_submitted_by_and_caller_user_id_null()
    {
        var registration = CreateDriverRegistration(null);
        await using var db = CreateDbContext(context => context.DriverRegistrations.Add(registration));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.DriverRegApprove).Returns(false);
        currentUser.HasPermission(PermissionCodes.DriverRegView).Returns(false);

        var handler = new GetDriverRegistrationQueryHandler(db, currentUser);
        var result = await handler.Handle(new GetDriverRegistrationQuery(registration.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    private static DriverRegistration CreateDriverRegistration(Guid? ownerId) => new()
    {
        SubmittedByUserId = ownerId,
        FullNameAr = "Test",
        FullNameEn = "Test",
        Phone = "0500000000",
        Status = VerificationStatus.Draft,
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
