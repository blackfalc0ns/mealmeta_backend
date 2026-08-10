using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Localization;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantReviewItemDto(
    Guid Id,
    string Section,
    string SubjectType,
    Guid? SubjectId,
    string? FieldKey,
    VerificationStatus Status,
    string? Reason,
    DateTime? ReviewedAtUtc,
    DateTime? SubmittedAtUtc);

public sealed record RestaurantReviewNotificationDto(
    Guid Id,
    string Type,
    string TitleAr,
    string TitleEn,
    string BodyAr,
    string BodyEn,
    string? Section,
    Guid? SubjectId,
    bool IsRead,
    DateTime SentAtUtc);

public sealed record RestaurantReviewCenterDto(
    Guid RestaurantId,
    Guid RegistrationId,
    VerificationStatus RegistrationStatus,
    IReadOnlyList<RestaurantReviewItemDto> Items,
    IReadOnlyList<RestaurantReviewNotificationDto> Notifications);

public sealed record GetRestaurantReviewCenterQuery : IQuery<RestaurantReviewCenterDto>;
public sealed record GetRestaurantReviewNotificationsQuery : IQuery<IReadOnlyList<RestaurantReviewNotificationDto>>;
public sealed record MarkRestaurantReviewNotificationReadCommand(Guid Id) : ICommand<bool>;
public sealed record SaveRestaurantReviewSectionDraftCommand(string Section) : ICommand<bool>;
public sealed record SubmitRestaurantReviewSectionCommand(string Section) : ICommand<bool>;

public sealed class RestaurantReviewCenterHandlers :
    IQueryHandler<GetRestaurantReviewCenterQuery, RestaurantReviewCenterDto>,
    IQueryHandler<GetRestaurantReviewNotificationsQuery, IReadOnlyList<RestaurantReviewNotificationDto>>,
    ICommandHandler<MarkRestaurantReviewNotificationReadCommand, bool>,
    ICommandHandler<SaveRestaurantReviewSectionDraftCommand, bool>,
    ICommandHandler<SubmitRestaurantReviewSectionCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RestaurantReviewCenterHandlers(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantReviewCenterDto>> Handle(GetRestaurantReviewCenterQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!_currentUser.IsAuthenticated || userId is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");

        var registration = await _db.RestaurantRegistrations.AsNoTracking()
            .Where(x => x.SubmittedByUserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (registration is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "No restaurant registration found.");

        var restaurantId = registration.ResultingRestaurantId ?? registration.Id;
        var items = await BuildItems(registration, restaurantId, cancellationToken);
        var notifications = await LoadNotifications(userId.Value, cancellationToken);
        return new RestaurantReviewCenterDto(restaurantId, registration.Id, registration.Status, items, notifications);
    }

    public async Task<Result<IReadOnlyList<RestaurantReviewNotificationDto>>> Handle(GetRestaurantReviewNotificationsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");
        var notifications = await LoadNotifications(_currentUser.UserId.Value, cancellationToken);
        return Result.Success<IReadOnlyList<RestaurantReviewNotificationDto>>(notifications);
    }

    public async Task<Result<bool>> Handle(MarkRestaurantReviewNotificationReadCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");
        var notification = await _db.RestaurantNotifications.FirstOrDefaultAsync(
            x => x.Id == request.Id && x.UserId == _currentUser.UserId.Value, cancellationToken);
        if (notification is null) return Error.NotFound(MessageCodes.Common.NotFound, "Notification not found.");
        notification.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<Result<bool>> Handle(SaveRestaurantReviewSectionDraftCommand request, CancellationToken cancellationToken)
        => SetSectionStatus(request.Section, VerificationStatus.Draft, cancellationToken);

    public Task<Result<bool>> Handle(SubmitRestaurantReviewSectionCommand request, CancellationToken cancellationToken)
        => SetSectionStatus(request.Section, VerificationStatus.Submitted, cancellationToken);

    private async Task<Result<bool>> SetSectionStatus(string section, VerificationStatus target, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");
        var registration = await _db.RestaurantRegistrations.Where(x => x.SubmittedByUserId == _currentUser.UserId)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (registration is null) return Error.NotFound(MessageCodes.Auth.InvalidState, "No registration found.");
        var restaurantId = registration.ResultingRestaurantId ?? registration.Id;
        var items = await _db.RestaurantReviewItems.Where(x => x.RestaurantId == restaurantId && x.Section == section).ToListAsync(cancellationToken);
        foreach (var item in items.Where(x => x.Status != VerificationStatus.Approved))
        {
            item.Status = target;
            if (target == VerificationStatus.Submitted) item.SubmittedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<RestaurantReviewItemDto>> BuildItems(
        Domain.IdentityAccess.RestaurantRegistration registration,
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        var fields = await _db.RestaurantRegistrationFieldReviews.AsNoTracking()
            .Where(x => x.RegistrationId == registration.Id).ToListAsync(cancellationToken);
        var documents = await _db.Documents.AsNoTracking().Include(x => x.Verifications)
            .Where(x => x.OwnerType == DocumentOwnerType.Registration && x.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);
        var generic = await _db.RestaurantReviewItems.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);

        var result = fields.Select(x => new RestaurantReviewItemDto(
            x.Id, "restaurant-data", "field", null, x.FieldKey, x.Status, x.Reason, x.ReviewedAtUtc, null)).ToList();
        result.AddRange(documents.Select(x =>
        {
            var latest = x.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new RestaurantReviewItemDto(x.Id, "documents", "document", x.Id, null,
                latest?.Status ?? VerificationStatus.Submitted, latest?.Reason, latest?.ReviewedAtUtc, x.CreatedAtUtc);
        }));
        result.AddRange(generic.Select(x => new RestaurantReviewItemDto(
            x.Id, x.Section, x.SubjectType, x.SubjectId, x.FieldKey, x.Status, x.Reason, x.ReviewedAtUtc, x.SubmittedAtUtc)));
        return result;
    }

    private async Task<IReadOnlyList<RestaurantReviewNotificationDto>> LoadNotifications(Guid userId, CancellationToken cancellationToken)
        => await _db.RestaurantNotifications.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.SentAtUtc).Take(100)
            .Select(x => new RestaurantReviewNotificationDto(x.Id, x.Type, x.TitleAr, x.TitleEn, x.BodyAr, x.BodyEn, x.Section, x.SubjectId, x.IsRead, x.SentAtUtc))
            .ToListAsync(cancellationToken);
}
