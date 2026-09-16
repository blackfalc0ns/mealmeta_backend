using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Influencer;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class InfluencerPromotionNotificationTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task PromoteCustomer_SendsPushNotificationAndBrandedEmail()
    {
        await using var db = CreateInMemoryDb();
        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var pushNotifier = Substitute.For<ICustomerPushNotifier>();
        var emailSender = Substitute.For<ITransactionalEmailSender>();
        emailSender.SendAsync(Arg.Any<TransactionalEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("msg-123"));

        var customerId = Guid.NewGuid();
        var customer = new UserAccountDto(
            customerId,
            "sarah@example.com",
            "+96551234567",
            "سارة المؤثرة",
            UserType.Customer,
            AccountStatus.Active,
            null,
            true,
            false,
            DateTime.UtcNow,
            null);

        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>()).Returns(customer);
        users.AddToRoleAsync(customerId, AppRoles.Influencer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var service = new InfluencerAdminService(db, users, currentUser, audit, clock, pushNotifier, emailSender);

        var command = new PromoteCustomerToInfluencerCommand(
            customerId,
            "سارة في المطبخ",
            "SARAH2026",
            15.0m,
            ActivateNow: true);

        var result = await service.PromoteCustomerAsync(command, CancellationToken.None);
        await db.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.InfluencerCode.Should().Be("SARAH2026");

        // 1. Verify push notification was dispatched
        await pushNotifier.Received(1).NotifyInfluencerPromotedAsync(
            customerId,
            "SARAH2026",
            15.0m,
            Arg.Any<CancellationToken>());

        // 2. Verify branded transactional email was dispatched
        await emailSender.Received(1).SendAsync(
            Arg.Is<TransactionalEmailRequest>(r =>
                r.To == "sarah@example.com" &&
                r.Subject.Contains("MealMate") &&
                r.Html.Contains("SARAH2026") &&
                r.Html.Contains("15%")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoteCustomer_SucceedsEvenIfPushNotifierOrEmailFails()
    {
        await using var db = CreateInMemoryDb();
        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var pushNotifier = Substitute.For<ICustomerPushNotifier>();
        pushNotifier.NotifyInfluencerPromotedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Firebase service unavailable"));

        var emailSender = Substitute.For<ITransactionalEmailSender>();
        emailSender.SendAsync(Arg.Any<TransactionalEmailRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("SMTP connection failure"));

        var customerId = Guid.NewGuid();
        var customer = new UserAccountDto(
            customerId,
            "influencer@example.com",
            "+96559999999",
            "Ahmed Influencer",
            UserType.Customer,
            AccountStatus.Active,
            null,
            true,
            false,
            DateTime.UtcNow,
            null);

        users.FindByIdAsync(customerId, Arg.Any<CancellationToken>()).Returns(customer);
        users.AddToRoleAsync(customerId, AppRoles.Influencer, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var service = new InfluencerAdminService(db, users, currentUser, audit, clock, pushNotifier, emailSender);

        var command = new PromoteCustomerToInfluencerCommand(
            customerId,
            "Chef Ahmed",
            "AHMED50",
            20.0m,
            ActivateNow: true);

        var result = await service.PromoteCustomerAsync(command, CancellationToken.None);
        await db.SaveChangesAsync();

        // Promotion succeeds regardless of notification failure
        result.IsSuccess.Should().BeTrue();
        result.Value.InfluencerCode.Should().Be("AHMED50");

        var profile = await db.InfluencerProfiles.FirstOrDefaultAsync(x => x.UserId == customerId);
        profile.Should().NotBeNull();
        profile!.InfluencerCode.Should().Be("AHMED50");
    }

    [Fact]
    public async Task RegisterInfluencer_SendsWelcomeEmail()
    {
        await using var db = CreateInMemoryDb();
        var users = Substitute.For<IUserAccountService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var emailSender = Substitute.For<ITransactionalEmailSender>();
        emailSender.SendAsync(Arg.Any<TransactionalEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("msg-456"));

        var newUserId = Guid.NewGuid();
        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserAccountDto(
                newUserId,
                "chef_reem@example.com",
                "+96558888888",
                "Chef Reem",
                UserType.Customer,
                AccountStatus.Active,
                null,
                true,
                false,
                DateTime.UtcNow,
                null)));

        var service = new InfluencerAdminService(db, users, currentUser, audit, clock, null, emailSender);

        var command = new RegisterInfluencerCommand(
            DisplayName: "Chef Reem",
            Email: "chef_reem@example.com",
            Phone: "+96558888888",
            Password: "SecurePass123!",
            InfluencerCode: "REEM2026");

        var result = await service.RegisterInfluencerAsync(command, CancellationToken.None);
        await db.SaveChangesAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.InfluencerCode.Should().Be("REEM2026");

        await emailSender.Received(1).SendAsync(
            Arg.Is<TransactionalEmailRequest>(r =>
                r.To == "chef_reem@example.com" &&
                r.Subject.Contains("MealMate") &&
                r.Html.Contains("REEM2026")),
            Arg.Any<CancellationToken>());
    }
}
