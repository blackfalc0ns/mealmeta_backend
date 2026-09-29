using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Restaurant;

public class RestaurantBranchOwnershipTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly Guid _restaurantA = Guid.NewGuid();
    private readonly Guid _restaurantB = Guid.NewGuid();

    public RestaurantBranchOwnershipTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _currentUser = Substitute.For<ICurrentUser>();
    }

    [Fact]
    public async Task CreateBranch_WhenUserBelongsToDifferentRestaurant_ReturnsForbidden()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantA);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "owner" });

        var handler = new CreateRestaurantBranchCommandHandler(_db, _currentUser);
        var command = new CreateRestaurantBranchCommand(
            _restaurantB, // Targets Restaurant B while user belongs to Restaurant A!
            "BR-01",
            "فرع جديد",
            "New Branch",
            "عنوان",
            "Address",
            "محافظة",
            "Gov",
            "99999999");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("restaurant.admin_scope_required", "restaurant.forbidden", "Branch.Forbidden");
    }

    [Fact]
    public async Task CreateBranch_WhenStaffLacksTeamManagePermission_ReturnsForbidden()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantA);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "kitchen" }); // ordinary staff without team management
        _currentUser.Permissions.Returns(Array.Empty<string>());

        var handler = new CreateRestaurantBranchCommandHandler(_db, _currentUser);
        var command = new CreateRestaurantBranchCommand(
            _restaurantA,
            "BR-01",
            "فرع جديد",
            "New Branch",
            "عنوان",
            "Address",
            "محافظة",
            "Gov",
            "99999999");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("restaurant.forbidden", "Branch.Forbidden", "branch.forbidden");
    }

    [Fact]
    public async Task ToggleBranch_WhenBranchBelongsToDifferentRestaurant_ReturnsNotFoundOrForbidden()
    {
        // Seed branch belonging to Restaurant B
        var branch = new RestaurantBranch
        {
            RestaurantRegistrationId = _restaurantB,
            BranchCode = "BR-B1",
            NameAr = "فرع ب",
            NameEn = "Branch B",
            AddressAr = "العنوان",
            AddressEn = "Address",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = "99999999",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.RestaurantBranches.Add(branch);
        await _db.SaveChangesAsync(CancellationToken.None);

        // Caller is from Restaurant A
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantA);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "owner" });

        var handler = new ToggleBranchStatusCommandHandler(_db, _currentUser);
        var command = new ToggleBranchStatusCommand(branch.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("Branch.NotFound", "Branch.Forbidden", "restaurant.forbidden", "restaurant.admin_scope_required");
    }

    [Fact]
    public async Task ListBranches_WhenQueryingDifferentRestaurantWithoutAdmin_ReturnsForbidden()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.RestaurantId.Returns(_restaurantA);
        _currentUser.UserType.Returns(UserType.Restaurant);
        _currentUser.Roles.Returns(new[] { "owner" });

        var handler = new ListRestaurantBranchesQueryHandler(_db, _currentUser);
        var query = new ListRestaurantBranchesQuery(_restaurantB);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("restaurant.admin_scope_required", "restaurant.forbidden");
    }
}
