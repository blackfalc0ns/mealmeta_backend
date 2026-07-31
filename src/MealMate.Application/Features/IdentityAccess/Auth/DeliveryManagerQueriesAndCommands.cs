using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.Email;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record DeliveryManagerDto(
    Guid Id,
    string FullName,
    string Email,
    string Phone,
    AccountStatus Status,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record ListDeliveryManagersQuery(Guid? RestaurantId = null) : IQuery<IReadOnlyList<DeliveryManagerDto>>;

public sealed class ListDeliveryManagersQueryHandler : IQueryHandler<ListDeliveryManagersQuery, IReadOnlyList<DeliveryManagerDto>>
{
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;

    public ListDeliveryManagersQueryHandler(IUserAccountService users, ICurrentUser currentUser)
    {
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<DeliveryManagerDto>>> Handle(ListDeliveryManagersQuery request, CancellationToken cancellationToken)
    {
        var targetRestaurantId = request.RestaurantId ?? _currentUser.RestaurantId;
        var users = await _users.ListUsersByTypeAsync(UserType.DeliveryManager, cancellationToken);

        if (targetRestaurantId.HasValue)
        {
            users = users.Where(u => u.RestaurantId == targetRestaurantId.Value || u.RestaurantId == null).ToList();
        }

        var result = users.Select(u => new DeliveryManagerDto(
            u.Id,
            u.FullName ?? "—",
            u.Email ?? "—",
            u.PhoneNumber ?? "—",
            u.AccountStatus,
            u.CreatedAtUtc,
            u.LastLoginAtUtc)).ToList();

        return result;
    }
}

public sealed record ToggleDeliveryManagerStatusCommand(Guid DeliveryManagerId) : ICommand;

public sealed class ToggleDeliveryManagerStatusCommandHandler : ICommandHandler<ToggleDeliveryManagerStatusCommand>
{
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public ToggleDeliveryManagerStatusCommandHandler(IUserAccountService users, IAuditLogger audit)
    {
        _users = users;
        _audit = audit;
    }

    public async Task<Result> Handle(ToggleDeliveryManagerStatusCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(request.DeliveryManagerId, cancellationToken);
        if (user is null || user.UserType != UserType.DeliveryManager)
        {
            return Error.NotFound("User.NotFound", "Delivery manager account not found.");
        }

        var nextStatus = user.AccountStatus == AccountStatus.Active
            ? AccountStatus.Suspended
            : AccountStatus.Active;

        await _users.UpdateAccountStatusAsync(user.Id, nextStatus, cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerStatusToggled",
            "User",
            user.Id,
            After: new { Status = nextStatus },
            SourceChannel: "restaurant"), cancellationToken);

        return Result.Success();
    }
}

public sealed record ResendDeliveryManagerInviteCommand(Guid DeliveryManagerId) : ICommand;

public sealed class ResendDeliveryManagerInviteCommandHandler : ICommandHandler<ResendDeliveryManagerInviteCommand>
{
    private readonly IUserAccountService _users;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IAuditLogger _audit;

    public ResendDeliveryManagerInviteCommandHandler(
        IUserAccountService users,
        ITransactionalEmailSender emailSender,
        IAuditLogger audit)
    {
        _users = users;
        _emailSender = emailSender;
        _audit = audit;
    }

    public async Task<Result> Handle(ResendDeliveryManagerInviteCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(request.DeliveryManagerId, cancellationToken);
        if (user is null || user.UserType != UserType.DeliveryManager)
        {
            return Error.NotFound("User.NotFound", "Delivery manager account not found.");
        }

        var newTempPassword = MealMate.Application.Common.Helpers.TemporaryPasswordGenerator.Generate();
        var resetToken = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        await _users.ResetPasswordAsync(user.Id, resetToken, newTempPassword, cancellationToken);

        var recipientEmail = user.Email ?? string.Empty;
        var rendered = EmailTemplateCatalog.Render(
            EmailTemplateCatalog.DeliveryManagerInvite,
            "ar",
            new Dictionary<string, string>
            {
                ["recipient_name"] = user.FullName ?? recipientEmail,
                ["email"] = recipientEmail,
                ["password"] = newTempPassword,
                ["support_email"] = "support@mealmate.app"
            });

        if (rendered.IsSuccess && !string.IsNullOrWhiteSpace(recipientEmail))
        {
            await _emailSender.SendAsync(new TransactionalEmailRequest(
                recipientEmail,
                rendered.Value.Subject,
                rendered.Value.Html,
                rendered.Value.Text,
                TemplateId: EmailTemplateCatalog.DeliveryManagerInvite), cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerInviteResent",
            "User",
            user.Id,
            After: new { user.Email },
            SourceChannel: "restaurant"), cancellationToken);

        return Result.Success();
    }
}
