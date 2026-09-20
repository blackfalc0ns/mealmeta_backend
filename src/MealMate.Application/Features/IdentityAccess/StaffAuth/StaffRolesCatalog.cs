using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public sealed record StaffRoleDto(
    string Code,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string DescriptionEn,
    string IconKey,
    bool AllowsSelfRegistration,
    int DisplayOrder);

public sealed record GetStaffRolesQuery : IQuery<IReadOnlyList<StaffRoleDto>>;

public sealed class GetStaffRolesQueryHandler : IQueryHandler<GetStaffRolesQuery, IReadOnlyList<StaffRoleDto>>
{
    private static readonly IReadOnlyList<StaffRoleDto> Roles = new List<StaffRoleDto>
    {
        new(
            Code: StaffRoles.Driver,
            NameAr: "سائق",
            NameEn: "Driver",
            DescriptionAr: "توصيل الطلبات، تحديث الحالات، ومتابعة المسارات",
            DescriptionEn: "Deliver orders, update statuses, and follow routes",
            IconKey: "driver",
            AllowsSelfRegistration: true,
            DisplayOrder: 1),
        new(
            Code: StaffRoles.DeliveryManager,
            NameAr: "مسؤول توصيل",
            NameEn: "Delivery Manager",
            DescriptionAr: "إدارة الرحلات، تعيين السائقين، ومتابعة الحركة",
            DescriptionEn: "Manage trips, assign drivers, and monitor dispatch",
            IconKey: "delivery_manager",
            AllowsSelfRegistration: false,
            DisplayOrder: 2)
    };

    public Task<Result<IReadOnlyList<StaffRoleDto>>> Handle(GetStaffRolesQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Success(Roles));
    }
}
