using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerNotifications;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerNotifications;

/// <summary>
/// Customer in-app notification inbox: unread count contract, list paging,
/// and owner-scoped read commands (no mock business data — rows are created
/// through the same handlers the API exposes).
/// </summary>
public sealed class CustomerNotificationHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Unread_WithNoNotifications_ReturnsFalseAndZero()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);

        count.IsSuccess.Should().BeTrue();
        count.Value.HasUnreadNotifications.Should().BeFalse();
        count.Value.UnreadNotificationsCount.Should().Be(0);
    }

    [Fact]
    public async Task Unread_WithTwoUnread_ReturnsTrueAndTwo_AndListSharesPredicate()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        await SeedAsync(fx, fx.CustomerId, unread: 2, read: 1);

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.Value.UnreadNotificationsCount.Should().Be(2);
        count.Value.HasUnreadNotifications.Should().BeTrue();

        var list = await user.Handle(new GetCustomerNotificationsQuery(1, 20), CancellationToken.None);
        list.Value.TotalCount.Should().Be(3);
        list.Value.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task MarkRead_OneOfTwo_ReducesUnreadCountToOne()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        var ids = await SeedAsync(fx, fx.CustomerId, unread: 2);

        var read = await user.Handle(new MarkCustomerNotificationReadCommand(ids[0]), CancellationToken.None);
        read.IsSuccess.Should().BeTrue();

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.Value.UnreadNotificationsCount.Should().Be(1);
        count.Value.HasUnreadNotifications.Should().BeTrue();
    }

    [Fact]
    public async Task MarkAllRead_ZeroesUnreadCount()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        await SeedAsync(fx, fx.CustomerId, unread: 2, read: 1);

        var read = await user.Handle(new MarkAllCustomerNotificationsReadCommand(), CancellationToken.None);
        read.IsSuccess.Should().BeTrue();

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.Value.UnreadNotificationsCount.Should().Be(0);
        count.Value.HasUnreadNotifications.Should().BeFalse();
        (await fx.Db.CustomerNotifications.CountAsync(x => x.UserId == fx.CustomerId && x.ReadAtUtc != null)).Should().Be(3);
    }

    [Fact]
    public async Task OtherUsersNotifications_AreExcludedFromCountAndList()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        await SeedAsync(fx, Guid.NewGuid(), unread: 3);
        var mine = await SeedAsync(fx, fx.CustomerId, unread: 1);

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.Value.UnreadNotificationsCount.Should().Be(1);

        var list = await user.Handle(new GetCustomerNotificationsQuery(1, 20), CancellationToken.None);
        list.Value.TotalCount.Should().Be(1);
        list.Value.Items.Single().Id.Should().Be(mine[0]);
    }

    [Fact]
    public async Task MarkRead_ForAnotherUsersNotification_ReturnsNotFound()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        var strangerId = Guid.NewGuid();
        var stranger = await SeedAsync(fx, strangerId, unread: 1);

        var read = await user.Handle(new MarkCustomerNotificationReadCommand(stranger[0]), CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Code.Should().Be(MealMate.Application.Common.Localization.MessageCodes.Common.NotFound);
        (await fx.Db.CustomerNotifications.SingleAsync(x => x.Id == stranger[0])).ReadAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task MarkRead_RepeatedIsIdempotentAndKeepsFirstReadAtUtc()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        var ids = await SeedAsync(fx, fx.CustomerId, unread: 1);

        (await user.Handle(new MarkCustomerNotificationReadCommand(ids[0]), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var firstReadAt = await fx.Db.CustomerNotifications.AsNoTracking().SingleAsync(x => x.Id == ids[0]);

        fx.Clock.UtcNow.Returns(Now.AddHours(2));
        (await user.Handle(new MarkCustomerNotificationReadCommand(ids[0]), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var after = await fx.Db.CustomerNotifications.AsNoTracking().SingleAsync(x => x.Id == ids[0]);
        after.ReadAtUtc.Should().Be(firstReadAt.ReadAtUtc);
    }

    [Fact]
    public async Task MarkRead_ForMissingNotification_ReturnsNotFound()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();

        var read = await user.Handle(new MarkCustomerNotificationReadCommand(Guid.NewGuid()), CancellationToken.None);

        read.IsFailure.Should().BeTrue();
        read.Error.Code.Should().Be(MealMate.Application.Common.Localization.MessageCodes.Common.NotFound);
    }

    [Fact]
    public async Task List_OrdersNewestFirst_WithPagingMetadata()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        var first = await SeedAsync(fx, fx.CustomerId, unread: 1);
        var second = await SeedAsync(fx, fx.CustomerId, unread: 1);
        var third = await SeedAsync(fx, fx.CustomerId, unread: 1);

        var page1 = await user.Handle(new GetCustomerNotificationsQuery(1, 2), CancellationToken.None);
        page1.Value.TotalCount.Should().Be(3);
        page1.Value.Page.Should().Be(1);
        page1.Value.PageSize.Should().Be(2);
        page1.Value.TotalPages.Should().Be(2);
        page1.Value.HasNextPage.Should().BeTrue();
        page1.Value.HasPreviousPage.Should().BeFalse();
        page1.Value.Items.Select(x => x.Id).Should().Equal(first[0], second[0]);

        var page2 = await user.Handle(new GetCustomerNotificationsQuery(2, 2), CancellationToken.None);
        page2.Value.Items.Select(x => x.Id).Should().Equal(third[0]);
        page2.Value.HasNextPage.Should().BeFalse();
        page2.Value.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public async Task List_ExcludesSoftDeletedNotifications()
    {
        var fx = await CreateAsync();
        var user = fx.CreateCustomerHandler();
        var ids = await SeedAsync(fx, fx.CustomerId, unread: 2);

        var row = await fx.Db.CustomerNotifications.SingleAsync(x => x.Id == ids[0]);
        row.IsDeleted = true;
        await fx.Db.SaveChangesAsync();

        var list = await user.Handle(new GetCustomerNotificationsQuery(1, 20), CancellationToken.None);
        list.Value.TotalCount.Should().Be(1);
        list.Value.Items.Single().Id.Should().Be(ids[1]);

        var count = await user.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.Value.UnreadNotificationsCount.Should().Be(1);
    }

    [Fact]
    public async Task NonCustomer_IsRejected()
    {
        var fx = await CreateAsync();
        var restaurantUser = fx.CreateHandlerFor(UserType.Restaurant);

        var count = await restaurantUser.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.IsFailure.Should().BeTrue();
        count.Error.Code.Should().Be(MealMate.Application.Common.Localization.MessageCodes.Auth.Forbidden);

        var list = await restaurantUser.Handle(new GetCustomerNotificationsQuery(), CancellationToken.None);
        list.IsFailure.Should().BeTrue();

        var read = await restaurantUser.Handle(new MarkCustomerNotificationReadCommand(Guid.NewGuid()), CancellationToken.None);
        read.IsFailure.Should().BeTrue();

        var readAll = await restaurantUser.Handle(new MarkAllCustomerNotificationsReadCommand(), CancellationToken.None);
        readAll.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Unauthenticated_IsRejected()
    {
        var fx = await CreateAsync();
        var anonymous = fx.CreateHandlerFor(UserType.Customer, authenticated: false);

        var count = await anonymous.Handle(new GetCustomerUnreadNotificationsQuery(), CancellationToken.None);
        count.IsFailure.Should().BeTrue();
        count.Error.Code.Should().Be(MealMate.Application.Common.Localization.MessageCodes.Common.Unauthorized);
    }

    private static async Task<List<Guid>> SeedAsync(
        Fixture fx, Guid userId, int unread, int read = 0)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < unread; i++)
        {
            var row = NewNotification(userId, fx.Seed);
            row.CreatedAtUtc = Now.AddMinutes(-fx.Seed);
            fx.Seed++;
            fx.Db.CustomerNotifications.Add(row);
            ids.Add(row.Id);
        }

        for (var i = 0; i < read; i++)
        {
            var row = NewNotification(userId, fx.Seed);
            row.CreatedAtUtc = Now.AddMinutes(-fx.Seed);
            row.ReadAtUtc = Now.AddMinutes(-30);
            fx.Seed++;
            fx.Db.CustomerNotifications.Add(row);
            ids.Add(row.Id);
        }

        await fx.Db.SaveChangesAsync();
        return ids;
    }

    private static CustomerNotification NewNotification(Guid userId, int seed) => new()
    {
        UserId = userId,
        EventKey = "order.confirmed_by_restaurant",
        SubjectId = Guid.NewGuid(),
        SubscriptionId = Guid.NewGuid(),
        TitleAr = "تم تأكيد طلبك 👨‍🍳",
        TitleEn = "Order Confirmed",
        BodyAr = $"إشعار تجريبي {seed}",
        BodyEn = $"Test notification {seed}",
        DeepLink = "mealmate://orders",
    };

    private static async Task<Fixture> CreateAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new Fixture(db, clock);
    }

    private sealed record Fixture(AppDbContext Db, IDateTimeProvider Clock)
    {
        public Guid CustomerId { get; } = Guid.NewGuid();
        public int Seed;

        public CustomerNotificationHandlers CreateCustomerHandler()
            => CreateHandlerFor(UserType.Customer);

        public CustomerNotificationHandlers CreateHandlerFor(UserType userType, bool authenticated = true)
        {
            var user = Substitute.For<ICurrentUser>();
            user.IsAuthenticated.Returns(authenticated);
            user.UserId.Returns(authenticated && userType == UserType.Customer ? CustomerId : authenticated ? Guid.NewGuid() : (Guid?)null);
            user.UserType.Returns(userType);
            return new CustomerNotificationHandlers(Db, user, Clock);
        }
    }
}
