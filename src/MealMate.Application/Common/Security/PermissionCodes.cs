namespace MealMate.Application.Common.Security;

/// <summary>Stable M01 permission catalog. Codes are embedded in JWT claims.</summary>
public static class PermissionCodes
{
    public const string Module = "M01";

    // Auth / self
    public const string AuthSelfManage = "auth.self.manage";

    // RBAC
    public const string RolesView = "rbac.roles.view";
    public const string RolesManage = "rbac.roles.manage";
    public const string UsersRolesAssign = "rbac.users.roles.assign";
    public const string ScopesManage = "rbac.scopes.manage";

    // Audit
    public const string AuditView = "audit.view";
    public const string AuditExport = "audit.export";

    // Admin users
    public const string AdminUsersView = "admin.users.view";
    public const string AdminUsersManage = "admin.users.manage";
    public const string AdminUsersInvite = "admin.users.invite";

    // Documents
    public const string DocumentsUpload = "documents.upload";
    public const string DocumentsReview = "documents.review";
    public const string DocumentsView = "documents.view";

    // Restaurant registration / approval
    public const string RestaurantRegCreate = "restaurant.registration.create";
    public const string RestaurantRegView = "restaurant.registration.view";
    public const string RestaurantRegApprove = "restaurant.registration.approve";
    public const string RestaurantRegReject = "restaurant.registration.reject";

    // Driver registration / approval
    public const string DriverRegCreate = "driver.registration.create";
    public const string DriverRegView = "driver.registration.view";
    public const string DriverRegApprove = "driver.registration.approve";
    public const string DriverRegReject = "driver.registration.reject";

    // Influencer
    public const string InfluencerRegCreate = "influencer.registration.create";
    public const string InfluencerLink = "influencer.link.manage";
    public const string InfluencerView = "influencer.view";

    // Delivery managers & dispatch operations
    public const string DeliveryManagersManage = "delivery.managers.manage";
    public const string DeliveryDispatchManage = "delivery.dispatch.manage";
    public const string DeliveryTripsManage = "delivery.trips.manage";
    public const string DeliveryDriversManage = "delivery.drivers.manage";

    // Customer admin ops
    public const string CustomersView = "customers.view";
    public const string CustomersSuspend = "customers.suspend";

    // Settings
    public const string SettingsManage = "settings.manage";

    public static IReadOnlyList<(string Code, string NameEn, string NameAr, string Module)> Catalog { get; } =
    [
        (AuthSelfManage, "Manage own auth session", "إدارة جلسة الحساب", Module),
        (RolesView, "View roles", "عرض الأدوار", Module),
        (RolesManage, "Manage roles", "إدارة الأدوار", Module),
        (UsersRolesAssign, "Assign user roles", "تعيين أدوار المستخدمين", Module),
        (ScopesManage, "Manage access scopes", "إدارة نطاقات الوصول", Module),
        (AuditView, "View audit logs", "عرض سجل التدقيق", Module),
        (AuditExport, "Export audit logs", "تصدير سجل التدقيق", Module),
        (AdminUsersView, "View admin users", "عرض مستخدمي الأدمن", Module),
        (AdminUsersManage, "Manage admin users", "إدارة مستخدمي الأدمن", Module),
        (AdminUsersInvite, "Invite admin users", "دعوة مستخدمي الأدمن", Module),
        (DocumentsUpload, "Upload documents", "رفع الوثائق", Module),
        (DocumentsReview, "Review documents", "مراجعة الوثائق", Module),
        (DocumentsView, "View documents", "عرض الوثائق", Module),
        (RestaurantRegCreate, "Create restaurant registration", "إنشاء تسجيل مطعم", Module),
        (RestaurantRegView, "View restaurant registrations", "عرض تسجيلات المطاعم", Module),
        (RestaurantRegApprove, "Approve restaurant registration", "اعتماد تسجيل مطعم", Module),
        (RestaurantRegReject, "Reject restaurant registration", "رفض تسجيل مطعم", Module),
        (DriverRegCreate, "Create driver registration", "إنشاء تسجيل مندوب", Module),
        (DriverRegView, "View driver registrations", "عرض تسجيلات المندوبين", Module),
        (DriverRegApprove, "Approve driver registration", "اعتماد تسجيل مندوب", Module),
        (DriverRegReject, "Reject driver registration", "رفض تسجيل مندوب", Module),
        (InfluencerRegCreate, "Create influencer registration", "إنشاء تسجيل مؤثر", Module),
        (InfluencerLink, "Link influencer account", "ربط حساب مؤثر", Module),
        (InfluencerView, "View influencers", "عرض المؤثرين", Module),
        (DeliveryManagersManage, "Manage delivery managers", "إدارة مدراء التوصيل", Module),
        (DeliveryDispatchManage, "Manage dispatch operations", "إدارة عمليات التوصيل والرحلات", Module),
        (DeliveryTripsManage, "Manage trips and driver assignments", "إدارة الرحلات وتعيين السائقين", Module),
        (DeliveryDriversManage, "Manage dispatch drivers", "إدارة سائقي التوصيل", Module),
        (CustomersView, "View customers", "عرض العملاء", Module),
        (CustomersSuspend, "Suspend customers", "إيقاف العملاء", Module),
        (SettingsManage, "Manage system settings", "إدارة إعدادات النظام", Module),
    ];
}
