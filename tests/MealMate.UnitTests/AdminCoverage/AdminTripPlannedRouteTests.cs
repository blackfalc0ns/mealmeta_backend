using System.Globalization;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminCoverage;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace MealMate.UnitTests.AdminCoverage;

public sealed class AdminTripPlannedRouteTests
{
    [Fact]
    public async Task OriginMode_DefaultsToDispatch_AndRoutesFromRestaurantOrigin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-DISPATCH-DEFAULT", TripStatus.Planned, fixture.AreaA.Id);
        var restaurant = await fixture.Db.RestaurantProfiles.SingleAsync(p => p.Id == trip.RestaurantId);
        restaurant.Latitude = 29.3m;
        restaurant.Longitude = 47.9m;
        var stops = await fixture.Db.TripStops.Where(s => s.TripId == trip.Id).OrderBy(s => s.SequenceNumber).ToListAsync();
        stops[0].Latitude = 29.31m; stops[0].Longitude = 47.91m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(29.3m, 47.9m, 29.31m, 47.91m, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RouteDirectionsResult(1000, 1, 120, 2, "leg-poly", false, 29.3m, 47.9m, 29.31m, 47.91m))));

        var handler = fixture.CreateRouteHandler(routing);
        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginMode.Should().Be("dispatch");
        result.Value.SourceLocationRecordedAtUtc.Should().BeNull();
        result.Value.Legs.Should().HaveCount(1);
        result.Value.Legs[0].Origin!.Latitude.Should().Be(29.3m);
        result.Value.Legs[0].Origin!.Longitude.Should().Be(47.9m);
        result.Value.Legs[0].Destination!.Latitude.Should().Be(29.31m);
    }

    [Fact]
    public async Task OriginMode_InvalidValue_ReturnsValidationError()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-INVALID-MODE", TripStatus.Planned, fixture.AreaA.Id);
        var handler = fixture.CreateRouteHandler(Substitute.For<IGoogleRoutingService>());

        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "helicopter"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("admin_coverage.invalid_origin_mode");
    }

    [Fact]
    public async Task OriginMode_CaseInsensitive_AcceptsRemaining()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-CASE-CHECK", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.34m, Longitude = 47.94m, RecordedAtUtc = Fixture.Now.AddSeconds(-10)
        });
        var stop = await fixture.Db.TripStops.SingleAsync(s => s.TripId == trip.Id);
        stop.Latitude = 29.35m; stop.Longitude = 47.95m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(29.34m, 47.94m, 29.35m, 47.95m, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RouteDirectionsResult(500, 1, 60, 2, "rem-poly", false, 29.34m, 47.94m, 29.35m, 47.95m))));

        var handler = fixture.CreateRouteHandler(routing);
        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "ReMaiNing"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginMode.Should().Be("remaining");
        result.Value.Legs.Should().HaveCount(1);
        result.Value.Legs[0].Origin!.Latitude.Should().Be(29.34m);
        result.Value.Legs[0].Origin!.Longitude.Should().Be(47.94m);
    }

    [Fact]
    public async Task OriginMode_Remaining_SkipsDeliveredStops_AndRoutesFromGpsToFirstActionable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-REMAINING-ACTIONABLE", TripStatus.InProgress, fixture.AreaA.Id, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        var gpsTime = Fixture.Now.AddSeconds(-20);
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = gpsTime
        });

        var stops = await fixture.Db.TripStops.Where(s => s.TripId == trip.Id).OrderBy(s => s.SequenceNumber).ToListAsync();
        stops[0].Status = TripStopStatus.Delivered;
        stops[0].Latitude = 29.31m; stops[0].Longitude = 47.91m;

        stops[1].Status = TripStopStatus.Pending;
        stops[1].Latitude = 29.35m; stops[1].Longitude = 47.95m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(29.33m, 47.93m, 29.35m, 47.95m, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RouteDirectionsResult(800, 1, 90, 2, "actionable-poly", false, 29.33m, 47.93m, 29.35m, 47.95m))));

        var handler = fixture.CreateRouteHandler(routing);
        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginMode.Should().Be("remaining");
        result.Value.SourceLocationRecordedAtUtc.Should().Be(gpsTime);
        result.Value.RemainingStopIds.Should().ContainSingle(id => id == stops[1].Id);
        result.Value.Legs.Should().HaveCount(1);
        result.Value.Legs[0].Origin!.Latitude.Should().Be(29.33m);
        result.Value.Legs[0].ToStopId.Should().Be(stops[1].Id);
    }

    [Fact]
    public async Task FreshnessBoundary_120SecondsOld_IsFresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-FRESH-120", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = Fixture.Now.AddSeconds(-120)
        });
        var stop = await fixture.Db.TripStops.SingleAsync(s => s.TripId == trip.Id);
        stop.Latitude = 29.35m; stop.Longitude = 47.95m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RouteDirectionsResult(500, 1, 60, 2, "poly", false, 0, 0, 0, 0))));

        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ErrorCode.Should().BeNull();
        result.Value.Legs.Should().HaveCount(1);
    }

    [Fact]
    public async Task FreshnessBoundary_121SecondsOld_IsStale()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-STALE-121", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        var staleTime = Fixture.Now.AddSeconds(-121);
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = staleTime
        });
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPartial.Should().BeTrue();
        result.Value.ErrorCode.Should().Be("driver_gps_stale");
        result.Value.Legs.Should().BeEmpty();
        result.Value.SourceLocationRecordedAtUtc.Should().Be(staleTime);
        await routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task FreshnessBoundary_30SecondsFuture_IsFresh()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-FUTURE-30", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = Fixture.Now.AddSeconds(30)
        });
        var stop = await fixture.Db.TripStops.SingleAsync(s => s.TripId == trip.Id);
        stop.Latitude = 29.35m; stop.Longitude = 47.95m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new RouteDirectionsResult(500, 1, 60, 2, "poly", false, 0, 0, 0, 0))));

        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ErrorCode.Should().BeNull();
        result.Value.Legs.Should().HaveCount(1);
    }

    [Fact]
    public async Task FreshnessBoundary_31SecondsFuture_IsInvalid()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-FUTURE-31", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = Fixture.Now.AddSeconds(31)
        });
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPartial.Should().BeTrue();
        result.Value.ErrorCode.Should().Be("driver_gps_invalid");
        result.Value.Legs.Should().BeEmpty();
        await routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task MissingGps_ReturnsExplicitPartialUnavailable_NoStraightLine()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-MISSING-GPS", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        // No delivery tracking points added
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsPartial.Should().BeTrue();
        result.Value.ErrorCode.Should().Be("driver_gps_missing");
        result.Value.Legs.Should().BeEmpty();
        await routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task CacheFingerprint_ChangesWithNewGpsOrStopStatus()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-CACHE-FINGERPRINT", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.33m, Longitude = 47.93m, RecordedAtUtc = Fixture.Now.AddSeconds(-30)
        });
        var stop = await fixture.Db.TripStops.SingleAsync(s => s.TripId == trip.Id);
        stop.Latitude = 29.35m; stop.Longitude = 47.95m;
        await fixture.Db.SaveChangesAsync();

        var routing = Substitute.For<IGoogleRoutingService>();
        var calls = 0;
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                return Task.FromResult(Result.Success(new RouteDirectionsResult(500, 1, 60, 2, $"poly-{calls}", false, 0, 0, 0, 0)));
            });

        var handler = fixture.CreateRouteHandler(routing);
        var first = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);
        first.IsSuccess.Should().BeTrue();
        calls.Should().Be(1);

        // Second call with same state should hit cache
        var second = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        calls.Should().Be(1);

        // Add a newer GPS point
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id, DriverProfileId = driverId, Latitude = 29.34m, Longitude = 47.94m, RecordedAtUtc = Fixture.Now.AddSeconds(-10)
        });
        await fixture.Db.SaveChangesAsync();

        // Third call must bypass cache because GPS timestamp and coordinate changed
        var third = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "remaining"), CancellationToken.None);
        third.IsSuccess.Should().BeTrue();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task UnauthorizedTrip_ReturnsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-UNAUTHORIZED", TripStatus.Planned, fixture.AreaC.Id); // AreaC is unauthorized
        var handler = fixture.CreateRouteHandler(Substitute.For<IGoogleRoutingService>());

        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id, "dispatch"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("not_found");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateOnly ServiceDate = new(2026, 10, 3);
        public static readonly DateTime Now = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);
        public AppDbContext Db { get; }
        public Country Country { get; }
        public Tenant Tenant { get; }
        public Region GovernorateA { get; }
        public Region GovernorateB { get; }
        public Region AreaA { get; }
        public Region AreaB { get; }
        public Region AreaC { get; }
        private readonly List<Guid> scopeIds = [];
        private readonly ICurrentUser user;
        private readonly IDateTimeProvider clock;

        private Fixture(AppDbContext db, Country country, Tenant tenant, Region govA, Region govB, Region areaA, Region areaB, Region areaC, ICurrentUser user, IDateTimeProvider clock)
            => (Db, Country, Tenant, GovernorateA, GovernorateB, AreaA, AreaB, AreaC, this.user, this.clock)
                = (db, country, tenant, govA, govB, areaA, areaB, areaC, user, clock);

        public static async Task<Fixture> CreateAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
            var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "KW", IsActive = true };
            var tenant = new Tenant { Code = "TEN-KW", NameAr = "الكويت", NameEn = "Kuwait", CountryId = country.Id, Country = country, Domain = "kw.mealmate.test", CurrencyCode = "KWD", TimeZoneId = "Asia/Kuwait", Status = TenantStatus.Active, IsActive = true };
            var govA = NewRegion(country.Id, RegionType.Governorate, null, "Capital");
            var govB = NewRegion(country.Id, RegionType.Governorate, null, "Hawalli");
            var areaA = NewRegion(country.Id, RegionType.Area, govA.Id, "Area A");
            var areaB = NewRegion(country.Id, RegionType.Area, govB.Id, "Area B");
            var areaC = NewRegion(country.Id, RegionType.Area, govB.Id, "Area C");
            var user = Substitute.For<ICurrentUser>();
            user.IsAuthenticated.Returns(true);
            user.UserId.Returns(Guid.NewGuid());
            user.UserType.Returns(UserType.Admin);
            user.Roles.Returns([AppRoles.CountryAdmin]);
            user.TenantIds.Returns([tenant.Id]);
            user.AccessScopeIds.Returns(_ => []);
            user.HasPermission(PermissionCodes.AdminCoverageOperationsView).Returns(true);
            user.HasPermission(PermissionCodes.RestaurantRegView).Returns(false);
            user.IsInRole(AppRoles.SuperAdmin).Returns(false);
            db.Countries.Add(country); db.Tenants.Add(tenant); db.Regions.AddRange(govA, govB, areaA, areaB, areaC);
            await db.SaveChangesAsync();
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(Now);
            var fixture = new Fixture(db, country, tenant, govA, govB, areaA, areaB, areaC, user, clock);
            await fixture.AddGrantAsync(areaA.Id);
            return fixture;
        }

        public async Task AddGrantAsync(Guid regionId)
        {
            var scope = new AccessScope { Name = $"Region-{regionId:N}", ScopeType = AccessScopeType.Region, CountryId = Country.Id, TenantId = Tenant.Id, RegionId = regionId, IsActive = true };
            Db.AccessScopes.Add(scope); await Db.SaveChangesAsync(); scopeIds.Add(scope.Id);
            user.AccessScopeIds.Returns(scopeIds.ToArray());
        }

        public async Task<Trip> AddTripAsync(string code, TripStatus status, params Guid[] stopRegionIds)
        {
            var restaurantUser = new ApplicationUser { UserName = $"restaurant-{code}", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
            var restaurant = new RestaurantProfile { UserId = restaurantUser.Id, CountryId = Country.Id, PrimaryRegionId = stopRegionIds.FirstOrDefault(), TradeNameAr = code, TradeNameEn = code, LegalNameAr = code, LegalNameEn = code, IsOperationallyActive = true };
            var trip = new Trip { RestaurantId = restaurant.Id, PlannedDate = ServiceDate, Code = code, Status = status };
            Db.Users.Add(restaurantUser); Db.RestaurantProfiles.Add(restaurant); Db.Trips.Add(trip);
            var sequence = 1;
            foreach (var regionId in stopRegionIds)
            {
                var day = new SubscriptionDay { SubscriptionId = Guid.NewGuid(), ServiceDate = ServiceDate, RestaurantId = restaurant.Id, DeliveryRegionId = regionId };
                Db.SubscriptionDays.Add(day);
                Db.TripStops.Add(new TripStop { TripId = trip.Id, SubscriptionDayId = day.Id, OrderCode = $"ORDER-{code}-{sequence}", CustomerMaskedId = "masked", ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", SequenceNumber = sequence++, Status = TripStopStatus.Pending });
            }
            await Db.SaveChangesAsync();
            return trip;
        }

        public GetAdminTripPlannedRouteQueryHandler CreateRouteHandler(IGoogleRoutingService routing)
            => new(Db, Access, routing, new MemoryCache(new MemoryCacheOptions()), clock);
        private AdminCoverageAccess Access => new(Db, user, new KuwaitTenantResolver(Db, user));
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        private static Region NewRegion(Guid countryId, RegionType type, Guid? parentId, string name) => new() { CountryId = countryId, Type = type, ParentRegionId = parentId, Code = $"{name}-{Guid.NewGuid():N}", NameAr = name, NameEn = name, IsActive = true };
    }
}
