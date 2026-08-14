using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder().SetBasePath(Path.Combine(Environment.CurrentDirectory, "src", "MealMate.Api")).AddJsonFile("appsettings.Development.json").Build();
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(config.GetConnectionString("MealMateDb")).EnableDetailedErrors().Options;
await using var db = new AppDbContext(options, new Dispatcher());
var controller = new CustomerSubscriptionManagementController(db, new User(), new Clock()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
try { var result = await controller.Management(true, CancellationToken.None); Console.WriteLine(result.GetType().Name); }
catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }

sealed class User : ICurrentUser { public bool IsAuthenticated => true; public Guid? UserId => Guid.Parse("49d159ac-5025-4acd-a990-ab52f0f83837"); public UserType? UserType => MealMate.Domain.Enums.UserType.Customer; public Guid? RestaurantId => null; public IReadOnlyCollection<string> Roles => []; public IReadOnlyCollection<string> Permissions => []; public IReadOnlyCollection<Guid> AccessScopeIds => []; public IReadOnlyCollection<Guid> TenantIds => []; public bool IsInRole(string role) => false; public bool HasPermission(string permission) => false; }
sealed class Clock : IDateTimeProvider { public DateTime UtcNow => DateTime.UtcNow; public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow); }
sealed class Dispatcher : IDomainEventDispatcher { public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask; }
