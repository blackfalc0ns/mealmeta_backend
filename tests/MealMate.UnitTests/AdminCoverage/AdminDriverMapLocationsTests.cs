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
using NSubstitute;

namespace MealMate.UnitTests.AdminCoverage;

public sealed class AdminDriverMapLocationsTests
{
    [Fact]
    public async Task ActiveSession_WithNoTrips_AppearsInRosterWithZeroTrips()
    {
        await using var fixture = await Fixture.CreateAsync();
        var onDutyDriver = await fixture.AddDriverAsync("OnDuty Driver", isAvailable: false, hasActiveSession: true);
        var availableOffDuty = await fixture.AddDriverAsync("OffDuty Available Driver", isAvailable: true, hasActiveSession: false);
        var handler = fixture.CreateLocationsHandler();

        var result = await handler.Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rosterEntry = result.Value.Drivers.Should().ContainSingle(driver => driver.DriverId == onDutyDriver.Id && driver.Trips.Count == 0).Subject;
        rosterEntry.IsAvailable.Should().BeFalse();
        rosterEntry.CurrentPoint.Should().BeNull();
        rosterEntry.CurrentTripId.Should().BeNull();
        rosterEntry.CurrentTripStatus.Should().BeNull();
        rosterEntry.NextZoneNameAr.Should().BeNull();
        rosterEntry.NextZoneNameEn.Should().BeNull();
        result.Value.Drivers.Should().NotContain(driver => driver.DriverId == availableOffDuty.Id);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ActiveAssignment_ResolvesCurrentTrip_NextZone_AndTrackingPoint_IndependentlyFromServiceDate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var driver = await fixture.AddDriverAsync("Active Courier", isAvailable: true, hasActiveSession: true);
        var tomorrowTrip = await fixture.AddTripWithCustomStopsAsync("TRIP-TOMORROW", TripStatus.InProgress, fixture.AreaA.Id,
            (1, TripStopStatus.Delivered),
            (2, TripStopStatus.PickedUp),
            (1, TripStopStatus.Pending));

        // Modify stops on tomorrowTrip to have distinct zone names
        var stops = await fixture.Db.TripStops.Where(s => s.TripId == tomorrowTrip.Id).OrderBy(s => s.SequenceNumber).ToListAsync();
        stops[0].ZoneNameAr = "حولي"; stops[0].ZoneNameEn = "Hawalli";
        stops[1].ZoneNameAr = "السالمية"; stops[1].ZoneNameEn = "Salmiya";
        stops[2].ZoneNameAr = "الرميثية"; stops[2].ZoneNameEn = "Rumaithiya";

        // Set planned date to tomorrow so it is distinct from query ServiceDate
        tomorrowTrip.PlannedDate = Fixture.ServiceDate.AddDays(1);

        fixture.Db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = tomorrowTrip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = Fixture.Now,
            Status = "Active"
        });

        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = tomorrowTrip.Id,
            DriverProfileId = driver.Id,
            Latitude = 29.3375m,
            Longitude = 48.0763m,
            RecordedAtUtc = Fixture.Now.AddSeconds(-25)
        });

        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rosterDriver = result.Value.Drivers.Should().ContainSingle(d => d.DriverId == driver.Id).Subject;

        // Selected-day trips are empty because tomorrowTrip is on tomorrow
        rosterDriver.Trips.Should().BeEmpty();
        result.Value.Items.Should().BeEmpty();

        // Ongoing active assignment resolves current trip, next zone, and current GPS
        rosterDriver.IsAvailable.Should().BeTrue();
        rosterDriver.CurrentTripId.Should().Be(tomorrowTrip.Id);
        rosterDriver.CurrentTripStatus.Should().Be("InProgress");
        rosterDriver.NextZoneNameAr.Should().Be("السالمية");
        rosterDriver.NextZoneNameEn.Should().Be("Salmiya");
        rosterDriver.CurrentPoint.Should().NotBeNull();
        rosterDriver.CurrentPoint!.Latitude.Should().Be(29.3375m);
        rosterDriver.CurrentPoint.Longitude.Should().Be(48.0763m);
        rosterDriver.LocationRecordedAtUtc.Should().Be(Fixture.Now.AddSeconds(-25));
    }

    [Fact]
    public async Task InactiveSession_DoesNotAppearInRoster()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inactiveSessionDriver = await fixture.AddDriverAsync("Ended Session Driver", isAvailable: true, hasActiveSession: false);
        fixture.Db.DriverWorkSessions.Add(new DriverWorkSession
        {
            DriverProfileId = inactiveSessionDriver.Id,
            Status = "Ended",
            StartedAtUtc = Fixture.Now.AddHours(-5),
            EndedAtUtc = Fixture.Now.AddHours(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Drivers.Should().NotContain(driver => driver.DriverId == inactiveSessionDriver.Id);
    }

    [Fact]
    public async Task TwoTrips_ForOneDriver_ProduceOneRosterEntryAndSeparateTrips()
    {
        await using var fixture = await Fixture.CreateAsync();
        var driver = await fixture.AddDriverAsync("MultiTrip Driver", isAvailable: false, hasActiveSession: true);
        var trip1 = await fixture.AddTripAsync("TRIP-1", TripStatus.InProgress, fixture.AreaA.Id);
        var trip2 = await fixture.AddTripAsync("TRIP-2", TripStatus.Planned, fixture.AreaA.Id);

        fixture.Db.DriverAssignments.AddRange(
            new DriverAssignment { TripId = trip1.Id, DriverProfileId = driver.Id, AssignedAtUtc = Fixture.Now, Status = "Active" },
            new DriverAssignment { TripId = trip2.Id, DriverProfileId = driver.Id, AssignedAtUtc = Fixture.Now.AddMinutes(5), Status = "Active" });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var rosterEntry = result.Value.Drivers.Should().ContainSingle(d => d.DriverId == driver.Id).Subject;
        rosterEntry.Trips.Should().HaveCount(2);
        rosterEntry.Trips.Select(t => t.TripId).Should().BeEquivalentTo(new[] { trip1.Id, trip2.Id });
    }

    [Fact]
    public async Task Items_PreservesExistingTripLevelShape_WhileDriversAddsRoster()
    {
        await using var fixture = await Fixture.CreateAsync();
        var driver = await fixture.AddDriverAsync("Roster Driver", isAvailable: true, hasActiveSession: true);
        var trip = await fixture.AddTripAsync("TRIP-ITEMS-CHECK", TripStatus.InProgress, fixture.AreaA.Id);
        fixture.Db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = Fixture.Now,
            Status = "Active"
        });
        fixture.Db.DeliveryTrackingPoints.Add(new DeliveryTrackingPoint
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            Latitude = 29.35m,
            Longitude = 47.95m,
            RecordedAtUtc = Fixture.Now.AddSeconds(-30)
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle(item => item.TripId == trip.Id && item.DriverId == driver.Id
            && item.Point!.Latitude == 29.35m && item.Point.Longitude == 47.95m);
        result.Value.Drivers.Should().ContainSingle(d => d.DriverId == driver.Id
            && d.CurrentPoint!.Latitude == 29.35m && d.CurrentPoint.Longitude == 47.95m);
    }

    [Fact]
    public async Task Driver_WithNoTrackingPoint_RemainsPresentWithNullGps()
    {
        await using var fixture = await Fixture.CreateAsync();
        var driver = await fixture.AddDriverAsync("No GPS Driver", isAvailable: false, hasActiveSession: true);
        var trip = await fixture.AddTripAsync("TRIP-NO-GPS", TripStatus.InProgress, fixture.AreaA.Id);
        fixture.Db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = Fixture.Now,
            Status = "Active"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var driverRoster = result.Value.Drivers.Should().ContainSingle(d => d.DriverId == driver.Id).Subject;
        driverRoster.CurrentPoint.Should().BeNull();
        driverRoster.LocationRecordedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task BoxTotals_AggregatesStatusesAndCountsInvalidBoxesCorrectly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var driver = await fixture.AddDriverAsync("Totals Driver", isAvailable: false, hasActiveSession: true);
        var trip = await fixture.AddTripWithCustomStopsAsync("TRIP-TOTALS", TripStatus.InProgress, fixture.AreaA.Id,
            (2, TripStopStatus.Delivered),
            (3, TripStopStatus.Pending),
            (1, TripStopStatus.Failed),
            (4, TripStopStatus.ReturnedToKitchen),
            (0, TripStopStatus.Pending)); // 5th stop: BoxCount = 0 (invalid)

        fixture.Db.DriverAssignments.Add(new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = driver.Id,
            AssignedAtUtc = Fixture.Now,
            Status = "Active"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var tripSummary = result.Value.Drivers.Should().ContainSingle(d => d.DriverId == driver.Id).Subject
            .Trips.Should().ContainSingle(t => t.TripId == trip.Id).Subject;

        tripSummary.Totals.StopCount.Should().Be(5);
        tripSummary.Totals.DeliveredStopCount.Should().Be(1);
        tripSummary.Totals.RemainingStopCount.Should().Be(2); // 3 Pending + 0 Pending
        tripSummary.Totals.FailedStopCount.Should().Be(1);
        tripSummary.Totals.ReturnedStopCount.Should().Be(1);

        tripSummary.Totals.DeliveredBoxCount.Should().Be(2);
        tripSummary.Totals.RemainingBoxCount.Should().Be(3);
        tripSummary.Totals.FailedBoxCount.Should().Be(1);
        tripSummary.Totals.ReturnedBoxCount.Should().Be(4);
        tripSummary.Totals.InvalidBoxCount.Should().Be(1);
    }

    [Fact]
    public async Task AuthorizedScope_ExcludesUnauthorizedRestaurantOrRegionRecords()
    {
        await using var fixture = await Fixture.CreateAsync();
        // AreaC is not in user's authorized scope (user only has AreaA)
        var unauthorizedDriver = await fixture.AddDriverAsync("Unauthorized Driver", isAvailable: false, hasActiveSession: true, regionId: fixture.AreaC.Id);

        var result = await fixture.CreateLocationsHandler().Handle(new GetAdminDriverMapLocationsQuery(
            "KW", Fixture.ServiceDate, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Drivers.Should().NotContain(d => d.DriverId == unauthorizedDriver.Id);
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

        public async Task<DriverProfile> AddDriverAsync(string name, bool isAvailable, bool hasActiveSession, Guid? regionId = null)
        {
            var driverUser = new ApplicationUser { UserName = $"driver-{Guid.NewGuid():N}", AccountStatus = AccountStatus.Active, UserType = UserType.Driver };
            var profile = new DriverProfile
            {
                UserId = driverUser.Id,
                CountryId = Country.Id,
                RegionId = regionId ?? AreaA.Id,
                FullNameAr = name,
                FullNameEn = name,
                IsAvailable = isAvailable,
                VerificationStatus = VerificationStatus.Approved,
                ProfileImageStorageKey = "uploads/driver.jpg",
                ProfileImageStatus = VerificationStatus.Approved
            };
            Db.Users.Add(driverUser);
            Db.DriverProfiles.Add(profile);
            if (hasActiveSession)
            {
                Db.DriverWorkSessions.Add(new DriverWorkSession
                {
                    DriverProfileId = profile.Id,
                    Status = "Active",
                    StartedAtUtc = Now.AddHours(-2)
                });
            }
            await Db.SaveChangesAsync();
            return profile;
        }

        public async Task<Trip> AddTripAsync(string code, TripStatus status, params Guid[] stopRegionIds)
        {
            var restaurantUser = new ApplicationUser { UserName = $"restaurant-{code}", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
            var restaurant = new RestaurantProfile { UserId = restaurantUser.Id, CountryId = Country.Id, PrimaryRegionId = stopRegionIds.FirstOrDefault(), TradeNameAr = code, TradeNameEn = code, LegalNameAr = code, LegalNameEn = code, IsOperationallyActive = true };
            var trip = new Trip { RestaurantId = restaurant.Id, PlannedDate = ServiceDate, Code = code, Status = status };
            Db.Users.Add(restaurantUser); Db.RestaurantProfiles.Add(restaurant); Db.Trips.Add(trip);
            var sequence = 1;
            foreach (var rId in stopRegionIds)
            {
                var day = new SubscriptionDay { SubscriptionId = Guid.NewGuid(), ServiceDate = ServiceDate, RestaurantId = restaurant.Id, DeliveryRegionId = rId };
                Db.SubscriptionDays.Add(day);
                Db.TripStops.Add(new TripStop { TripId = trip.Id, SubscriptionDayId = day.Id, OrderCode = $"ORDER-{code}-{sequence}", CustomerMaskedId = "masked", ZoneNameAr = "منطقة", ZoneNameEn = "Area", MealSummaryAr = "وجبة", MealSummaryEn = "Meal", SlotLabel = "AM", SequenceNumber = sequence++, Status = TripStopStatus.Pending, BoxCount = 1 });
            }
            await Db.SaveChangesAsync();
            return trip;
        }

        public async Task<Trip> AddTripWithCustomStopsAsync(string code, TripStatus status, Guid regionId, params (int BoxCount, TripStopStatus Status)[] stopSpecs)
        {
            var restaurantUser = new ApplicationUser { UserName = $"restaurant-{code}", AccountStatus = AccountStatus.Active, UserType = UserType.Restaurant };
            var restaurant = new RestaurantProfile { UserId = restaurantUser.Id, CountryId = Country.Id, PrimaryRegionId = regionId, TradeNameAr = code, TradeNameEn = code, LegalNameAr = code, LegalNameEn = code, IsOperationallyActive = true };
            var trip = new Trip { RestaurantId = restaurant.Id, PlannedDate = ServiceDate, Code = code, Status = status };
            Db.Users.Add(restaurantUser); Db.RestaurantProfiles.Add(restaurant); Db.Trips.Add(trip);
            var sequence = 1;
            foreach (var (boxCount, stopStatus) in stopSpecs)
            {
                var day = new SubscriptionDay { SubscriptionId = Guid.NewGuid(), ServiceDate = ServiceDate, RestaurantId = restaurant.Id, DeliveryRegionId = regionId };
                Db.SubscriptionDays.Add(day);
                Db.TripStops.Add(new TripStop
                {
                    TripId = trip.Id,
                    SubscriptionDayId = day.Id,
                    OrderCode = $"ORDER-{code}-{sequence}",
                    CustomerMaskedId = $"C***{sequence}",
                    ZoneNameAr = "منطقة",
                    ZoneNameEn = "Area",
                    MealSummaryAr = "وجبة",
                    MealSummaryEn = "Meal",
                    SlotLabel = "AM",
                    SequenceNumber = sequence++,
                    Status = stopStatus,
                    BoxCount = boxCount
                });
            }
            await Db.SaveChangesAsync();
            return trip;
        }

        public GetAdminDriverMapLocationsQueryHandler CreateLocationsHandler() => new(Db, Access, clock);
        private AdminCoverageAccess Access => new(Db, user, new KuwaitTenantResolver(Db, user));
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        private static Region NewRegion(Guid countryId, RegionType type, Guid? parentId, string name) => new() { CountryId = countryId, Type = type, ParentRegionId = parentId, Code = $"{name}-{Guid.NewGuid():N}", NameAr = name, NameEn = name, IsActive = true };
    }
}
