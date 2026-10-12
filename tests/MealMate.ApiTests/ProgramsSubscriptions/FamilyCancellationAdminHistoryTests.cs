using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api.Localization;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MealMate.ApiTests.ProgramsSubscriptions;

public sealed class FamilyCancellationAdminHistoryTests
{
    [Fact]
    public async Task CancellationFeedIncludesDurableFamilyRowsAndPreservesLegacyAndPagingShapes()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "cancel-history", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "cancel-history", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Family", Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "balanced" };
        group.Members.Add(member);
        var familySubscription = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = member.Id, Status = SubscriptionStatus.Cancelled,
            PaidAmount = new Money(20m, "KWD"), StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 20), DurationDays = 10 };
        var legacySubscription = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            Status = SubscriptionStatus.Cancelled, PaidAmount = new Money(10m, "KWD"),
            StartDate = new DateOnly(2026, 10, 10), EndDate = new DateOnly(2026, 10, 20), DurationDays = 10 };
        var settlement = new SubscriptionCancellation { SubscriptionId = familySubscription.Id, CustomerUserId = manager,
            FamilyGroupId = group.Id, SettlementKind = "FamilyCancellation", Status = "Settled", ActorUserId = manager,
            PayerCustomerUserId = manager, RequestedAtUtc = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 10, 9, 10, 1, 0, DateTimeKind.Utc), CountedDays = 2, UnusedDays = 8 };
        settlement.RefundCalculations.Add(new RefundCalculation { CancellationId = settlement.Id,
            RefundEligiblePaid = new Money(20m, "KWD"), UnusedCashValue = new Money(10m, "KWD"),
            CancellationFee = new Money(1m, "KWD"), FinalCashRefund = new Money(9m, "KWD"),
            CalculatedAtUtc = settlement.CompletedAtUtc.Value });
        var review = new SubscriptionCancellation { SubscriptionId = legacySubscription.Id, CustomerUserId = manager,
            FamilyGroupId = group.Id, SettlementKind = "FamilyCancellation", Status = "ReviewRequired",
            RequestedAtUtc = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc) };
        var legacy = new SubscriptionOperation { SubscriptionId = legacySubscription.Id, OperationType = "CANCELLATION_PROCESSED",
            NewStatus = SubscriptionStatus.Cancelled, PerformedByUserId = manager, PerformedByUserType = UserType.Admin,
            TimestampUtc = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc), Reason = "legacy cancellation" };
        db.AddRange(program, bundle, group, familySubscription, legacySubscription, settlement, review, legacy);
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IReadOnlyCollection<Guid>)ci[0]!).Select(id => new UserIdentityDto(id, "Manager", null, null)).ToList());
        var handler = new SubscriptionOperationsHandlers(db, Substitute.For<IAuditLogger>(), Substitute.For<IDateTimeProvider>(),
            Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>(), users);
        var query = await handler.Handle(new GetSubscriptionCancellationRequestsQuery(1, 20), CancellationToken.None);
        query.IsSuccess.Should().BeTrue();
        query.Value.TotalCount.Should().Be(3);
        query.Value.Items.Should().ContainSingle(x => x.IsFamilySettlement && x.Status == "Settled"
            && x.FinalCashRefund == 9m && x.CurrencyCode == "KWD" && x.PayerCustomerUserId == manager);
        query.Value.Items.Should().ContainSingle(x => x.IsFamilySettlement && x.Status == "ReviewRequired"
            && x.FinalCashRefund == null && x.CurrencyCode == null);
        query.Value.Items.Should().ContainSingle(x => !x.IsFamilySettlement && x.Status == nameof(SubscriptionStatus.Cancelled));

        var controller = CreateController(new DirectSender(query.Value));
        var legacyShape = (await controller.GetCancellationRequests(cancellationToken: CancellationToken.None))
            .Should().BeOfType<OkObjectResult>().Subject.Value;
        legacyShape.Should().BeAssignableTo<IReadOnlyList<AdminSubscriptionCancellationRequestDto>>();
        var pagedShape = (await controller.GetCancellationRequests(page: 1, cancellationToken: CancellationToken.None))
            .Should().BeOfType<OkObjectResult>().Subject.Value;
        pagedShape.Should().BeOfType<PagedResult<AdminSubscriptionCancellationRequestDto>>().Which.TotalCount.Should().Be(3);
    }

    private static SubscriptionLifecycleOperationsController CreateController(ISender sender)
    {
        var messages = Substitute.For<ILocalizedMessageProvider>();
        messages.GetMessage(Arg.Any<string>()).Returns(ci => (string)ci[0]!);
        messages.GetMessage(Arg.Any<string>(), arguments: Arg.Any<object[]>()).Returns(ci => (string)ci[0]!);
        var services = new ServiceCollection().AddSingleton<ISender>(sender)
            .AddSingleton(new ProblemDetailsLocalizer(messages)).BuildServiceProvider();
        return new SubscriptionLifecycleOperationsController(Substitute.For<ICurrentUser>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } },
        };
    }

    private sealed class DirectSender(PagedResult<AdminSubscriptionCancellationRequestDto> page) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request is GetSubscriptionCancellationRequestsQuery
                ? (Result<PagedResult<AdminSubscriptionCancellationRequestDto>>)page
                : throw new NotSupportedException(request.GetType().Name);
            return Task.FromResult((TResponse)result);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
