using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Influencer;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class InfluencerSelfLinkTests
{
    [Fact]
    public async Task LinkInfluencerCustomer_blocks_self_link_even_with_link_permission()
    {
        var userId = Guid.NewGuid();
        var profile = new InfluencerProfile
        {
            UserId = userId,
            DisplayName = "Influencer",
            InfluencerCode = "CODE1",
            VerificationStatus = VerificationStatus.Approved,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };

        await using var db = CreateDbContext(context => context.InfluencerProfiles.Add(profile));

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, "inf@test.com", null, "Influencer", UserType.Customer, AccountStatus.Active,
                null, true, false, DateTime.UtcNow, null));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);
        currentUser.HasPermission(PermissionCodes.InfluencerLink).Returns(true);

        var audit = Substitute.For<IAuditLogger>();
        var handler = new LinkInfluencerCustomerCommandHandler(db, users, currentUser, audit);
        var result = await handler.Handle(
            new LinkInfluencerCustomerCommand(profile.Id, userId, "test"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
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
