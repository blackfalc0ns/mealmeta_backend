using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.RestaurantPush;

public sealed record SendDispatcherTestPushCommand(
    Guid? RestaurantId = null,
    Guid? DeliveryManagerId = null,
    string? CustomMessage = null) : ICommand<TestPushDeliveryResultDto>;

public sealed record SendDriverTestPushCommand(
    Guid? RestaurantId = null,
    Guid? DriverId = null,
    string? CustomMessage = null) : ICommand<TestPushDeliveryResultDto>;

public sealed record SendFleetTestPushApiRequest(
    Guid? RestaurantId = null,
    Guid? TargetId = null,
    string? Message = null);

public sealed class SendTestNotificationToFleetCommandHandlers
    : ICommandHandler<SendDispatcherTestPushCommand, TestPushDeliveryResultDto>,
      ICommandHandler<SendDriverTestPushCommand, TestPushDeliveryResultDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantPushTestService _pushTestService;

    public SendTestNotificationToFleetCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IRestaurantPushTestService pushTestService)
    {
        _db = db;
        _currentUser = currentUser;
        _pushTestService = pushTestService;
    }

    public async Task<Result<TestPushDeliveryResultDto>> Handle(
        SendDispatcherTestPushCommand request,
        CancellationToken cancellationToken)
    {
        var restaurant = await ResolveRestaurantAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على المطعم المحدد.");
        }

        var result = await _pushTestService.SendTestToDispatcherAsync(
            restaurant.Id,
            request.DeliveryManagerId,
            request.CustomMessage,
            cancellationToken);

        return Result.Success(result);
    }

    public async Task<Result<TestPushDeliveryResultDto>> Handle(
        SendDriverTestPushCommand request,
        CancellationToken cancellationToken)
    {
        var restaurant = await ResolveRestaurantAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على المطعم المحدد.");
        }

        var result = await _pushTestService.SendTestToDriverAsync(
            restaurant.Id,
            request.DriverId,
            request.CustomMessage,
            cancellationToken);

        return Result.Success(result);
    }

    private async Task<RestaurantProfile?> ResolveRestaurantAsync(Guid? restaurantId, CancellationToken cancellationToken)
    {
        if (restaurantId.HasValue && restaurantId.Value != Guid.Empty)
        {
            return await _db.RestaurantProfiles.FirstOrDefaultAsync(r => r.Id == restaurantId.Value, cancellationToken);
        }

        if (_currentUser.UserId.HasValue)
        {
            var byUser = await _db.RestaurantProfiles.FirstOrDefaultAsync(r => r.UserId == _currentUser.UserId.Value, cancellationToken);
            if (byUser is not null) return byUser;

            var byScope = await _db.UserScopes
                .Where(s => s.UserId == _currentUser.UserId.Value && s.AccessScope.IsActive)
                .Select(s => s.AccessScope.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken);

            if (byScope.HasValue && byScope.Value != Guid.Empty)
            {
                return await _db.RestaurantProfiles.FirstOrDefaultAsync(r => r.Id == byScope.Value, cancellationToken);
            }
        }

        return await _db.RestaurantProfiles.OrderBy(r => r.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }
}
