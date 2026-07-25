using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Common.Security;

internal static class DocumentAccess
{
    public static bool CanAccessAnyDocument(ICurrentUser user) =>
        user.IsInRole(AppRoles.SuperAdmin)
        || user.HasPermission(PermissionCodes.DocumentsReview);

    public static async Task<bool> CallerOwnsDocumentTargetAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        DocumentOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return false;
        }

        return ownerType switch
        {
            DocumentOwnerType.Registration =>
                await db.RestaurantRegistrations.AnyAsync(
                    r => r.Id == ownerId && r.SubmittedByUserId == userId, cancellationToken)
                || await db.DriverRegistrations.AnyAsync(
                    r => r.Id == ownerId && r.SubmittedByUserId == userId, cancellationToken),
            DocumentOwnerType.Restaurant =>
                currentUser.RestaurantId == ownerId
                || await db.RestaurantProfiles.AnyAsync(
                    r => r.Id == ownerId && r.UserId == userId, cancellationToken),
            DocumentOwnerType.Driver =>
                await db.DriverProfiles.AnyAsync(
                    d => d.Id == ownerId && d.UserId == userId, cancellationToken),
            DocumentOwnerType.Customer =>
                await db.CustomerProfiles.AnyAsync(
                    c => c.Id == ownerId && c.UserId == userId, cancellationToken),
            DocumentOwnerType.Influencer =>
                await db.InfluencerProfiles.AnyAsync(
                    i => i.Id == ownerId && i.UserId == userId, cancellationToken),
            DocumentOwnerType.Admin => false,
            _ => false,
        };
    }
}
