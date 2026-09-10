using MealMate.Application.Common.Abstractions;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

/// <summary>
/// When a family member is linked to an existing customer who already has a saved address,
/// prefer that customer's address over a manager/shadow-owned address that the client may have sent.
/// </summary>
internal static class FamilyMemberAddressHydration
{
    public static async Task PreferLinkedCustomerAddressAsync(
        IAppDbContext db,
        FamilyMember member,
        Guid linkedCustomerUserId,
        CancellationToken cancellationToken)
    {
        var linkedDefaultId = await db.CustomerAddresses.AsNoTracking()
            .Where(a => a.UserId == linkedCustomerUserId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAtUtc)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkedDefaultId is null)
            return;

        if (member.DeliveryAddressId is Guid assignedId)
        {
            var assignedOwnerId = await db.CustomerAddresses.AsNoTracking()
                .Where(a => a.Id == assignedId)
                .Select(a => (Guid?)a.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            // Keep an address that already belongs to the linked customer.
            if (assignedOwnerId == linkedCustomerUserId)
                return;
        }

        member.DeliveryAddressId = linkedDefaultId;
        member.SetupStatus = FamilyMemberPlanRules.ResolveSetupStatus(member);
    }
}
