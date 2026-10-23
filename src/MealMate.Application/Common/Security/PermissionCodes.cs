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
    public const string InfluencerManage = "influencer.manage";

    // Delivery managers & dispatch operations
    public const string DeliveryManagersManage = "delivery.managers.manage";
    public const string DeliveryDispatchManage = "delivery.dispatch.manage";
    public const string DeliveryTripsManage = "delivery.trips.manage";
    public const string DeliveryDriversManage = "delivery.drivers.manage";
    public const string AdminCoverageOperationsView = "admin.coverage.operations.view";

    // Customer admin ops
    public const string CustomersView = "customers.view";
    public const string CustomersSuspend = "customers.suspend";
    public const string CustomersWalletsManage = "customers.wallets.manage";

    // Unified support
    public const string SupportView = "support:view";
    public const string SupportReply = "support.reply";
    public const string SupportManage = "support.manage";
    public const string SupportInternal = "support.internal";

    // Settings
    public const string SettingsManage = "settings.manage";

    // M02 Programs & Subscriptions
    public const string ModulePrograms = "M02";
    public const string ProgramsView = "programs.view";
    public const string ProgramsManage = "programs.manage";
    public const string ProgramsApprove = "programs.approve";
    public const string BundlesView = "bundles.view";
    public const string BundlesManage = "bundles.manage";
    public const string SubscriptionsView = "subscriptions.view";
    public const string SubscriptionsManage = "subscriptions.manage";
    public const string SubscriptionsPricing = "subscriptions.pricing";

    // M15 Finance governance
    public const string FinanceGovernanceView = "finance.governance.view";
    public const string FinanceGovernanceManage = "finance.governance.manage";
    public const string FinanceGovernanceApprove = "finance.governance.approve";
    public const string FinancePricingView = "finance.pricing.view";
    public const string FinancePricingManage = "finance.pricing.manage";
    public const string FinancePricingApprove = "finance.pricing.approve";

    public const string LedgerView = "ledger.view";
    public const string LedgerManage = "ledger.manage";
    public const string LedgerApprove = "ledger.approve";
    public const string LedgerPeriodsManage = "ledger.periods.manage";

    // M04 Restaurant Meals & Ingredients
    public const string RestaurantMealsView = "restaurant.meals.view";
    public const string RestaurantMealsManage = "restaurant.meals.manage";
    public const string RestaurantMealsApprove = "restaurant.meals.approve";
    public const string RestaurantMealsReject = "restaurant.meals.reject";
    public const string RestaurantIngredientsView = "restaurant.ingredients.view";
    public const string RestaurantIngredientsManage = "restaurant.ingredients.manage";

    // M11 Collaborative Campaigns
    public const string ModuleCampaigns = "M11";
    public const string CampaignsView = "campaigns.view";
    public const string CampaignsManage = "campaigns.manage";
    public const string OffersView = "offers.view";
    public const string OffersManage = "offers.manage";

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
        (InfluencerManage, "Manage influencers & withdrawals", "إدارة المؤثرين وطلبات السحب", Module),
        (DeliveryManagersManage, "Manage delivery managers", "إدارة مدراء التوصيل", Module),
        (DeliveryDispatchManage, "Manage dispatch operations", "إدارة عمليات التوصيل والرحلات", Module),
        (DeliveryTripsManage, "Manage trips and driver assignments", "إدارة الرحلات وتعيين السائقين", Module),
        (DeliveryDriversManage, "Manage dispatch drivers", "إدارة سائقي التوصيل", Module),
        (AdminCoverageOperationsView, "View coverage operations trips and driver locations", "عرض رحلات التغطية ومواقع المندوبين", Module),
        (CustomersView, "View customers", "عرض العملاء", Module),
        (CustomersSuspend, "Suspend customers", "إيقاف العملاء", Module),
        (CustomersWalletsManage, "Manage customer wallets", "إدارة محافظ العملاء", Module),
        (SupportView, "View support cases", "عرض حالات الدعم", Module),
        (SupportReply, "Reply to support cases", "الرد على حالات الدعم", Module),
        (SupportManage, "Manage support cases", "إدارة حالات الدعم", Module),
        (SupportInternal, "Write internal support notes", "كتابة ملاحظات الدعم الداخلية", Module),
        (SettingsManage, "Manage system settings", "إدارة إعدادات النظام", Module),
        (ProgramsView, "View nutrition programs", "عرض البرامج الغذائية", ModulePrograms),
        (ProgramsManage, "Manage nutrition programs", "إدارة البرامج الغذائية", ModulePrograms),
        (ProgramsApprove, "Approve nutrition programs", "اعتماد البرامج الغذائية", ModulePrograms),
        (BundlesView, "View meal bundles", "عرض باقات الوجبات", ModulePrograms),
        (BundlesManage, "Manage meal bundles", "إدارة باقات الوجبات", ModulePrograms),
        (SubscriptionsView, "View subscriptions catalog", "عرض كتالوج الاشتراكات", ModulePrograms),
        (SubscriptionsManage, "Manage subscriptions catalog", "إدارة كتالوج الاشتراكات", ModulePrograms),
        (SubscriptionsPricing, "Manage subscription pricing", "إدارة تسعير الاشتراكات", ModulePrograms),
        (FinanceGovernanceView, "View finance governance", "عرض حوكمة الشؤون المالية", "M15"),
        (FinanceGovernanceManage, "Manage finance governance drafts", "إدارة مسودات الحوكمة المالية", "M15"),
        (FinanceGovernanceApprove, "Approve finance governance rules", "اعتماد قواعد الحوكمة المالية", "M15"),
        (LedgerView, "View governed ledger", "عرض دفتر الأستاذ المحكوم", "M15"),
        (LedgerManage, "Manage chart, periods, and source drafts", "إدارة الحسابات والفترات ومسودات المستندات", "M15"),
        (LedgerApprove, "Approve ledger source documents", "اعتماد مستندات دفتر الأستاذ", "M15"),
        (LedgerPeriodsManage, "Manage accounting period lifecycle", "إدارة دورة حياة الفترات المحاسبية", "M15"),
        (RestaurantMealsView, "View restaurant menus & meals", "عرض قوائم ووجبات المطاعم", Module),
        (RestaurantMealsManage, "Manage restaurant menus & meals", "إدارة قوائم ووجبات المطاعم", Module),
        (RestaurantMealsApprove, "Approve restaurant meals", "اعتماد وجبات المطاعم", Module),
        (RestaurantMealsReject, "Reject restaurant meals", "رفض وجبات المطاعم", Module),
        (RestaurantIngredientsView, "View ingredient catalog", "عرض قائمة المكونات", Module),
        (RestaurantIngredientsManage, "Manage ingredient catalog", "إدارة قائمة المكونات", Module),
        (CampaignsView, "View collaborative campaigns", "عرض الحملات التعاونية", ModuleCampaigns),
        (CampaignsManage, "Manage collaborative campaigns", "إدارة الحملات التعاونية", ModuleCampaigns),
        (OffersView, "View offers & promotions", "عرض العروض والخصومات", ModuleCampaigns),
        (OffersManage, "Manage offers & promotions", "إدارة العروض والخصومات", ModuleCampaigns),
    ];
}
