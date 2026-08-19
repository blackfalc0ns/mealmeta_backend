using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.ProgramsSubscriptions;

public sealed class AdminFamilyGroupsControllerTests
{
    [Fact]
    public async Task ListAndDetail_ActiveGroupWithTwoMembers_ReturnsExpectedCounts()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "عائلة الاختبار",
            Status = FamilyGroupStatus.Active,
            PaymentAnchorSubscriptionId = Guid.NewGuid(),
        };
        group.Members.Add(new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Manager,
            DisplayName = "أحمد",
            RelationshipCode = "self",
            Gender = FamilyMemberGender.Male,
            AgeYears = 30,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            DurationDays = 26,
            SelectedTier = RestaurantTier.Platinum,
        });
        group.Members.Add(new FamilyMember
        {
            FamilyGroupId = group.Id,
            Role = FamilyMemberRole.Member,
            DisplayName = "سارة",
            RelationshipCode = "spouse",
            Gender = FamilyMemberGender.Female,
            AgeYears = 28,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            DurationDays = 12,
            SelectedTier = RestaurantTier.Basic,
        });
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ids = (IReadOnlyCollection<Guid>)ci[0]!;
                return ids.Select(id => new UserIdentityDto(id, "أحمد المدير", null, null)).ToList();
            });

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc));

        var controller = CreateController(db, users, clock);

        var listResult = await controller.List(status: null, q: null, page: 1, pageSize: 20, CancellationToken.None);
        var listOk = listResult.Should().BeOfType<OkObjectResult>().Subject;
        var page = listOk.Value.Should().BeOfType<PagedResult<AdminFamilyGroupRowDto>>().Subject;
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].MemberCount.Should().Be(2);
        page.Items[0].Members.Should().HaveCount(2);

        var detailResult = await controller.GetById(group.Id, CancellationToken.None);
        var detailOk = detailResult.Should().BeOfType<OkObjectResult>().Subject;
        var detail = detailOk.Value.Should().BeOfType<AdminFamilyGroupRowDto>().Subject;
        detail.Members.Should().HaveCount(2);
        detail.Id.Should().Be(group.Id);
        detail.Name.Should().Be("عائلة الاختبار");
        detail.Status.Should().Be(FamilyGroupStatus.Active);
    }

    private static AdminFamilyGroupsController CreateController(
        AppDbContext db,
        IUserAccountService users,
        IDateTimeProvider clock)
    {
        var handlers = new AdminFamilyGroupHandlers(db, users, clock);
        var sender = new DirectAdminFamilyGroupsSender(handlers);

        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        messages.GetMessage(Arg.Any<string>(), arguments: Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
        var localizer = new ProblemDetailsLocalizer(messages);

        var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .AddSingleton(localizer)
            .BuildServiceProvider();

        return new AdminFamilyGroupsController
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

    private sealed class DirectAdminFamilyGroupsSender(AdminFamilyGroupHandlers handlers) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetAdminFamilyGroupsListQuery q => await handlers.Handle(q, cancellationToken),
                GetAdminFamilyGroupByIdQuery q => await handlers.Handle(q, cancellationToken),
                GetAdminFamilyGroupStatsQuery q => await handlers.Handle(q, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name),
            };
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
