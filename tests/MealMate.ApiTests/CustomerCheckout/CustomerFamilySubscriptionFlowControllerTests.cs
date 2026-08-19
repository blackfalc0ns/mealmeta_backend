using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using FamilyPricingSettings = MealMate.Application.Features.ProgramsSubscriptions.FamilyPricingSettings;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerFamilySubscriptionFlowControllerTests
{
    [Fact]
    public async Task PutGroup_CreatesDraftWithManagerMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, null, null, "أحمد", UserType.Customer, AccountStatus.Active,
                null, false, false, DateTime.UtcNow, null));

        var controller = CreateController(db, userId, users);
        var result = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة أحمد"), CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<FamilyGroupScreenDto>().Subject;
        dto.Name.Should().Be("عائلة أحمد");
        dto.Members.Should().ContainSingle(m => m.Role == FamilyMemberRole.Manager);
        dto.Members[0].DisplayName.Should().Be("أحمد");
        dto.Members[0].RelationshipCode.Should().Be("self");
        dto.Status.Should().Be(FamilyGroupStatus.Draft);
    }

    [Fact]
    public async Task AddMember_RejectsFifthMember()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "Home",
            AddressLine = "Kuwait",
            IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        var controller = CreateController(db, userId);
        var created = await controller.UpsertGroup(new UpsertFamilyGroupRequest("عائلة"), CancellationToken.None);
        created.Should().BeOfType<OkObjectResult>();

        for (var i = 0; i < 3; i++)
        {
            var add = await controller.AddMember(NewMemberWrite($"Member {i + 2}", address.Id), CancellationToken.None);
            add.Should().BeOfType<OkObjectResult>();
        }

        var fifth = await controller.AddMember(NewMemberWrite("Member 5", address.Id), CancellationToken.None);
        var conflict = fifth.Should().BeOfType<ObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = conflict.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("family.max_members");
    }

    private static FamilyMemberWriteDto NewMemberWrite(string name, Guid addressId) => new(
        name,
        "spouse",
        FamilyMemberGender.Female,
        30,
        "balanced",
        FamilyActivityLevel.Moderate,
        addressId);

    private static CustomerFamilySubscriptionFlowController CreateController(
        AppDbContext db,
        Guid userId,
        IUserAccountService? users = null,
        IFamilyPricingSettingsReader? settings = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        users ??= Substitute.For<IUserAccountService>();
        settings ??= new StubFamilyPricingSettingsReader(new FamilyPricingSettings
        {
            MaxMembers = 4,
            DiscountPercent = 0.10m,
            MinMembersForDiscount = 2,
        });

        var handlers = new FamilyDraftHandlers(db, currentUser, users, settings);
        var sender = new DirectFamilyDraftSender(handlers, db);

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        messages.GetMessage(Arg.Any<string>(), arguments: Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
        var localizer = new ProblemDetailsLocalizer(messages);

        var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .AddSingleton(localizer)
            .BuildServiceProvider();

        return new CustomerFamilySubscriptionFlowController(currentUser)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services },
            },
        };
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private sealed class StubFamilyPricingSettingsReader(FamilyPricingSettings settings) : IFamilyPricingSettingsReader
    {
        public Task<FamilyPricingSettings> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(settings);
    }

    /// <summary>Dispatches family draft requests to handlers and persists successful commands (test stand-in for MediatR + UnitOfWork).</summary>
    private sealed class DirectFamilyDraftSender(FamilyDraftHandlers handlers, AppDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetFamilyDraftMetadataQuery q => await handlers.Handle(q, cancellationToken),
                GetCurrentFamilyGroupQuery q => await handlers.Handle(q, cancellationToken),
                UpsertFamilyGroupCommand c => await handlers.Handle(c, cancellationToken),
                AddFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                UpdateFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                DeleteFamilyMemberCommand c => await handlers.Handle(c, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };

            if (request is IBaseCommand && result is Result { IsSuccess: true })
            {
                await db.SaveChangesAsync(cancellationToken);
                // Mimic request-scoped DbContext: detach after commit so the next call reloads cleanly.
                db.ChangeTracker.Clear();
            }

            return (TResponse)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
