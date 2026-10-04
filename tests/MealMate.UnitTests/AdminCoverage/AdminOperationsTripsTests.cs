using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.AdminCoverage;
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

public sealed class AdminOperationsTripsTests
{
    [Fact]
    public async Task Trips_AuthorizeEveryStopAcrossUnionOfGrantsBeforeTotalsAndPaging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var allowedAB = await fixture.AddTripAsync("TRIP-AB", TripStatus.Assigned, fixture.AreaA.Id, fixture.AreaB.Id);
        var deniedAC = await fixture.AddTripAsync("TRIP-AC", TripStatus.InProgress, fixture.AreaA.Id, fixture.AreaC.Id);
        await fixture.AddGrantAsync(fixture.AreaB.Id);
        var handler = fixture.CreateTripsHandler();

        var result = await handler.Handle(new GetAdminOperationsTripsQuery(
            CountryCode: "KW", ServiceDate: Fixture.ServiceDate, GovernorateId: null, RestaurantId: null,
            Search: null, TripStatus: null, Page: 1, PageSize: 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(item => item.Id == allowedAB.Id);
        result.Value.Items.Should().NotContain(item => item.Id == deniedAC.Id);
        result.Value.InProgressCount.Should().Be(0);
        result.Value.ScheduledCount.Should().Be(1);
    }

    [Fact]
    public async Task Trips_GovernorateFilterRequiresAllStopsInsideThatGovernorate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCountryGrantAsync();
        var mixedGovernorates = await fixture.AddTripAsync("TRIP-MIXED", TripStatus.InProgress, fixture.AreaA.Id, fixture.AreaB.Id);
        var sameGovernorate = await fixture.AddTripAsync("TRIP-IN-GOV", TripStatus.InProgress, fixture.AreaA.Id);
        var handler = fixture.CreateTripsHandler();

        var result = await handler.Handle(new GetAdminOperationsTripsQuery(
            "KW", Fixture.ServiceDate, fixture.GovernorateA.Id, null, null, null, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(item => item.Id == sameGovernorate.Id);
        result.Value.Items.Should().NotContain(item => item.Id == mixedGovernorates.Id);
    }

    [Fact]
    public async Task Trips_CountryGrantDoesNotAuthorizeKnownRegionOutsideCountryHierarchy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCountryGrantAsync();
        var foreignRegionId = Guid.NewGuid();
        var foreignRegionTrip = await fixture.AddTripAsync("TRIP-FOREIGN-REGION", TripStatus.Planned, foreignRegionId);

        var result = await fixture.CreateTripsHandler().Handle(new GetAdminOperationsTripsQuery(
            "KW", Fixture.ServiceDate, null, null, null, null, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotContain(item => item.Id == foreignRegionTrip.Id);
    }

    [Fact]
    public async Task Trips_CountryGrantDoesNotOverrideConflictingBlockAndDeliveryRegion()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCountryGrantAsync();
        var trip = await fixture.AddTripAsync("TRIP-CONFLICTING-REGIONS", TripStatus.Planned, fixture.AreaA.Id);
        var stop = await fixture.Db.TripStops.SingleAsync(candidate => candidate.TripId == trip.Id);
        var day = await fixture.Db.SubscriptionDays.SingleAsync(candidate => candidate.Id == stop.SubscriptionDayId);
        day.DeliveryBlockRegionId = fixture.AreaC.Id;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateTripsHandler().Handle(new GetAdminOperationsTripsQuery(
            "KW", Fixture.ServiceDate, null, null, null, null, 1, 25), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotContain(item => item.Id == trip.Id);
    }

    [Fact]
    public async Task DriverLocations_AreBoundToTripAndCurrentAssignmentAndMissingGpsStaysNull()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstTrip = await fixture.AddTripAsync("TRIP-GPS-A", TripStatus.InProgress, fixture.AreaA.Id);
        var secondTrip = await fixture.AddTripAsync("TRIP-GPS-B", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.AddRange(
            new DriverAssignment { TripId = firstTrip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" },
            new DriverAssignment { TripId = secondTrip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now.AddMinutes(1), Status = "Active" });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = firstTrip.Id, DriverProfileId = driverId, Latitude = 29.3m, Longitude = 47.9m,
            RecordedAtUtc = Fixture.Now.AddMinutes(-2), Heading = null, SpeedKmh = null,
        });
        await fixture.Db.SaveChangesAsync();
        var handler = fixture.CreateLocationsHandler();

        var result = await handler.Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(item => item.TripId == firstTrip.Id && item.DriverId == driverId
            && item.Point!.Latitude == 29.3m && item.RecordedAtUtc == Fixture.Now.AddMinutes(-2));
        result.Value.Items.Should().ContainSingle(item => item.TripId == secondTrip.Id && item.Point == null && item.RecordedAtUtc == null);
        result.Value.Items.Should().OnlyContain(item => item.Heading == null && item.SpeedKmh == null);
    }

    [Fact]
    public async Task TripDetail_HidesOutOfScopeTripAsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        var outsideTrip = await fixture.AddTripAsync("TRIP-OUTSIDE", TripStatus.Planned, fixture.AreaC.Id);
        var handler = fixture.CreateDetailHandler();

        var result = await handler.Handle(new GetAdminOperationsTripDetailQuery("KW", outsideTrip.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("not_found");
    }

    [Fact]
    public async Task TripDetail_HeadTailTrackPreservesTheLatestActualPointWhenTruncated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-LONG-GPS", TripStatus.InProgress, fixture.AreaA.Id);
        var driverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.Add(new DriverAssignment { TripId = trip.Id, DriverProfileId = driverId, AssignedAtUtc = Fixture.Now, Status = "Active" });
        for (var index = 0; index < 2_001; index++)
            fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
            {
                TripId = trip.Id, DriverProfileId = driverId, Latitude = 29m + index / 100_000m,
                Longitude = 47m, RecordedAtUtc = Fixture.Now.AddSeconds(index),
            });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateDetailHandler().Handle(new GetAdminOperationsTripDetailQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActualTrack.Should().HaveCount(2_000);
        result.Value.ActualTrackWasTruncated.Should().BeTrue();
        result.Value.ActualTrackSourceCount.Should().Be(2_001);
        result.Value.ActualTrackSamplingStrategy.Should().Be("head-tail");
        result.Value.ActualTrack.MaxBy(point => point.RecordedAtUtc)!.RecordedAtUtc.Should().Be(Fixture.Now.AddSeconds(2_000));
    }

    [Fact]
    public async Task TripDetail_IncludesOnlyAssignedDriverHistoryAcrossReassignmentWithoutAnActiveAssignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-REASSIGNED", TripStatus.Completed, fixture.AreaA.Id);
        var priorDriverId = Guid.NewGuid();
        var finalDriverId = Guid.NewGuid();
        var neverAssignedDriverId = Guid.NewGuid();
        fixture.Db.DriverAssignments.AddRange(
            new DriverAssignment { TripId = trip.Id, DriverProfileId = priorDriverId, AssignedAtUtc = Fixture.Now.AddHours(-2), Status = "Completed" },
            new DriverAssignment { TripId = trip.Id, DriverProfileId = finalDriverId, AssignedAtUtc = Fixture.Now.AddHours(-1), Status = "Completed" });
        fixture.Db.DeliveryTrackingPoints.AddRange(
            new DeliveryTrackingPoint { TripId = trip.Id, DriverProfileId = priorDriverId, Latitude = 29.1m, Longitude = 47.1m, RecordedAtUtc = Fixture.Now.AddMinutes(-30) },
            new DeliveryTrackingPoint { TripId = trip.Id, DriverProfileId = finalDriverId, Latitude = 29.2m, Longitude = 47.2m, RecordedAtUtc = Fixture.Now.AddMinutes(-10) },
            new DeliveryTrackingPoint { TripId = trip.Id, DriverProfileId = neverAssignedDriverId, Latitude = 29.3m, Longitude = 47.3m, RecordedAtUtc = Fixture.Now.AddMinutes(-5) });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateDetailHandler().Handle(new GetAdminOperationsTripDetailQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DriverId.Should().BeNull();
        result.Value.ActualTrack.Select(point => point.DriverId).Should().BeEquivalentTo(new[] { priorDriverId, finalDriverId });
        result.Value.ActualTrack.Select(point => point.SegmentIndex).Should().OnlyContain(index => index == 0);
    }

    [Fact]
    public async Task PlannedRoute_UsesProviderGeometryPerOrderedLegAndNeverReturnsFallbackPolyline()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-ROUTE", TripStatus.Planned, fixture.AreaA.Id, fixture.AreaA.Id);
        var restaurant = await fixture.Db.RestaurantProfiles.SingleAsync(profile => profile.Id == trip.RestaurantId);
        restaurant.Latitude = 29.3m; restaurant.Longitude = 47.9m;
        var stops = await fixture.Db.TripStops.Where(stop => stop.TripId == trip.Id).OrderBy(stop => stop.SequenceNumber).ToListAsync();
        stops[0].Latitude = 29.31m; stops[0].Longitude = 47.91m;
        stops[1].Latitude = 29.32m; stops[1].Longitude = 47.92m;
        await fixture.Db.SaveChangesAsync();
        var routing = Substitute.For<IGoogleRoutingService>();
        var calls = 0;
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                var fallback = calls == 2;
                return Task.FromResult(Result.Success(new RouteDirectionsResult(1000, 1, 120, 2,
                    fallback ? "must-not-render" : $"provider-leg-{calls}", fallback, 29.3m, 47.9m, 29.31m, 47.91m)));
            });
        var handler = fixture.CreateRouteHandler(routing);

        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Provider.Should().Be("google-directions");
        result.Value.Legs.Should().HaveCount(2);
        result.Value.Legs[0].EncodedPolyline.Should().Be("provider-leg-1");
        result.Value.Legs[1].EncodedPolyline.Should().BeNull();
        result.Value.Legs[1].IsFallbackEstimate.Should().BeTrue();
        result.Value.Legs[1].ErrorCode.Should().Be("fallback_estimate_no_road_geometry");
        result.Value.HasFallbackEstimates.Should().BeTrue();
        result.Value.IsPartial.Should().BeTrue();
        result.Value.TotalDistanceMeters.Should().BeNull();

        var cached = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id), CancellationToken.None);
        cached.Value.Should().BeEquivalentTo(result.Value);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task PlannedRoute_MissingIntermediateStopCoordinatesDoNotConnectAcrossIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-ROUTE-MISSING", TripStatus.Planned, fixture.AreaA.Id, fixture.AreaA.Id);
        var restaurant = await fixture.Db.RestaurantProfiles.SingleAsync(profile => profile.Id == trip.RestaurantId);
        restaurant.Latitude = 29.3m; restaurant.Longitude = 47.9m;
        var stops = await fixture.Db.TripStops.Where(stop => stop.TripId == trip.Id).OrderBy(stop => stop.SequenceNumber).ToListAsync();
        stops[0].Latitude = null; stops[0].Longitude = null;
        stops[1].Latitude = 29.32m; stops[1].Longitude = 47.92m;
        await fixture.Db.SaveChangesAsync();
        var routing = Substitute.For<IGoogleRoutingService>();
        var handler = fixture.CreateRouteHandler(routing);

        var result = await handler.Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MissingStopIds.Should().Contain(stops[0].Id);
        result.Value.Legs.Should().HaveCount(2);
        result.Value.Legs.Should().OnlyContain(leg => leg.EncodedPolyline == null && leg.ErrorCode == "stop_coordinates_missing");
        await routing.DidNotReceiveWithAnyArgs().GetDirectionsAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task PlannedRoute_CompleteProviderLegSetReportsProviderTotalsAndExpectedLegCount()
    {
        await using var fixture = await Fixture.CreateAsync();
        var trip = await fixture.AddTripAsync("TRIP-ROUTE-COMPLETE", TripStatus.Planned, fixture.AreaA.Id, fixture.AreaA.Id);
        var restaurant = await fixture.Db.RestaurantProfiles.SingleAsync(profile => profile.Id == trip.RestaurantId);
        restaurant.Latitude = 29.3m; restaurant.Longitude = 47.9m;
        var stops = await fixture.Db.TripStops.Where(stop => stop.TripId == trip.Id).OrderBy(stop => stop.SequenceNumber).ToListAsync();
        stops[0].Latitude = 29.31m; stops[0].Longitude = 47.91m;
        stops[1].Latitude = 29.32m; stops[1].Longitude = 47.92m;
        await fixture.Db.SaveChangesAsync();
        var routing = Substitute.For<IGoogleRoutingService>();
        routing.GetDirectionsAsync(Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(new RouteDirectionsResult(1000, 1, 120, 2, "provider-polyline", false, 0, 0, 0, 0))));

        var result = await fixture.CreateRouteHandler(routing).Handle(new GetAdminTripPlannedRouteQuery("KW", trip.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Legs.Should().HaveCount(stops.Count);
        result.Value.Legs.Should().OnlyContain(leg => leg.EncodedPolyline == "provider-polyline" && !leg.IsFallbackEstimate);
        result.Value.IsPartial.Should().BeFalse();
        result.Value.TotalDistanceMeters.Should().Be(2000);
        result.Value.TotalDurationSeconds.Should().Be(240);
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

        public async Task AddCountryGrantAsync()
        {
            var scope = new AccessScope { Name = "Countrywide", ScopeType = AccessScopeType.Country, CountryId = Country.Id, TenantId = Tenant.Id, IsActive = true };
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

        public GetAdminOperationsTripsQueryHandler CreateTripsHandler() => new(Db, Access, clock);
        public GetAdminOperationsTripDetailQueryHandler CreateDetailHandler() => new(Db, Access);
        public GetAdminDriverMapLocationsQueryHandler CreateLocationsHandler() => new(Db, Access, clock);
        public GetAdminTripPlannedRouteQueryHandler CreateRouteHandler(IGoogleRoutingService routing)
            => new(Db, Access, routing, new MemoryCache(new MemoryCacheOptions()), clock);
        private AdminCoverageAccess Access => new(Db, user, new KuwaitTenantResolver(Db, user));
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        private static Region NewRegion(Guid countryId, RegionType type, Guid? parentId, string name) => new() { CountryId = countryId, Type = type, ParentRegionId = parentId, Code = $"{name}-{Guid.NewGuid():N}", NameAr = name, NameEn = name, IsActive = true };
    }
}
