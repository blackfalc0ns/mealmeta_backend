using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RegisterRestaurantDeliveryManagerCommandTests
{
    [Fact]
    public void Validator_ShouldFail_WhenRequiredFieldsAreMissingOrExpiriesInPast()
    {
        var command = new RegisterRestaurantDeliveryManagerCommand(
            FullName: "",
            Phone: "",
            Email: "not-an-email",
            NationalId: "",
            NationalIdExpiry: DateTime.UtcNow.AddDays(-1), // past date
            ContractExpiry: DateTime.UtcNow.AddDays(-2),   // past date
            CoverageType: DeliveryManagerCoverageType.Governorate,
            CoverageScopeId: "",
            CoverageScopeName: "",
            RestaurantId: null,
            NationalIdFrontStorageKey: "",
            NationalIdBackStorageKey: "",
            ContractStorageKey: "");

        var validator = new RegisterRestaurantDeliveryManagerCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.FullName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ContractExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdFrontStorageKey));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdBackStorageKey));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ContractStorageKey));
    }

    [Fact]
    public void Validator_ShouldPass_WhenAllFieldsAndFutureExpiriesAreValid()
    {
        var command = new RegisterRestaurantDeliveryManagerCommand(
            FullName: "نورة خالد العجمي",
            Phone: "+96551234567",
            Email: "noura.dispatch@burgerfactory.kw",
            NationalId: "296051201234",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            ContractExpiry: DateTime.UtcNow.AddYears(1),
            CoverageType: DeliveryManagerCoverageType.Governorate,
            CoverageScopeId: "gov-hawally",
            CoverageScopeName: "حولي",
            RestaurantId: Guid.NewGuid(),
            NationalIdFrontStorageKey: "uploads/delivery-managers/id-front.jpg",
            NationalIdBackStorageKey: "uploads/delivery-managers/id-back.jpg",
            ContractStorageKey: "uploads/delivery-managers/contract.pdf");

        var validator = new RegisterRestaurantDeliveryManagerCommandValidator();
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

    private static RegisterRestaurantDeliveryManagerCommand ValidCommand(
        Guid? restaurantId = null,
        string? phone = null,
        string? email = null,
        string? nationalId = null) => new(
        FullName: "نورة خالد العجمي",
        Phone: phone ?? "+96551234567",
        Email: email ?? "noura.dispatch@burgerfactory.kw",
        NationalId: nationalId ?? "296051201234",
        NationalIdExpiry: DateTime.UtcNow.AddYears(2),
        ContractExpiry: DateTime.UtcNow.AddYears(1),
        CoverageType: DeliveryManagerCoverageType.Governorate,
        CoverageScopeId: "gov-hawally",
        CoverageScopeName: "حولي",
        RestaurantId: restaurantId,
        NationalIdFrontStorageKey: "uploads/delivery-managers/id-front.jpg",
        NationalIdBackStorageKey: "uploads/delivery-managers/id-back.jpg",
        ContractStorageKey: "uploads/delivery-managers/contract.pdf");

    private sealed class HandlerHarness
    {
        public AppDbContext Db { get; } = CreateDb();
        public IUserAccountService Users { get; } = Substitute.For<IUserAccountService>();
        public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
        public IAuditLogger Audit { get; } = Substitute.For<IAuditLogger>();
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public Guid OwnerUserId { get; } = Guid.NewGuid();
        public Guid NewUserId { get; } = Guid.NewGuid();
        public CreateUserRequest? CapturedCreateRequest { get; private set; }
        public AuditEntry? CapturedAuditEntry { get; private set; }

        public RegisterRestaurantDeliveryManagerCommandHandler BuildHandler()
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc));

            CurrentUser.UserId.Returns(OwnerUserId);
            CurrentUser.RestaurantId.Returns(RestaurantId);

            Users.CreateAsync(Arg.Do<CreateUserRequest>(r => CapturedCreateRequest = r), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var request = (CreateUserRequest)callInfo[0];
                    return Result<UserAccountDto>.Success(new UserAccountDto(
                        NewUserId,
                        request.Email,
                        request.PhoneNumber,
                        request.FullName,
                        request.UserType,
                        request.AccountStatus,
                        request.RestaurantId,
                        EmailConfirmed: false,
                        PhoneNumberConfirmed: false,
                        DateTime.UtcNow,
                        LastLoginAtUtc: null,
                        HasSetPassword: false));
                });

            Audit.LogAsync(Arg.Do<AuditEntry>(e => CapturedAuditEntry = e), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            return new RegisterRestaurantDeliveryManagerCommandHandler(
                Users, Db, CurrentUser, Audit, clock);
        }
    }

    [Fact]
    public async Task Handle_ValidRegistration_ProvisionsActiveUserAndApprovedRegistration_WithoutAdminDecision()
    {
        var harness = new HandlerHarness();
        var handler = harness.BuildHandler();

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        harness.CapturedCreateRequest.Should().NotBeNull();
        harness.CapturedCreateRequest!.AccountStatus.Should().Be(AccountStatus.Active,
            "registration must provision an immediately eligible account without an admin approval step");
        harness.CapturedCreateRequest.RestaurantId.Should().Be(harness.RestaurantId);

        await harness.Users.Received(1).AddToRoleAsync(harness.NewUserId, AppRoles.DeliveryManager, Arg.Any<CancellationToken>());

        var registration = await harness.Db.DeliveryManagerRegistrations.SingleAsync();
        registration.Status.Should().Be(VerificationStatus.Approved,
            "registration eligibility is completed automatically by the restaurant onboarding");
        registration.ReviewedByUserId.Should().BeNull("no admin reviewed this registration");
        registration.ReviewedAtUtc.Should().BeNull("no admin reviewed this registration");
        registration.ResultingUserId.Should().Be(harness.NewUserId);
        registration.RestaurantId.Should().Be(harness.RestaurantId);

        (await harness.Db.Documents.CountAsync(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id))
            .Should().Be(3);

        (await harness.Db.DocumentVerifications.CountAsync()).Should().Be(0,
            "automatic onboarding must never fabricate document approvals");

        await harness.Users.DidNotReceive().UpdateAccountStatusAsync(
            Arg.Any<Guid>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());

        harness.CapturedAuditEntry.Should().NotBeNull();
        harness.CapturedAuditEntry!.Action.Should().Be("DeliveryManagerProvisionedByRestaurant",
            "the audit trail must describe automatic restaurant onboarding, not an admin approval");
    }

    [Theory]
    [InlineData("auth.phone_already_registered", "phone")]
    [InlineData("auth.email_already_registered", "email")]
    [InlineData("auth.national_id_already_registered", "nationalId")]
    public async Task Handle_DuplicateIdentity_ReturnsConflict(string expectedCode, string which)
    {
        var harness = new HandlerHarness();
        harness.Db.DeliveryManagerRegistrations.Add(new DeliveryManagerRegistration
        {
            RestaurantId = harness.RestaurantId,
            FullName = "مدير سابق",
            Phone = "+96555551111",
            Email = "previous.dispatch@burgerfactory.kw",
            NationalId = "296051299999",
            Status = VerificationStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await harness.Db.SaveChangesAsync();
        var handler = harness.BuildHandler();

        var command = which switch
        {
            "phone" => ValidCommand(phone: "+96555551111"),
            "email" => ValidCommand(email: "previous.dispatch@burgerfactory.kw"),
            _ => ValidCommand(nationalId: "296051299999"),
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be(expectedCode);
        harness.Db.DeliveryManagerRegistrations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_OwnerRestaurantLinkage_RegistersUnderOwnersRestaurant_IgnoringForeignRestaurantId()
    {
        var harness = new HandlerHarness();
        var handler = harness.BuildHandler();
        var foreignRestaurantId = Guid.NewGuid();

        var result = await handler.Handle(ValidCommand(restaurantId: foreignRestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.CapturedCreateRequest!.RestaurantId.Should().Be(harness.RestaurantId);
        (await harness.Db.DeliveryManagerRegistrations.SingleAsync()).RestaurantId.Should().Be(harness.RestaurantId);
    }
}
