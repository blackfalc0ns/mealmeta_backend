using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record SendDriverDocumentExpiryAlertCommand(Guid DriverProfileId) : ICommand;

public sealed class SendDriverDocumentExpiryAlertCommandHandler : ICommandHandler<SendDriverDocumentExpiryAlertCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SendDriverDocumentExpiryAlertCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(SendDriverDocumentExpiryAlertCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated ||
            (!_currentUser.Roles.Any(AppRoles.IsAdminRole) && !_currentUser.HasPermission(PermissionCodes.DriverRegView)))
        {
            return Error.Forbidden("Driver.AlertForbidden", "Only administrators can send document expiry alerts.");
        }

        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.DriverProfileId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound("Driver.NotFound", "Driver profile not found.");
        }

        var now = _clock.UtcNow;
        var notification = new DriverNotification
        {
            UserId = driver.UserId,
            DriverProfileId = driver.Id,
            Type = "document-expiry-warning",
            TitleAr = "تنبيه تجديد المستندات المنتهية",
            TitleEn = "Document Renewal Alert",
            BodyAr = "يرجى تجديد ورفع مستنداتك المعتمدة (رخصة القيادة أو البطاقة المدنية أو دفتر المركبة) للحفاظ على جاهزيتك التشغيلية.",
            BodyEn = "Please renew and upload your required documents (driving license, civil ID, or vehicle registration) to maintain operational eligibility.",
            Section = "documents",
            DeepLink = "/driver/profile/documents",
            IsRead = false,
            SentAtUtc = now
        };

        _db.DriverNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
