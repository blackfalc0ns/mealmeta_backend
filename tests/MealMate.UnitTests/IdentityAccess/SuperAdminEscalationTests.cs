using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.AdminUsers;
using MealMate.Application.Features.IdentityAccess.Rbac;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class SuperAdminEscalationTests
{
    [Fact]
    public async Task InviteAdmin_rejects_super_admin_role_from_non_super_admin()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var otp = Substitute.For<IOtpService>();
        var clock = Substitute.For<IDateTimeProvider>();

        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);

        var handler = new InviteAdminCommandHandler(users, db, currentUser, audit, otp, clock);
        var result = await handler.Handle(
            new InviteAdminCommand("new@test.com", "New Admin", AppRoles.SuperAdmin, null, "test"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task AssignUserRole_rejects_super_admin_from_non_super_admin()
    {
        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();

        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);

        var handler = new AssignUserRoleCommandHandler(users, currentUser, audit);
        var result = await handler.Handle(
            new AssignUserRoleCommand(Guid.NewGuid(), AppRoles.SuperAdmin, "test"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }
}
