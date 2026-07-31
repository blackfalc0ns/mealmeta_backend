using System.Collections;
using System.Linq.Expressions;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.AdminUsers;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class AdminInviteOtpTests
{
    [Fact]
    public async Task InviteAdmin_persists_invitation_and_does_not_create_user()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var invitations = CreateDbSet(Array.Empty<AdminInvitation>());
        db.AdminInvitations.Returns(invitations);
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var otp = Substitute.For<IOtpService>();
        var clock = Substitute.For<IDateTimeProvider>();
        AdminInvitation? addedInvitation = null;

        invitations
            .When(set => set.Add(Arg.Any<AdminInvitation>()))
            .Do(call => addedInvitation = call.Arg<AdminInvitation>());
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        clock.UtcNow.Returns(DateTime.UtcNow);
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new InviteAdminCommandHandler(users, db, currentUser, audit, otp, clock);
        var result = await handler.Handle(
            new InviteAdminCommand("NEW@test.com", "New Admin", AppRoles.OperationsAdmin, null, "test"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        addedInvitation.Should().NotBeNull();
        addedInvitation!.NormalizedEmail.Should().Be("new@test.com");
        addedInvitation.Status.Should().Be(AdminInvitationStatus.Pending);
        result.Value.UserId.Should().Be(addedInvitation.Id);
        await users.DidNotReceive().CreateAsync(
            Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>());
        await users.DidNotReceive().AddToRoleAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(request =>
                request.Purpose == OtpPurposes.AdminInvite
                && request.UserId == addedInvitation.Id
                && request.TemplateVariables != null
                && request.TemplateVariables["recipient_name"] == "New Admin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAdminInvite_invalid_otp_does_not_create_user()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();

        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation("auth.otp_invalid", "Invalid OTP."));

        var handler = new AcceptAdminInviteCommandHandler(users, db, otp, audit, clock);
        var result = await handler.Handle(
            new AcceptAdminInviteCommand("new@test.com", "000000", "NewPassword1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await users.DidNotReceive().CreateAsync(
            Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAdminInvite_creates_active_admin_after_otp_and_applies_invitation()
    {
        var scopeId = Guid.NewGuid();
        var invitation = new AdminInvitation
        {
            NormalizedEmail = "new@test.com",
            FullName = "New Admin",
            RoleName = AppRoles.OperationsAdmin,
            AccessScopeId = scopeId,
            InvitedByUserId = Guid.NewGuid(),
            Reason = "test",
            Status = AdminInvitationStatus.Pending,
            InvitedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };
        var createdUserId = Guid.NewGuid();
        var createdUser = new UserAccountDto(
            createdUserId,
            invitation.NormalizedEmail,
            null,
            invitation.FullName,
            UserType.Admin,
            AccountStatus.Active,
            null,
            false,
            false,
            DateTime.UtcNow,
            null);

        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var invitations = CreateDbSet([invitation]);
        db.AdminInvitations.Returns(invitations);
        var userScopes = Substitute.For<DbSet<UserScope>>();
        db.UserScopes.Returns(userScopes);
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        var acceptedAt = DateTime.UtcNow;
        clock.UtcNow.Returns(acceptedAt);

        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(createdUser));
        users.AddToRoleAsync(createdUserId, invitation.RoleName, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new AcceptAdminInviteCommandHandler(users, db, otp, audit, clock);
        var result = await handler.Handle(
            new AcceptAdminInviteCommand("NEW@test.com", "123456", "NewPassword1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _ = otp.VerifyAsync(
                Arg.Is<OtpVerifyRequest>(request => request.Purpose == OtpPurposes.AdminInvite),
                Arg.Any<CancellationToken>());
            _ = users.CreateAsync(
                Arg.Is<CreateUserRequest>(request =>
                    request.Email == invitation.NormalizedEmail
                    && request.Password == "NewPassword1!"
                    && request.UserType == UserType.Admin
                    && request.AccountStatus == AccountStatus.Active),
                Arg.Any<CancellationToken>());
        });
        await users.Received(1).AddToRoleAsync(
            createdUserId, invitation.RoleName, Arg.Any<CancellationToken>());
        await users.Received(1).SetEmailConfirmedAsync(
            createdUserId, true, Arg.Any<CancellationToken>());
        userScopes.Received(1).Add(Arg.Is<UserScope>(scope =>
            scope.UserId == createdUserId && scope.AccessScopeId == scopeId));
        invitation.Status.Should().Be(AdminInvitationStatus.Accepted);
        invitation.AcceptedAtUtc.Should().Be(acceptedAt);
        invitation.AcceptedUserId.Should().Be(createdUserId);
    }

    private static DbSet<T> CreateDbSet<T>(IEnumerable<T> source)
        where T : class
    {
        var queryable = source.AsQueryable();
        var set = Substitute.For<DbSet<T>, IQueryable<T>, IAsyncEnumerable<T>>();
        ((IQueryable<T>)set).Provider.Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        ((IQueryable<T>)set).Expression.Returns(queryable.Expression);
        ((IQueryable<T>)set).ElementType.Returns(queryable.ElementType);
        ((IQueryable<T>)set).GetEnumerator().Returns(_ => queryable.GetEnumerator());
        ((IAsyncEnumerable<T>)set).GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(call => new TestAsyncEnumerator<T>(
                queryable.GetEnumerator(),
                call.Arg<CancellationToken>()));
        return set;
    }

    private sealed class TestAsyncQueryProvider<TEntity>(IQueryProvider inner)
        : IAsyncQueryProvider
    {
        public IQueryable CreateQuery(Expression expression)
            => new TestAsyncEnumerable<TEntity>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
            => new TestAsyncEnumerable<TElement>(expression);

        public object? Execute(Expression expression)
            => inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression)
            => inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var resultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(
                    nameof(IQueryProvider.Execute),
                    1,
                    [typeof(Expression)])!
                .MakeGenericMethod(resultType)
                .Invoke(inner, [expression]);
            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T>(Expression expression)
        : EnumerableQuery<T>(expression), IAsyncEnumerable<T>, IQueryable<T>
    {
        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new TestAsyncEnumerator<T>(
                ((IEnumerable<T>)this).GetEnumerator(),
                cancellationToken);
    }

    private sealed class TestAsyncEnumerator<T>(
        IEnumerator<T> inner,
        CancellationToken cancellationToken) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(inner.MoveNext());
        }

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
