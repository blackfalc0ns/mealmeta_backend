using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDeliveryAddressSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    BodyEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllergenDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllergenDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceChannel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BranchStaffAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    IsBranchManager = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchStaffAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusyStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsBusy = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusyStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalDiscountRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    MealMateShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RestaurantShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    MaxSubscribers = table.Column<int>(type: "integer", nullable: true),
                    CurrentSubscribers = table.Column<int>(type: "integer", nullable: false),
                    EnrollmentOpensAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnrollmentClosesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CapacityHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChartOfAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartOfAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChartOfAccounts_ChartOfAccounts_ParentId",
                        column: x => x.ParentId,
                        principalTable: "ChartOfAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintDeductionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintDeductionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Complaints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Complaints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DialCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false, defaultValue: "+965"),
                    FlagEmoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "🇰🇼"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlockRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Governorate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Block = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Building = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Floor = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOnDelivery = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAddresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAllergyPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllergenCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AllergenNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllergenNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsDislike = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAllergyPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBankDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iban = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBankDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCashWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Balance_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCashWallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerDietaryPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDietaryPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DefaultRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryAddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveryLatitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DeliveryLongitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    OnboardingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    ProfileImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DietaryPattern = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DietaryNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AllergiesCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DietaryPreferencesCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActiveDeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveDeliveryAddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActiveDeliveryRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveDeliveryBlockRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingDeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingDeliveryAddressEffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPromotionalWallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Balance_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPromotionalWallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerWithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerWithdrawalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyCapacities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CapacityType = table.Column<int>(type: "integer", nullable: false),
                    MaxBoxes = table.Column<int>(type: "integer", nullable: false),
                    ReservedBoxes = table.Column<int>(type: "integer", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCapacities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeferredRevenues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OriginalAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RemainingAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeferredRevenues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryConfirmationScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhotoPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryConfirmationScans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiversificationLimitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NSnapshot = table.Column<int>(type: "integer", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    RawLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    LMin = table.Column<int>(type: "integer", nullable: false),
                    LMax = table.Column<int>(type: "integer", nullable: false),
                    LimitValue = table.Column<int>(type: "integer", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiversificationLimitSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverPerformanceStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveredCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    AvgRating = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    OnTimeRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverPerformanceStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FullNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VehiclePlate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProfileImageStatus = table.Column<int>(type: "integer", nullable: false),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DriverRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverRatings", x => x.Id);
                    table.CheckConstraint("CK_DriverRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
                });

            migrationBuilder.CreateTable(
                name: "DriverRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    FullNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NationalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VehiclePlate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangeRequestNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultingDriverProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagerCustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SelectedFamilyPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscountPercentSnapshot = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    QuotedFinalPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentInvoiceId = table.Column<string>(type: "text", nullable: true),
                    PaymentId = table.Column<string>(type: "text", nullable: true),
                    PaymentAnchorSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConvertedFromSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAuditTrails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAuditTrails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialSettlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GrossTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GrossTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NetTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    NetTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialSettlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FoodPreferenceDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodPreferenceDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DiscountAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DiscountAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CommissionRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EligibleDurationsJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerCustomerLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AttributionSource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerCustomerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerEligibilityRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerEligibilityRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerPayableLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CommissionAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerPayableLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InfluencerCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LinkedCustomerUserIdRaw = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LinkedCustomerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultCommissionRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Iban = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerWithdrawalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerWithdrawalRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngredientFoodPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientFoodPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ingredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultCaloriesPer100g = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DefaultProteinPer100g = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DefaultCarbsPer100g = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DefaultFatPer100g = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    AllergenCodes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "text", nullable: true),
                    DefaultWeightGrams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    OriginCountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsLocal = table.Column<bool>(type: "boolean", nullable: false),
                    IsHalalCertified = table.Column<bool>(type: "boolean", nullable: false),
                    TraceabilityNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsOriginVisibleToCustomers = table.Column<bool>(type: "boolean", nullable: false),
                    IsCore = table.Column<bool>(type: "boolean", nullable: false),
                    RestaurantIconUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MealIconUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ingredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealBarcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    BarcodeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealBarcodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealFoodPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferenceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealFoodPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TotalBoxes = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealLabels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    LabelPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealPayables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrossSupply_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GrossSupply_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Commission_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Commission_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CampaignContributionNet_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CampaignContributionNet_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    NetPayable_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    NetPayable_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPayables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuPlanAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPlanAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MenuPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    IsTemplate = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Menus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menus", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MyFatoorahGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayName = table.Column<string>(type: "text", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: false),
                    EncryptedApiToken = table.Column<string>(type: "text", nullable: false),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: false),
                    IsKnetEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisaMasterEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsApplePayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    MaskedToken = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyFatoorahGatewayConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperatingExpenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IncurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingExpenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Destination = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RelativeFee = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    FixedFee_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    FixedFee_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MinFee_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    MinFee_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MaxFee_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    MaxFee_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentGatewayConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentWebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Gateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FulfillmentAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastFulfillmentErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastFulfillmentAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FulfillmentCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfitabilityAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassificationRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceDailyBox = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DailySupplyGross = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MeanReference = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerDailyPrice = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedProfit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MinimumProfit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfitabilityAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProgramMealSlotRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramMealSlotRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProgramNutritionStandards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealCategory = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    KcalMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    KcalMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProteinGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ProteinGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CarbMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CarbsGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CarbsGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FatGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FatGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FiberGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedProteinGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedProteinGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedCarbsGMin = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CookedCarbsGMax = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    KetoDayNetCarbsLimit = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    StandardVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramNutritionStandards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ShortCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    KeywordsAr = table.Column<string[]>(type: "text[]", nullable: false),
                    KeywordsEn = table.Column<string[]>(type: "text[]", nullable: false),
                    ImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableForFamily = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Programs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    DiscountAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    DiscountAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EligibleDurationDaysJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RewardPercent = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantAccountStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    OpeningBalance_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OpeningBalance_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ClosingBalance_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ClosingBalance_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantAccountStatements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantBranches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchCode = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    AddressAr = table.Column<string>(type: "text", nullable: false),
                    AddressEn = table.Column<string>(type: "text", nullable: false),
                    GovernorateAr = table.Column<string>(type: "text", nullable: false),
                    GovernorateEn = table.Column<string>(type: "text", nullable: false),
                    ContactPhone = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    ManagerStaffId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhotoStorageKey = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantBranches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantCommissionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrossSupply_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GrossSupply_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    CommissionAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CommissionAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantCommissionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantContractTerminations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantContractTerminations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantCustomRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    DescriptionAr = table.Column<string>(type: "text", nullable: false),
                    DescriptionEn = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantCustomRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantDailyOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResolvedMenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedMenuDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleMode = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BoxCount = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantDailyOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantDriverLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantDriverLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantMonthlyInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GrossTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GrossTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CommissionTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CommissionTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DeductionsTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DeductionsTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NetTotal_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    NetTotal_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantMonthlyInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyAr = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    BodyEn = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Section = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantOperatingDayExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantOperatingDayExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantOperatingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantOperatingSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantPayableLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    GrossAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    GrossAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CommissionAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CommissionAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    NetAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    NetAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CampaignContribution_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CampaignContribution_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantPayableLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantPayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BankReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantPayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TradeNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TradeNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CommercialLicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    IsOperationallyActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantRatings", x => x.Id);
                    table.CheckConstraint("CK_RestaurantRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
                });

            migrationBuilder.CreateTable(
                name: "RestaurantRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LegalNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LegalNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TradeNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TradeNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CommercialRegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CommercialRegistrationExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    CommercialLicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CommercialLicenseExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    FoodLicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FoodLicenseExpiry = table.Column<DateOnly>(type: "date", nullable: true),
                    LegalEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompanyCivilId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InstagramHandle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RestaurantCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OwnerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AuthorizedSignatoryCivilId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AuthorizedSignatoryTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrimaryContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrimaryContactTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PrimaryContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    PrimaryContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BankBeneficiaryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SwiftCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SettlementCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceRegionIdsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ChangeRequestNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultingRestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryByRestaurantConfirmed = table.Column<bool>(type: "boolean", nullable: true),
                    DataResponsibilityConfirmed = table.Column<bool>(type: "boolean", nullable: true),
                    SubmissionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RestaurantNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantReviewHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantReviewHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Section = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    VersionId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantReviewItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantRolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ModuleKey = table.Column<string>(type: "text", nullable: false),
                    PermissionLevel = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantRolePermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantServiceRegions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryWindowStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DeliveryWindowEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PreparationLeadTimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantServiceRegions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantSettingsChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    AdminNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantSettingsChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VersionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleVersions_RuleVersions_PreviousVersionId",
                        column: x => x.PreviousVersionId,
                        principalTable: "RuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafeContactLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Within3Km = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafeContactLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecondDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecondDeliveryAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantRegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullNameAr = table.Column<string>(type: "text", nullable: false),
                    FullNameEn = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    AllBranches = table.Column<bool>(type: "boolean", nullable: false),
                    BranchIdsJson = table.Column<string>(type: "text", nullable: true),
                    InviteToken = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffInvitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalServiceDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionCancellations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountedDays = table.Column<int>(type: "integer", nullable: false),
                    UnusedDays = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionCancellations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionDayMealSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredSlotsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MealsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionDayMealSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionFreezes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DaysCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionFreezes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GatewayCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GatewayTransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ValueJson = table.Column<string>(type: "jsonb", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatcherUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UserType = table.Column<int>(type: "integer", nullable: false),
                    AccountStatus = table.Column<int>(type: "integer", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletType = table.Column<int>(type: "integer", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BalanceAfter_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    BalanceAfter_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignAccountingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerPriceBefore_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerPriceBefore_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CustomerDiscount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerDiscount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CustomerPriceAfter_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerPriceAfter_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    MealMateFundingNet_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    MealMateFundingNet_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RestaurantFundingNet_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RestaurantFundingNet_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    BaseSupplyGross_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    BaseSupplyGross_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CampaignSupplyGross_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CampaignSupplyGross_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Commission_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Commission_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RestaurantNet_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RestaurantNet_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReferenceDailyBox_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReferenceDailyBox_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAccountingEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignAccountingEntries_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignBundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignBundles_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignCapacityReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignCapacityReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignCapacityReviews_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignDiscountSplits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    MealMateShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RestaurantShareRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignDiscountSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignDiscountSplits_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignPrograms_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRestaurantCapacities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxBoxes = table.Column<int>(type: "integer", nullable: false),
                    ReservedBoxes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRestaurantCapacities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRestaurantCapacities_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRestaurantEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapacityOffered = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRestaurantEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRestaurantEnrollments_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignSubscriberCaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cap = table.Column<int>(type: "integer", nullable: false),
                    CurrentCount = table.Column<int>(type: "integer", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignSubscriberCaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignSubscriberCaps_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminComplaintReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminComplaintReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminComplaintReviews_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintClassifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ClassifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClassifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintClassifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintClassifications_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComplaintEvidencePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintEvidencePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintEvidencePhotos_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCompensations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompensationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    FreeDays = table.Column<int>(type: "integer", nullable: true),
                    PromoCreditAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    PromoCreditAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCompensations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCompensations_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FreeDayCompensations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysGranted = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreeDayCompensations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreeDayCompensations_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResponsibilityDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    Party = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsibilityDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsibilityDecisions_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantBoxDeductions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoxesDeducted = table.Column<int>(type: "integer", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantBoxDeductions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantBoxDeductions_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantComplaintResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RespondedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantComplaintResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantComplaintResponses_Complaints_ComplaintId",
                        column: x => x.ComplaintId,
                        principalTable: "Complaints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Regions_Regions_ParentRegionId",
                        column: x => x.ParentRegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultLanguage = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenants_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVerifications_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerCommissionCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAmountAfterDiscount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PaidAmountAfterDiscount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    CommissionAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CommissionAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerCommissionCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfluencerCommissionCalculations_InfluencerCodes_CodeId",
                        column: x => x.CodeId,
                        principalTable: "InfluencerCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerPayoutHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfluencerProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BankReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceiptPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerPayoutHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfluencerPayoutHistories_InfluencerWithdrawalRequests_With~",
                        column: x => x.WithdrawalRequestId,
                        principalTable: "InfluencerWithdrawalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerWithdrawalApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerWithdrawalApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfluencerWithdrawalApprovals_InfluencerWithdrawalRequests_~",
                        column: x => x.WithdrawalRequestId,
                        principalTable: "InfluencerWithdrawalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyRevenueRecognitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRevenueRecognitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRevenueRecognitions_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntryLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Debit_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Debit_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Credit_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Credit_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Memo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_ChartOfAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "ChartOfAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuPlanPrograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsPrimaryOwner = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPlanPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuPlanPrograms_MenuPlans_MenuPlanId",
                        column: x => x.MenuPlanId,
                        principalTable: "MenuPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuPlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuPlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuPlanVersions_MenuPlans_MenuPlanId",
                        column: x => x.MenuPlanId,
                        principalTable: "MenuPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Meals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Slot = table.Column<string>(type: "text", nullable: true),
                    IsAvailableForNewOrders = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedForNewOrdersAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovalStatus = table.Column<int>(type: "integer", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Calories = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    ProteinG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CarbsG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    FatG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    ServingWeightG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    FiberG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    NetCarbsG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    SugarAlcoholG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    SugarAlcoholType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SaturatedFatG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    TotalSugarsG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    AddedSugarsG = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    SodiumMg = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    NutritionSource = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AnalysisReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VerificationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    VerifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecipeVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RestaurantNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AllergensReviewed = table.Column<bool>(type: "boolean", nullable: false),
                    PresentAllergenCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MayContainAllergenCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CrossContactNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DataResponsibilityConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    NutritionTagCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SearchExclusionTagCodes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meals_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MealsPerDay = table.Column<int>(type: "integer", nullable: false),
                    BreakfastCount = table.Column<int>(type: "integer", nullable: false),
                    LunchCount = table.Column<int>(type: "integer", nullable: false),
                    DinnerCount = table.Column<int>(type: "integer", nullable: false),
                    SnackCount = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionAr = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableForFamily = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bundles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bundles_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    HandedOverAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderHandovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderHandovers_RestaurantDailyOrders_DailyOrderId",
                        column: x => x.DailyOrderId,
                        principalTable: "RestaurantDailyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantConfirmations_RestaurantDailyOrders_DailyOrderId",
                        column: x => x.DailyOrderId,
                        principalTable: "RestaurantDailyOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantPartialPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthlyInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantPartialPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantPartialPayments_RestaurantMonthlyInvoices_Monthly~",
                        column: x => x.MonthlyInvoiceId,
                        principalTable: "RestaurantMonthlyInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferReceipts_RestaurantPayouts_PayoutId",
                        column: x => x.PayoutId,
                        principalTable: "RestaurantPayouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantRegistrationFieldReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RegistrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantRegistrationFieldReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantRegistrationFieldReviews_RestaurantRegistrations_~",
                        column: x => x.RegistrationId,
                        principalTable: "RestaurantRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingRuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RMax = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RMin = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    DShort = table.Column<int>(type: "integer", nullable: false),
                    DLong = table.Column<int>(type: "integer", nullable: false),
                    Gamma = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundingStepKwd = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRuleVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingRuleVersions_RuleVersions_RuleVersionId",
                        column: x => x.RuleVersionId,
                        principalTable: "RuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SelectionDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedMenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAutoSelected = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryAddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveryRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryBlockRegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionDays_SubscriptionCalendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "SubscriptionCalendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CancellationFeeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeRate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    FeeAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FeeAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CancellationFeeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CancellationFeeRecords_SubscriptionCancellations_Cancellati~",
                        column: x => x.CancellationId,
                        principalTable: "SubscriptionCancellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefundCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundEligiblePaid_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RefundEligiblePaid_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    UnusedCashValue_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnusedCashValue_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CancellationFee_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    CancellationFee_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FinalCashRefund_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FinalCashRefund_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefundCalculations_SubscriptionCancellations_CancellationId",
                        column: x => x.CancellationId,
                        principalTable: "SubscriptionCancellations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FreezeLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreezeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysAdded = table.Column<int>(type: "integer", nullable: false),
                    EndDateBefore = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDateAfter = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FreezeLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FreezeLedgerEntries_SubscriptionFreezes_FreezeId",
                        column: x => x.FreezeId,
                        principalTable: "SubscriptionFreezes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    HeldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryHolds_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTrackingPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTrackingPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryTrackingPoints_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DriverAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriverAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriverAssignments_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PickupScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    DriverProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    BarcodeValue = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhotoPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ScannedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickupScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickupScans_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthLoginChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NextStep = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthLoginChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthLoginChallenges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignRestaurantApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRestaurantApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignRestaurantApprovals_CampaignRestaurantEnrollments_E~",
                        column: x => x.EnrollmentId,
                        principalTable: "CampaignRestaurantEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessScopes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InfluencerCancellationImpacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionCalculationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CancellationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReversedAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfluencerCancellationImpacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfluencerCancellationImpacts_InfluencerCommissionCalculati~",
                        column: x => x.CommissionCalculationId,
                        principalTable: "InfluencerCommissionCalculations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayNumber = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuDays_MenuPlanVersions_MenuPlanVersionId",
                        column: x => x.MenuPlanVersionId,
                        principalTable: "MenuPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealAllergens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllergenCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AllergenNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllergenNameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealAllergens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealAllergens_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    AltTextAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AltTextEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealImages_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    IngredientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PortionGrams = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealIngredients_Ingredients_IngredientId",
                        column: x => x.IngredientId,
                        principalTable: "Ingredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MealIngredients_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealProgramAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealProgramAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealProgramAssignments_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealRatings", x => x.Id);
                    table.CheckConstraint("CK_MealRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
                    table.ForeignKey(
                        name: "FK_MealRatings_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WeekdayMealAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekdayMealAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekdayMealAssignments_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeekdayMealAssignments_MenuPlanVersions_MenuPlanVersionId",
                        column: x => x.MenuPlanVersionId,
                        principalTable: "MenuPlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassificationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeanReferenceDaily = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    SdReferenceDaily = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    LowerBound = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UpperBound = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RestaurantCount = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationRuns_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClassificationRuns_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageContentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ContentSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageContentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageContentVersions_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackageContentVersions_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantBundlePricings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferencePrice26_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReferencePrice26_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ContractedSupplyPrice26_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ContractedSupplyPrice26_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LockStatus = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeRequestReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PreviousPricingId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantBundlePricings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantBundlePricings_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestaurantBundlePricings_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestaurantBundlePricings_RestaurantBundlePricings_PreviousP~",
                        column: x => x.PreviousPricingId,
                        principalTable: "RestaurantBundlePricings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantProgramBundleOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantProgramBundleOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantProgramBundleOffers_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestaurantProgramBundleOffers_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyMealSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolvedMenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedMenuDayId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleMode = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    SelectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyMealSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyMealSelections_SubscriptionDays_SubscriptionDayId",
                        column: x => x.SubscriptionDayId,
                        principalTable: "SubscriptionDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyRestaurantSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SelectionSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyRestaurantSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyRestaurantSelections_SubscriptionDays_SubscriptionDayId",
                        column: x => x.SubscriptionDayId,
                        principalTable: "SubscriptionDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MealChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromMealId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToMealId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealChangeRequests_SubscriptionDays_SubscriptionDayId",
                        column: x => x.SubscriptionDayId,
                        principalTable: "SubscriptionDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdminInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RoleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccessScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InvitedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminInvitations_AccessScopes_AccessScopeId",
                        column: x => x.AccessScopeId,
                        principalTable: "AccessScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserScopes_AccessScopes_AccessScopeId",
                        column: x => x.AccessScopeId,
                        principalTable: "AccessScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuDayMeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuDayId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuDayMeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuDayMeals_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MenuDayMeals_MenuDays_MenuDayId",
                        column: x => x.MenuDayId,
                        principalTable: "MenuDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantClassifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassificationRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    ReferenceDailyBox = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsOutlierFlagged = table.Column<bool>(type: "boolean", nullable: false),
                    OutlierReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantClassifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestaurantClassifications_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestaurantClassifications_ClassificationRuns_Classification~",
                        column: x => x.ClassificationRunId,
                        principalTable: "ClassificationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RestaurantClassifications_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscountsAndReferralLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscountType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Amount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Amount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FundingMode = table.Column<int>(type: "integer", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountsAndReferralLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMemberAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasSeparateAccount = table.Column<bool>(type: "boolean", nullable: false),
                    LinkedCustomerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    InviteTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    InviteExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPaused = table.Column<bool>(type: "boolean", nullable: false),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMemberAccesses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMemberPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EditDailyMeals = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeDeliveryTime = table.Column<bool>(type: "boolean", nullable: false),
                    ViewProgram = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeProgram = table.Column<bool>(type: "boolean", nullable: false),
                    EditMealsAndDays = table.Column<bool>(type: "boolean", nullable: false),
                    EditAddress = table.Column<bool>(type: "boolean", nullable: false),
                    UnitExchange = table.Column<bool>(type: "boolean", nullable: false),
                    ChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMemberPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RelationshipCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    AgeYears = table.Column<int>(type: "integer", nullable: false),
                    NutritionalGoalCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActivityLevel = table.Column<int>(type: "integer", nullable: false),
                    DeliveryAddressId = table.Column<Guid>(type: "uuid", nullable: true),
                    SetupStatus = table.Column<int>(type: "integer", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedTier = table.Column<int>(type: "integer", nullable: true),
                    DurationDays = table.Column<int>(type: "integer", nullable: true),
                    IncludeFriday = table.Column<bool>(type: "boolean", nullable: false),
                    MemberSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyMembers_FamilyGroups_FamilyGroupId",
                        column: x => x.FamilyGroupId,
                        principalTable: "FamilyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedTier = table.Column<int>(type: "integer", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    IncludeFriday = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CountryId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PricingRuleVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromoCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    InfluencerCodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageContentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAmount_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PaidAmount_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastOperationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentInvoiceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FulfillmentAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastFulfillmentAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFulfillmentErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FulfillmentCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    FamilyGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    FamilyMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Bundles_BundleId",
                        column: x => x.BundleId,
                        principalTable: "Bundles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_FamilyGroups_FamilyGroupId",
                        column: x => x.FamilyGroupId,
                        principalTable: "FamilyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_FamilyMembers_FamilyMemberId",
                        column: x => x.FamilyMemberId,
                        principalTable: "FamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_PackageContentVersions_PackageContentVersionId",
                        column: x => x.PackageContentVersionId,
                        principalTable: "PackageContentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_PricingRuleVersions_PricingRuleVersionId",
                        column: x => x.PricingRuleVersionId,
                        principalTable: "PricingRuleVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Subscriptions_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FamilySubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimarySubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberCustomerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Relationship = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MaxMealsAllowedPerDay = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilySubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilySubscriptions_Subscriptions_PrimarySubscriptionId",
                        column: x => x.PrimarySubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionFreezeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FreezeStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FreezeEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FreezeDays = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionFreezeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionFreezeRequests_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByUserType = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionOperations_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRenewals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousSubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NewSubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRenewals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRenewals_Subscriptions_NewSubscriptionId",
                        column: x => x.NewSubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionRenewals_Subscriptions_PreviousSubscriptionId",
                        column: x => x.PreviousSubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantPoolSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EligibilityCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RestaurantCount = table.Column<int>(type: "integer", nullable: false),
                    IsBackfilled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRestaurantPoolSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantPoolSnapshots_Subscriptions_Subscript~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSnapshotMigrationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSnapshotMigrationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSnapshotMigrationIssues_Subscriptions_Subscript~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    A_t = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ROfD = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    NSnapshot = table.Column<int>(type: "integer", nullable: false),
                    LimitValue = table.Column<int>(type: "integer", nullable: false),
                    LMin = table.Column<int>(type: "integer", nullable: false),
                    LMax = table.Column<int>(type: "integer", nullable: false),
                    ReferencePricesJson = table.Column<string>(type: "jsonb", nullable: false),
                    SupplyPricesJson = table.Column<string>(type: "jsonb", nullable: false),
                    RuleVersionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionSnapshots_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionTierUpgrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromTier = table.Column<int>(type: "integer", nullable: false),
                    ToTier = table.Column<int>(type: "integer", nullable: false),
                    PriceDifference_Amount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PriceDifference_CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionTierUpgrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionTierUpgrades_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsInitiallySelected = table.Column<bool>(type: "boolean", nullable: false),
                    OfferingSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRestaurantSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantSnapshots_SubscriptionRestaurantPoolS~",
                        column: x => x.PoolSnapshotId,
                        principalTable: "SubscriptionRestaurantPoolSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantSnapshots_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionRestaurantDaySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RestaurantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MenuPlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotRequirementsJson = table.Column<string>(type: "jsonb", nullable: false),
                    MealsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionRestaurantDaySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantDaySnapshots_SubscriptionRestaurantSn~",
                        column: x => x.RestaurantSnapshotId,
                        principalTable: "SubscriptionRestaurantSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionRestaurantDaySnapshots_Subscriptions_Subscripti~",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScopes_ScopeType_CountryId_RegionId_RestaurantId_Tena~",
                table: "AccessScopes",
                columns: new[] { "ScopeType", "CountryId", "RegionId", "RestaurantId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScopes_TenantId",
                table: "AccessScopes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminComplaintReviews_ComplaintId",
                table: "AdminComplaintReviews",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminComplaintReviews_ReviewedAtUtc",
                table: "AdminComplaintReviews",
                column: "ReviewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminComplaintReviews_ReviewerUserId",
                table: "AdminComplaintReviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminComplaintReviews_Status",
                table: "AdminComplaintReviews",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_AccessScopeId",
                table: "AdminInvitations",
                column: "AccessScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_NormalizedEmail",
                table: "AdminInvitations",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AdminInvitations_Status",
                table: "AdminInvitations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_Channel",
                table: "AdminNotifications",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_IsRead",
                table: "AdminNotifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_SentAtUtc",
                table: "AdminNotifications",
                column: "SentAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_UserId",
                table: "AdminNotifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotifications_UserId_IsRead_SentAtUtc",
                table: "AdminNotifications",
                columns: new[] { "UserId", "IsRead", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AllergenDefinitions_Code",
                table: "AllergenDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AllergenDefinitions_IsActive_SortOrder",
                table: "AllergenDefinitions",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CorrelationId",
                table: "AuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAtUtc",
                table: "AuditLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthLoginChallenges_ExpiresAtUtc",
                table: "AuthLoginChallenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthLoginChallenges_TokenHash",
                table: "AuthLoginChallenges",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthLoginChallenges_UserId",
                table: "AuthLoginChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_AvailableForFamily",
                table: "Bundles",
                column: "AvailableForFamily");

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_IsActive",
                table: "Bundles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_ProgramId_Code",
                table: "Bundles",
                columns: new[] { "ProgramId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bundles_Status",
                table: "Bundles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BusyStatuses_EndsAtUtc",
                table: "BusyStatuses",
                column: "EndsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BusyStatuses_RestaurantId",
                table: "BusyStatuses",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_BusyStatuses_RestaurantId_IsBusy_StartsAtUtc",
                table: "BusyStatuses",
                columns: new[] { "RestaurantId", "IsBusy", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_CampaignId",
                table: "CampaignAccountingEntries",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_CampaignId_RestaurantId_Recognize~",
                table: "CampaignAccountingEntries",
                columns: new[] { "CampaignId", "RestaurantId", "RecognizedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_FundingMode",
                table: "CampaignAccountingEntries",
                column: "FundingMode");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_RecognizedAtUtc",
                table: "CampaignAccountingEntries",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_RestaurantId",
                table: "CampaignAccountingEntries",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAccountingEntries_SubscriptionDayId",
                table: "CampaignAccountingEntries",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBundles_BundleId",
                table: "CampaignBundles",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignBundles_CampaignId_BundleId",
                table: "CampaignBundles",
                columns: new[] { "CampaignId", "BundleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCapacityReviews_CampaignId",
                table: "CampaignCapacityReviews",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCapacityReviews_Decision",
                table: "CampaignCapacityReviews",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCapacityReviews_ReviewedAtUtc",
                table: "CampaignCapacityReviews",
                column: "ReviewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignCapacityReviews_ReviewedByUserId",
                table: "CampaignCapacityReviews",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDiscountSplits_CampaignId",
                table: "CampaignDiscountSplits",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignDiscountSplits_FundingMode",
                table: "CampaignDiscountSplits",
                column: "FundingMode");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPrograms_CampaignId_ProgramId",
                table: "CampaignPrograms",
                columns: new[] { "CampaignId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPrograms_ProgramId",
                table: "CampaignPrograms",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantApprovals_ApprovedByUserId",
                table: "CampaignRestaurantApprovals",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantApprovals_DecidedAtUtc",
                table: "CampaignRestaurantApprovals",
                column: "DecidedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantApprovals_Decision",
                table: "CampaignRestaurantApprovals",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantApprovals_EnrollmentId",
                table: "CampaignRestaurantApprovals",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantCapacities_CampaignId",
                table: "CampaignRestaurantCapacities",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantCapacities_CampaignId_RestaurantId_Servic~",
                table: "CampaignRestaurantCapacities",
                columns: new[] { "CampaignId", "RestaurantId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantCapacities_RestaurantId",
                table: "CampaignRestaurantCapacities",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantCapacities_ServiceDate",
                table: "CampaignRestaurantCapacities",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantEnrollments_CampaignId",
                table: "CampaignRestaurantEnrollments",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantEnrollments_CampaignId_RestaurantId",
                table: "CampaignRestaurantEnrollments",
                columns: new[] { "CampaignId", "RestaurantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantEnrollments_RequestedAtUtc",
                table: "CampaignRestaurantEnrollments",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantEnrollments_RestaurantId",
                table: "CampaignRestaurantEnrollments",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRestaurantEnrollments_Status",
                table: "CampaignRestaurantEnrollments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_EnrollmentOpensAtUtc_EnrollmentClosesAtUtc",
                table: "Campaigns",
                columns: new[] { "EnrollmentOpensAtUtc", "EnrollmentClosesAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_FundingMode",
                table: "Campaigns",
                column: "FundingMode");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_StartsAtUtc_EndsAtUtc",
                table: "Campaigns",
                columns: new[] { "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_Status",
                table: "Campaigns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubscriberCaps_CampaignId",
                table: "CampaignSubscriberCaps",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignSubscriberCaps_LastReviewedAtUtc",
                table: "CampaignSubscriberCaps",
                column: "LastReviewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationFeeRecords_CancellationId",
                table: "CancellationFeeRecords",
                column: "CancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_CancellationFeeRecords_RuleVersionId",
                table: "CancellationFeeRecords",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityHolds_ExpiresAtUtc",
                table: "CapacityHolds",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityHolds_RestaurantId_ServiceDate_Status",
                table: "CapacityHolds",
                columns: new[] { "RestaurantId", "ServiceDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityHolds_SubscriptionId_ServiceDate",
                table: "CapacityHolds",
                columns: new[] { "SubscriptionId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_AccountType",
                table: "ChartOfAccounts",
                column: "AccountType");

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_Code",
                table: "ChartOfAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_IsActive",
                table: "ChartOfAccounts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ChartOfAccounts_ParentId",
                table: "ChartOfAccounts",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRuns_BundleId",
                table: "ClassificationRuns",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRuns_ProgramId_BundleId_RunAtUtc",
                table: "ClassificationRuns",
                columns: new[] { "ProgramId", "BundleId", "RunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRuns_RuleVersionId",
                table: "ClassificationRuns",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintClassifications_CategoryCode",
                table: "ComplaintClassifications",
                column: "CategoryCode");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintClassifications_ClassifiedAtUtc",
                table: "ComplaintClassifications",
                column: "ClassifiedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintClassifications_ClassifiedByUserId",
                table: "ComplaintClassifications",
                column: "ClassifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintClassifications_ComplaintId",
                table: "ComplaintClassifications",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintClassifications_Severity",
                table: "ComplaintClassifications",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintDeductionRecords_AppliedAtUtc",
                table: "ComplaintDeductionRecords",
                column: "AppliedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintDeductionRecords_ComplaintId",
                table: "ComplaintDeductionRecords",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintDeductionRecords_RestaurantId",
                table: "ComplaintDeductionRecords",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintDeductionRecords_Status",
                table: "ComplaintDeductionRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintEvidencePhotos_ComplaintId",
                table: "ComplaintEvidencePhotos",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintEvidencePhotos_UploadedAtUtc",
                table: "ComplaintEvidencePhotos",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Category",
                table: "Complaints",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_CustomerUserId",
                table: "Complaints",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_DriverProfileId",
                table: "Complaints",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_OrderId",
                table: "Complaints",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_RestaurantId",
                table: "Complaints",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Status",
                table: "Complaints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_SubmittedAtUtc",
                table: "Complaints",
                column: "SubmittedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_SubscriptionDayId",
                table: "Complaints",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Code",
                table: "Countries",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_IsActive",
                table: "Countries",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_BlockRegionId",
                table: "CustomerAddresses",
                column: "BlockRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAddresses_UserId_IsDefault",
                table: "CustomerAddresses",
                columns: new[] { "UserId", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllergyPreferences_AllergenCode",
                table: "CustomerAllergyPreferences",
                column: "AllergenCode");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllergyPreferences_CustomerUserId",
                table: "CustomerAllergyPreferences",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAllergyPreferences_CustomerUserId_AllergenCode_IsDi~",
                table: "CustomerAllergyPreferences",
                columns: new[] { "CustomerUserId", "AllergenCode", "IsDislike" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankDetails_CustomerUserId",
                table: "CustomerBankDetails",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankDetails_Iban",
                table: "CustomerBankDetails",
                column: "Iban");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBankDetails_IsVerified",
                table: "CustomerBankDetails",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashWallets_CustomerUserId",
                table: "CustomerCashWallets",
                column: "CustomerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashWallets_IsActive",
                table: "CustomerCashWallets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCompensations_CompensationType",
                table: "CustomerCompensations",
                column: "CompensationType");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCompensations_ComplaintId",
                table: "CustomerCompensations",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCompensations_CustomerUserId",
                table: "CustomerCompensations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCompensations_Status",
                table: "CustomerCompensations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDietaryPreferences_CustomerUserId_PreferenceCode",
                table: "CustomerDietaryPreferences",
                columns: new[] { "CustomerUserId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDietaryPreferences_PreferenceCode",
                table: "CustomerDietaryPreferences",
                column: "PreferenceCode");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_UserId",
                table: "CustomerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPromotionalWallets_CustomerUserId",
                table: "CustomerPromotionalWallets",
                column: "CustomerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPromotionalWallets_IsActive",
                table: "CustomerPromotionalWallets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWithdrawalRequests_CustomerUserId",
                table: "CustomerWithdrawalRequests",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWithdrawalRequests_RequestedAtUtc",
                table: "CustomerWithdrawalRequests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWithdrawalRequests_ReviewedByUserId",
                table: "CustomerWithdrawalRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWithdrawalRequests_Status",
                table: "CustomerWithdrawalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCapacities_CampaignId",
                table: "DailyCapacities",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCapacities_CapacityType",
                table: "DailyCapacities",
                column: "CapacityType");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCapacities_RestaurantId",
                table: "DailyCapacities",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCapacities_RestaurantId_ServiceDate_CapacityType_Campa~",
                table: "DailyCapacities",
                columns: new[] { "RestaurantId", "ServiceDate", "CapacityType", "CampaignId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyCapacities_ServiceDate",
                table: "DailyCapacities",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_MealId",
                table: "DailyMealSelections",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ResolvedMenuDayId",
                table: "DailyMealSelections",
                column: "ResolvedMenuDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ResolvedMenuPlanVersionId",
                table: "DailyMealSelections",
                column: "ResolvedMenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ResolvedProgramId",
                table: "DailyMealSelections",
                column: "ResolvedProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_ScheduleMode",
                table: "DailyMealSelections",
                column: "ScheduleMode");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_SubscriptionDayId",
                table: "DailyMealSelections",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_SubscriptionDayId_MealId",
                table: "DailyMealSelections",
                columns: new[] { "SubscriptionDayId", "MealId" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMealSelections_SubscriptionDayId_SortOrder",
                table: "DailyMealSelections",
                columns: new[] { "SubscriptionDayId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRestaurantSelections_RestaurantId",
                table: "DailyRestaurantSelections",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRestaurantSelections_SelectionSource",
                table: "DailyRestaurantSelections",
                column: "SelectionSource");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRestaurantSelections_SubscriptionDayId",
                table: "DailyRestaurantSelections",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRestaurantSelections_SubscriptionDayId_RestaurantId_Se~",
                table: "DailyRestaurantSelections",
                columns: new[] { "SubscriptionDayId", "RestaurantId", "SelectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueRecognitions_JournalEntryId",
                table: "DailyRevenueRecognitions",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueRecognitions_RecognizedAtUtc",
                table: "DailyRevenueRecognitions",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueRecognitions_SubscriptionDayId",
                table: "DailyRevenueRecognitions",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueRecognitions_SubscriptionDayId_SubscriptionId",
                table: "DailyRevenueRecognitions",
                columns: new[] { "SubscriptionDayId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyRevenueRecognitions_SubscriptionId",
                table: "DailyRevenueRecognitions",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeferredRevenues_Status",
                table: "DeferredRevenues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeferredRevenues_SubscriptionId",
                table: "DeferredRevenues",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmationScans_BarcodeValue",
                table: "DeliveryConfirmationScans",
                column: "BarcodeValue");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmationScans_DriverProfileId",
                table: "DeliveryConfirmationScans",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmationScans_OrderId",
                table: "DeliveryConfirmationScans",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmationScans_ScannedAtUtc",
                table: "DeliveryConfirmationScans",
                column: "ScannedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmationScans_SubscriptionDayId",
                table: "DeliveryConfirmationScans",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_HeldAtUtc",
                table: "DeliveryHolds",
                column: "HeldAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_OrderId",
                table: "DeliveryHolds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_Status",
                table: "DeliveryHolds",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_SubscriptionDayId",
                table: "DeliveryHolds",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryHolds_TripId",
                table: "DeliveryHolds",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrackingPoints_DriverProfileId",
                table: "DeliveryTrackingPoints",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrackingPoints_RecordedAtUtc",
                table: "DeliveryTrackingPoints",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrackingPoints_TripId",
                table: "DeliveryTrackingPoints",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrackingPoints_TripId_RecordedAtUtc",
                table: "DeliveryTrackingPoints",
                columns: new[] { "TripId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_AppliedAtUtc",
                table: "DiscountsAndReferralLogs",
                column: "AppliedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_Code",
                table: "DiscountsAndReferralLogs",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountsAndReferralLogs_SubscriptionId",
                table: "DiscountsAndReferralLogs",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_DiversificationLimitSnapshots_CapturedAtUtc",
                table: "DiversificationLimitSnapshots",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DiversificationLimitSnapshots_SubscriptionId",
                table: "DiversificationLimitSnapshots",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentType",
                table: "Documents",
                column: "DocumentType");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OwnerType_OwnerId",
                table: "Documents",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVerifications_DocumentId_Status",
                table: "DocumentVerifications",
                columns: new[] { "DocumentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVerifications_ReviewedByUserId",
                table: "DocumentVerifications",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_AssignedAtUtc",
                table: "DriverAssignments",
                column: "AssignedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_DriverProfileId",
                table: "DriverAssignments",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_Status",
                table: "DriverAssignments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_TripId",
                table: "DriverAssignments",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAssignments_TripId_DriverProfileId",
                table: "DriverAssignments",
                columns: new[] { "TripId", "DriverProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverPerformanceStats_DriverProfileId",
                table: "DriverPerformanceStats",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverPerformanceStats_DriverProfileId_PeriodStart_PeriodEnd",
                table: "DriverPerformanceStats",
                columns: new[] { "DriverProfileId", "PeriodStart", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverPerformanceStats_PeriodEnd",
                table: "DriverPerformanceStats",
                column: "PeriodEnd");

            migrationBuilder.CreateIndex(
                name: "IX_DriverPerformanceStats_PeriodStart",
                table: "DriverPerformanceStats",
                column: "PeriodStart");

            migrationBuilder.CreateIndex(
                name: "IX_DriverProfiles_RestaurantId",
                table: "DriverProfiles",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverProfiles_UserId",
                table: "DriverProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverProfiles_VerificationStatus",
                table: "DriverProfiles",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRatings_CustomerUserId",
                table: "DriverRatings",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRatings_DriverProfileId",
                table: "DriverRatings",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRatings_DriverProfileId_CustomerUserId_SubscriptionDa~",
                table: "DriverRatings",
                columns: new[] { "DriverProfileId", "CustomerUserId", "SubscriptionDayId" });

            migrationBuilder.CreateIndex(
                name: "IX_DriverRatings_Score",
                table: "DriverRatings",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRatings_SubscriptionDayId",
                table: "DriverRatings",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRegistrations_RestaurantId",
                table: "DriverRegistrations",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_DriverRegistrations_Status",
                table: "DriverRegistrations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_ConvertedFromSubscriptionId",
                table: "FamilyGroups",
                column: "ConvertedFromSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_ManagerCustomerUserId",
                table: "FamilyGroups",
                column: "ManagerCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_SelectedFamilyPackageId",
                table: "FamilyGroups",
                column: "SelectedFamilyPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_Status",
                table: "FamilyGroups",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_FamilyMemberId",
                table: "FamilyMemberAccesses",
                column: "FamilyMemberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_InviteTokenHash",
                table: "FamilyMemberAccesses",
                column: "InviteTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberAccesses_LinkedCustomerUserId",
                table: "FamilyMemberAccesses",
                column: "LinkedCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMemberPermissions_FamilyMemberId",
                table: "FamilyMemberPermissions",
                column: "FamilyMemberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_FamilyGroupId",
                table: "FamilyMembers",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyMembers_MemberSubscriptionId",
                table: "FamilyMembers",
                column: "MemberSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilySubscriptions_IsActive",
                table: "FamilySubscriptions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FamilySubscriptions_MemberCustomerUserId",
                table: "FamilySubscriptions",
                column: "MemberCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilySubscriptions_PrimarySubscriptionId_MemberCustomerUse~",
                table: "FamilySubscriptions",
                columns: new[] { "PrimarySubscriptionId", "MemberCustomerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_Action",
                table: "FinancialAuditTrails",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_ActorUserId",
                table: "FinancialAuditTrails",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_CorrelationId",
                table: "FinancialAuditTrails",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_EntityId",
                table: "FinancialAuditTrails",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_EntityType",
                table: "FinancialAuditTrails",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAuditTrails_OccurredAtUtc",
                table: "FinancialAuditTrails",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettings_IsActive",
                table: "FinancialSettings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettings_RuleVersionId",
                table: "FinancialSettings",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettings_SettingKey",
                table: "FinancialSettings",
                column: "SettingKey");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettings_SettingKey_IsActive",
                table: "FinancialSettings",
                columns: new[] { "SettingKey", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettlements_PeriodYear_PeriodMonth",
                table: "FinancialSettlements",
                columns: new[] { "PeriodYear", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettlements_RestaurantId",
                table: "FinancialSettlements",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettlements_RestaurantId_PeriodYear_PeriodMonth",
                table: "FinancialSettlements",
                columns: new[] { "RestaurantId", "PeriodYear", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettlements_SettledAtUtc",
                table: "FinancialSettlements",
                column: "SettledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialSettlements_Status",
                table: "FinancialSettlements",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FoodPreferenceDefinitions_Code",
                table: "FoodPreferenceDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FoodPreferenceDefinitions_IsActive_SortOrder",
                table: "FoodPreferenceDefinitions",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FreeDayCompensations_ComplaintId",
                table: "FreeDayCompensations",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_FreeDayCompensations_Status",
                table: "FreeDayCompensations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FreeDayCompensations_SubscriptionId",
                table: "FreeDayCompensations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeLedgerEntries_FreezeId",
                table: "FreezeLedgerEntries",
                column: "FreezeId");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeLedgerEntries_RecordedAtUtc",
                table: "FreezeLedgerEntries",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FreezeLedgerEntries_SubscriptionId",
                table: "FreezeLedgerEntries",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAtUtc",
                table: "IdempotencyRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Key",
                table: "IdempotencyRecords",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_UserId",
                table: "IdempotencyRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCancellationImpacts_CancellationId",
                table: "InfluencerCancellationImpacts",
                column: "CancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCancellationImpacts_CommissionCalculationId",
                table: "InfluencerCancellationImpacts",
                column: "CommissionCalculationId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCancellationImpacts_RecordedAtUtc",
                table: "InfluencerCancellationImpacts",
                column: "RecordedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCodes_Code",
                table: "InfluencerCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCodes_InfluencerProfileId",
                table: "InfluencerCodes",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCodes_IsActive",
                table: "InfluencerCodes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCodes_StartsAtUtc_EndsAtUtc",
                table: "InfluencerCodes",
                columns: new[] { "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCommissionCalculations_CalculatedAtUtc",
                table: "InfluencerCommissionCalculations",
                column: "CalculatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCommissionCalculations_CodeId",
                table: "InfluencerCommissionCalculations",
                column: "CodeId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCommissionCalculations_InfluencerProfileId",
                table: "InfluencerCommissionCalculations",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCommissionCalculations_Status",
                table: "InfluencerCommissionCalculations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCommissionCalculations_SubscriptionId",
                table: "InfluencerCommissionCalculations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCustomerLinks_CustomerUserId",
                table: "InfluencerCustomerLinks",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCustomerLinks_InfluencerProfileId",
                table: "InfluencerCustomerLinks",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCustomerLinks_InfluencerProfileId_CustomerUserId",
                table: "InfluencerCustomerLinks",
                columns: new[] { "InfluencerProfileId", "CustomerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCustomerLinks_IsActive",
                table: "InfluencerCustomerLinks",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerCustomerLinks_LinkedAtUtc",
                table: "InfluencerCustomerLinks",
                column: "LinkedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerEligibilityRules_InfluencerProfileId",
                table: "InfluencerEligibilityRules",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerEligibilityRules_IsActive",
                table: "InfluencerEligibilityRules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerEligibilityRules_RuleVersionId",
                table: "InfluencerEligibilityRules",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayableLedgers_InfluencerProfileId",
                table: "InfluencerPayableLedgers",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayableLedgers_RecognizedAtUtc",
                table: "InfluencerPayableLedgers",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayableLedgers_Status",
                table: "InfluencerPayableLedgers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayableLedgers_SubscriptionId",
                table: "InfluencerPayableLedgers",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayoutHistories_InfluencerProfileId",
                table: "InfluencerPayoutHistories",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayoutHistories_PaidAtUtc",
                table: "InfluencerPayoutHistories",
                column: "PaidAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerPayoutHistories_WithdrawalRequestId",
                table: "InfluencerPayoutHistories",
                column: "WithdrawalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerProfiles_InfluencerCode",
                table: "InfluencerProfiles",
                column: "InfluencerCode",
                unique: true,
                filter: "\"InfluencerCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerProfiles_LinkedCustomerUserId",
                table: "InfluencerProfiles",
                column: "LinkedCustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerProfiles_UserId",
                table: "InfluencerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalApprovals_ApprovedByUserId",
                table: "InfluencerWithdrawalApprovals",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalApprovals_DecidedAtUtc",
                table: "InfluencerWithdrawalApprovals",
                column: "DecidedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalApprovals_Decision",
                table: "InfluencerWithdrawalApprovals",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalApprovals_WithdrawalRequestId",
                table: "InfluencerWithdrawalApprovals",
                column: "WithdrawalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalRequests_InfluencerProfileId",
                table: "InfluencerWithdrawalRequests",
                column: "InfluencerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalRequests_RequestedAtUtc",
                table: "InfluencerWithdrawalRequests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalRequests_ReviewedByUserId",
                table: "InfluencerWithdrawalRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalRequests_Status",
                table: "InfluencerWithdrawalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IngredientFoodPreferences_IngredientId_PreferenceCode",
                table: "IngredientFoodPreferences",
                columns: new[] { "IngredientId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_IsActive",
                table: "Ingredients",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId",
                table: "Ingredients",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId_Code",
                table: "Ingredients",
                columns: new[] { "RestaurantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ingredients_RestaurantId_IsCore",
                table: "Ingredients",
                columns: new[] { "RestaurantId", "IsCore" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CorrelationId",
                table: "JournalEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_PostedAtUtc",
                table: "JournalEntries",
                column: "PostedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_SourceReference",
                table: "JournalEntries",
                column: "SourceReference");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_Status",
                table: "JournalEntries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_AccountId",
                table: "JournalEntryLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_JournalEntryId",
                table: "JournalEntryLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_MealAllergens_AllergenCode",
                table: "MealAllergens",
                column: "AllergenCode");

            migrationBuilder.CreateIndex(
                name: "IX_MealAllergens_MealId",
                table: "MealAllergens",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealAllergens_MealId_AllergenCode",
                table: "MealAllergens",
                columns: new[] { "MealId", "AllergenCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_BarcodeValue",
                table: "MealBarcodes",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_GeneratedAtUtc",
                table: "MealBarcodes",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_OrderId",
                table: "MealBarcodes",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MealBarcodes_SubscriptionDayId",
                table: "MealBarcodes",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealChangeRequests_FromMealId",
                table: "MealChangeRequests",
                column: "FromMealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealChangeRequests_RequestedByUserId",
                table: "MealChangeRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MealChangeRequests_Status",
                table: "MealChangeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MealChangeRequests_SubscriptionDayId",
                table: "MealChangeRequests",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealChangeRequests_ToMealId",
                table: "MealChangeRequests",
                column: "ToMealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealFoodPreferences_MealId_PreferenceCode",
                table: "MealFoodPreferences",
                columns: new[] { "MealId", "PreferenceCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealImages_MealId",
                table: "MealImages",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealImages_MealId_DisplayOrder",
                table: "MealImages",
                columns: new[] { "MealId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_IngredientId",
                table: "MealIngredients",
                column: "IngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_MealId",
                table: "MealIngredients",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_MealId_IngredientId",
                table: "MealIngredients",
                columns: new[] { "MealId", "IngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_InvoiceNumber",
                table: "MealInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_RestaurantId",
                table: "MealInvoices",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_RestaurantId_ServiceDate",
                table: "MealInvoices",
                columns: new[] { "RestaurantId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MealInvoices_ServiceDate",
                table: "MealInvoices",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabels_GeneratedAtUtc",
                table: "MealLabels",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MealLabels_SubscriptionDayId",
                table: "MealLabels",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPayables_OrderId",
                table: "MealPayables",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPayables_RecognizedAtUtc",
                table: "MealPayables",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MealPayables_RestaurantId",
                table: "MealPayables",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_MealPayables_RestaurantId_SubscriptionDayId",
                table: "MealPayables",
                columns: new[] { "RestaurantId", "SubscriptionDayId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealPayables_SubscriptionDayId",
                table: "MealPayables",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_MealProgramAssignments_MealId_ProgramId",
                table: "MealProgramAssignments",
                columns: new[] { "MealId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealRatings_CustomerUserId",
                table: "MealRatings",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MealRatings_MealId",
                table: "MealRatings",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MealRatings_MealId_CustomerUserId_SubscriptionDayId",
                table: "MealRatings",
                columns: new[] { "MealId", "CustomerUserId", "SubscriptionDayId" });

            migrationBuilder.CreateIndex(
                name: "IX_MealRatings_Score",
                table: "MealRatings",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_MealRatings_SubscriptionDayId",
                table: "MealRatings",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_ApprovalStatus",
                table: "Meals",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_BundleId",
                table: "Meals",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_IsAvailableForNewOrders",
                table: "Meals",
                column: "IsAvailableForNewOrders");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_MenuId",
                table: "Meals",
                column: "MenuId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_ProgramId",
                table: "Meals",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_RestaurantId",
                table: "Meals",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_RestaurantId_Code",
                table: "Meals",
                columns: new[] { "RestaurantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MealId",
                table: "MenuDayMeals",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MenuDayId_ProgramId_MealId",
                table: "MenuDayMeals",
                columns: new[] { "MenuDayId", "ProgramId", "MealId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MenuDayId_ProgramId_Slot",
                table: "MenuDayMeals",
                columns: new[] { "MenuDayId", "ProgramId", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuDayMeals_MenuDayId_ProgramId_Slot_IsPrimary",
                table: "MenuDayMeals",
                columns: new[] { "MenuDayId", "ProgramId", "Slot", "IsPrimary" },
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_MenuDays_MenuPlanVersionId_DayNumber",
                table: "MenuDays",
                columns: new[] { "MenuPlanVersionId", "DayNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuPlanAudits_MenuPlanVersionId",
                table: "MenuPlanAudits",
                column: "MenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuPlanPrograms_MenuPlanId_ProgramId",
                table: "MenuPlanPrograms",
                columns: new[] { "MenuPlanId", "ProgramId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuPlanPrograms_ProgramId_EffectiveFrom_EffectiveTo",
                table: "MenuPlanPrograms",
                columns: new[] { "ProgramId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuPlans_RestaurantId_IsTemplate",
                table: "MenuPlans",
                columns: new[] { "RestaurantId", "IsTemplate" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuPlanVersions_MenuPlanId_VersionNumber",
                table: "MenuPlanVersions",
                columns: new[] { "MenuPlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Menus_IsActive",
                table: "Menus",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_ProgramId",
                table: "Menus",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_RestaurantId",
                table: "Menus",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_RestaurantId_NameEn",
                table: "Menus",
                columns: new[] { "RestaurantId", "NameEn" });

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_Category",
                table: "OperatingExpenses",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_IncurredOn",
                table: "OperatingExpenses",
                column: "IncurredOn");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingExpenses_Status",
                table: "OperatingExpenses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_DailyOrderId",
                table: "OrderHandovers",
                column: "DailyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_DriverProfileId",
                table: "OrderHandovers",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_HandedOverAtUtc",
                table: "OrderHandovers",
                column: "HandedOverAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_Status",
                table: "OrderHandovers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrderHandovers_TripId",
                table: "OrderHandovers",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Destination_Purpose_CreatedAtUtc",
                table: "OtpChallenges",
                columns: new[] { "Destination", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_UserId",
                table: "OtpChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_BundleId",
                table: "PackageContentVersions",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_IsActive",
                table: "PackageContentVersions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PackageContentVersions_ProgramId_VersionNumber",
                table: "PackageContentVersions",
                columns: new[] { "ProgramId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayConfigs_Code",
                table: "PaymentGatewayConfigs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayConfigs_IsActive",
                table: "PaymentGatewayConfigs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayConfigs_RuleVersionId",
                table: "PaymentGatewayConfigs",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayConfigs_Weight",
                table: "PaymentGatewayConfigs",
                column: "Weight");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_EventKey",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_InvoiceId",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_Gateway_PaymentId",
                table: "PaymentWebhookReceipts",
                columns: new[] { "Gateway", "PaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookReceipts_SubscriptionId",
                table: "PaymentWebhookReceipts",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Module",
                table: "Permissions",
                column: "Module");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_BarcodeValue",
                table: "PickupScans",
                column: "BarcodeValue");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_DriverProfileId",
                table: "PickupScans",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_OrderId",
                table: "PickupScans",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_ScannedAtUtc",
                table: "PickupScans",
                column: "ScannedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_SubscriptionDayId",
                table: "PickupScans",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_PickupScans_TripId",
                table: "PickupScans",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRuleVersions_IsActive",
                table: "PricingRuleVersions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRuleVersions_RuleVersionId",
                table: "PricingRuleVersions",
                column: "RuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_BundleId",
                table: "ProfitabilityAlerts",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_ClassificationRunId",
                table: "ProfitabilityAlerts",
                column: "ClassificationRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_ProgramId",
                table: "ProfitabilityAlerts",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_ResolvedAtUtc",
                table: "ProfitabilityAlerts",
                column: "ResolvedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_ResolvedByUserId",
                table: "ProfitabilityAlerts",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_RestaurantId",
                table: "ProfitabilityAlerts",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_RestaurantId_ProgramId_BundleId",
                table: "ProfitabilityAlerts",
                columns: new[] { "RestaurantId", "ProgramId", "BundleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfitabilityAlerts_Status",
                table: "ProfitabilityAlerts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramMealSlotRequirements_ProgramId_Slot",
                table: "ProgramMealSlotRequirements",
                columns: new[] { "ProgramId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramNutritionStandards_ProgramId_IsActive",
                table: "ProgramNutritionStandards",
                columns: new[] { "ProgramId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgramNutritionStandards_ProgramId_MealCategory_StandardVe~",
                table: "ProgramNutritionStandards",
                columns: new[] { "ProgramId", "MealCategory", "StandardVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Programs_AvailableForFamily",
                table: "Programs",
                column: "AvailableForFamily");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_Code",
                table: "Programs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Programs_IsActive",
                table: "Programs",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_ShortCode",
                table: "Programs",
                column: "ShortCode");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_SortOrder",
                table: "Programs",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Programs_Status",
                table: "Programs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_Code",
                table: "PromoCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_IsActive",
                table: "PromoCodes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_StartsAtUtc_EndsAtUtc",
                table: "PromoCodes",
                columns: new[] { "StartsAtUtc", "EndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_Code",
                table: "ReferralCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_IsActive",
                table: "ReferralCodes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCodes_OwnerUserId",
                table: "ReferralCodes",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ExpiresAtUtc",
                table: "RefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefundCalculations_CalculatedAtUtc",
                table: "RefundCalculations",
                column: "CalculatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RefundCalculations_CancellationId",
                table: "RefundCalculations",
                column: "CancellationId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId_Code",
                table: "Regions",
                columns: new[] { "CountryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_CountryId_ParentRegionId_IsActive",
                table: "Regions",
                columns: new[] { "CountryId", "ParentRegionId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Regions_IsActive",
                table: "Regions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_ParentRegionId",
                table: "Regions",
                column: "ParentRegionId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityDecisions_ComplaintId",
                table: "ResponsibilityDecisions",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityDecisions_DecidedAtUtc",
                table: "ResponsibilityDecisions",
                column: "DecidedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityDecisions_DecidedByUserId",
                table: "ResponsibilityDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsibilityDecisions_Party",
                table: "ResponsibilityDecisions",
                column: "Party");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantAccountStatements_GeneratedAtUtc",
                table: "RestaurantAccountStatements",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantAccountStatements_RestaurantId",
                table: "RestaurantAccountStatements",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantAccountStatements_RestaurantId_PeriodYear_PeriodM~",
                table: "RestaurantAccountStatements",
                columns: new[] { "RestaurantId", "PeriodYear", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBoxDeductions_ComplaintId",
                table: "RestaurantBoxDeductions",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBoxDeductions_RestaurantId",
                table: "RestaurantBoxDeductions",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBoxDeductions_Status",
                table: "RestaurantBoxDeductions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBundlePricings_BundleId",
                table: "RestaurantBundlePricings",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBundlePricings_LockStatus",
                table: "RestaurantBundlePricings",
                column: "LockStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBundlePricings_PreviousPricingId",
                table: "RestaurantBundlePricings",
                column: "PreviousPricingId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBundlePricings_ProgramId",
                table: "RestaurantBundlePricings",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantBundlePricings_RestaurantId_ProgramId_BundleId_Ef~",
                table: "RestaurantBundlePricings",
                columns: new[] { "RestaurantId", "ProgramId", "BundleId", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantClassifications_BundleId",
                table: "RestaurantClassifications",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantClassifications_ClassificationRunId_RestaurantId",
                table: "RestaurantClassifications",
                columns: new[] { "ClassificationRunId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantClassifications_ProgramId_BundleId_Tier",
                table: "RestaurantClassifications",
                columns: new[] { "ProgramId", "BundleId", "Tier" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantClassifications_RestaurantId",
                table: "RestaurantClassifications",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantCommissionRecords_RecognizedAtUtc",
                table: "RestaurantCommissionRecords",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantCommissionRecords_RestaurantId",
                table: "RestaurantCommissionRecords",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantCommissionRecords_SubscriptionDayId",
                table: "RestaurantCommissionRecords",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantComplaintResponses_ComplaintId",
                table: "RestaurantComplaintResponses",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantComplaintResponses_RespondedAtUtc",
                table: "RestaurantComplaintResponses",
                column: "RespondedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantComplaintResponses_RespondedByUserId",
                table: "RestaurantComplaintResponses",
                column: "RespondedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantComplaintResponses_RestaurantId",
                table: "RestaurantComplaintResponses",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantConfirmations_ConfirmedByUserId",
                table: "RestaurantConfirmations",
                column: "ConfirmedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantConfirmations_DailyOrderId",
                table: "RestaurantConfirmations",
                column: "DailyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantConfirmations_RespondedAtUtc",
                table: "RestaurantConfirmations",
                column: "RespondedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantConfirmations_Status",
                table: "RestaurantConfirmations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantContractTerminations_BundleId",
                table: "RestaurantContractTerminations",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantContractTerminations_ProgramId",
                table: "RestaurantContractTerminations",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantContractTerminations_RequestedAtUtc",
                table: "RestaurantContractTerminations",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantContractTerminations_RestaurantId",
                table: "RestaurantContractTerminations",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantContractTerminations_Status",
                table: "RestaurantContractTerminations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_OrderId",
                table: "RestaurantDailyOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuDayId",
                table: "RestaurantDailyOrders",
                column: "ResolvedMenuDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ResolvedMenuPlanVersionId",
                table: "RestaurantDailyOrders",
                column: "ResolvedMenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ResolvedProgramId",
                table: "RestaurantDailyOrders",
                column: "ResolvedProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_RestaurantId",
                table: "RestaurantDailyOrders",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_RestaurantId_ServiceDate",
                table: "RestaurantDailyOrders",
                columns: new[] { "RestaurantId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_RestaurantId_SubscriptionDayId",
                table: "RestaurantDailyOrders",
                columns: new[] { "RestaurantId", "SubscriptionDayId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ScheduleMode",
                table: "RestaurantDailyOrders",
                column: "ScheduleMode");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_ServiceDate",
                table: "RestaurantDailyOrders",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_Status",
                table: "RestaurantDailyOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDailyOrders_SubscriptionDayId",
                table: "RestaurantDailyOrders",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDriverLinks_DriverProfileId",
                table: "RestaurantDriverLinks",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDriverLinks_IsActive",
                table: "RestaurantDriverLinks",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDriverLinks_RestaurantId",
                table: "RestaurantDriverLinks",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantDriverLinks_RestaurantId_DriverProfileId",
                table: "RestaurantDriverLinks",
                columns: new[] { "RestaurantId", "DriverProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMonthlyInvoices_InvoiceNumber",
                table: "RestaurantMonthlyInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMonthlyInvoices_IssuedAtUtc",
                table: "RestaurantMonthlyInvoices",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMonthlyInvoices_RestaurantId",
                table: "RestaurantMonthlyInvoices",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMonthlyInvoices_RestaurantId_PeriodYear_PeriodMon~",
                table: "RestaurantMonthlyInvoices",
                columns: new[] { "RestaurantId", "PeriodYear", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantMonthlyInvoices_Status",
                table: "RestaurantMonthlyInvoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantNotifications_UserId_IsRead_SentAtUtc",
                table: "RestaurantNotifications",
                columns: new[] { "UserId", "IsRead", "SentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingDayExceptions_RestaurantId",
                table: "RestaurantOperatingDayExceptions",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingDayExceptions_RestaurantId_Date",
                table: "RestaurantOperatingDayExceptions",
                columns: new[] { "RestaurantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingSchedules_RestaurantId",
                table: "RestaurantOperatingSchedules",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantOperatingSchedules_RestaurantId_DayOfWeek",
                table: "RestaurantOperatingSchedules",
                columns: new[] { "RestaurantId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPartialPayments_MonthlyInvoiceId",
                table: "RestaurantPartialPayments",
                column: "MonthlyInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPartialPayments_PaidAtUtc",
                table: "RestaurantPartialPayments",
                column: "PaidAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPartialPayments_Status",
                table: "RestaurantPartialPayments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayableLedgers_RecognizedAtUtc",
                table: "RestaurantPayableLedgers",
                column: "RecognizedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayableLedgers_RestaurantId",
                table: "RestaurantPayableLedgers",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayableLedgers_RestaurantId_RecognizedAtUtc",
                table: "RestaurantPayableLedgers",
                columns: new[] { "RestaurantId", "RecognizedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayableLedgers_SubscriptionDayId",
                table: "RestaurantPayableLedgers",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayouts_PaidAtUtc",
                table: "RestaurantPayouts",
                column: "PaidAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayouts_RestaurantId",
                table: "RestaurantPayouts",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayouts_RestaurantId_PeriodYear_PeriodMonth",
                table: "RestaurantPayouts",
                columns: new[] { "RestaurantId", "PeriodYear", "PeriodMonth" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantPayouts_Status",
                table: "RestaurantPayouts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProfiles_IsOperationallyActive",
                table: "RestaurantProfiles",
                column: "IsOperationallyActive");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProfiles_UserId",
                table: "RestaurantProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProfiles_VerificationStatus",
                table: "RestaurantProfiles",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProgramBundleOffers_BundleId",
                table: "RestaurantProgramBundleOffers",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProgramBundleOffers_ProgramId",
                table: "RestaurantProgramBundleOffers",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProgramBundleOffers_RestaurantId_IsActive",
                table: "RestaurantProgramBundleOffers",
                columns: new[] { "RestaurantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantProgramBundleOffers_RestaurantId_ProgramId_Bundle~",
                table: "RestaurantProgramBundleOffers",
                columns: new[] { "RestaurantId", "ProgramId", "BundleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRatings_CustomerUserId",
                table: "RestaurantRatings",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRatings_RestaurantId",
                table: "RestaurantRatings",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRatings_RestaurantId_CustomerUserId_SubscriptionD~",
                table: "RestaurantRatings",
                columns: new[] { "RestaurantId", "CustomerUserId", "SubscriptionDayId" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRatings_Score",
                table: "RestaurantRatings",
                column: "Score");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRatings_SubscriptionDayId",
                table: "RestaurantRatings",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrationFieldReviews_RegistrationId_FieldKey",
                table: "RestaurantRegistrationFieldReviews",
                columns: new[] { "RegistrationId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_CommercialRegistrationNumber",
                table: "RestaurantRegistrations",
                column: "CommercialRegistrationNumber");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_RestaurantCode",
                table: "RestaurantRegistrations",
                column: "RestaurantCode",
                unique: true,
                filter: "\"RestaurantCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_Status",
                table: "RestaurantRegistrations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_SubmittedByUserId",
                table: "RestaurantRegistrations",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewHistories_ReviewItemId_CreatedAtUtc",
                table: "RestaurantReviewHistories",
                columns: new[] { "ReviewItemId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewItems_RegistrationId_SubjectType_SubjectId_~",
                table: "RestaurantReviewItems",
                columns: new[] { "RegistrationId", "SubjectType", "SubjectId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantReviewItems_RestaurantId_Section_Status",
                table: "RestaurantReviewItems",
                columns: new[] { "RestaurantId", "Section", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantServiceRegions_IsActive",
                table: "RestaurantServiceRegions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantServiceRegions_RegionId",
                table: "RestaurantServiceRegions",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantServiceRegions_RestaurantId",
                table: "RestaurantServiceRegions",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantServiceRegions_RestaurantId_RegionId",
                table: "RestaurantServiceRegions",
                columns: new[] { "RestaurantId", "RegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantSettingsChangeRequests_RestaurantId_Status_Create~",
                table: "RestaurantSettingsChangeRequests",
                columns: new[] { "RestaurantId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_PreviousVersionId",
                table: "RuleVersions",
                column: "PreviousVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_RuleKey_ApprovalStatus_EffectiveFromUtc",
                table: "RuleVersions",
                columns: new[] { "RuleKey", "ApprovalStatus", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_RuleKey_VersionCode",
                table: "RuleVersions",
                columns: new[] { "RuleKey", "VersionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleVersions_ScopeEntityId",
                table: "RuleVersions",
                column: "ScopeEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_CustomerUserId",
                table: "SafeContactLogs",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_DriverProfileId",
                table: "SafeContactLogs",
                column: "DriverProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_OccurredAtUtc",
                table: "SafeContactLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_OrderId",
                table: "SafeContactLogs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_SubscriptionDayId",
                table: "SafeContactLogs",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeContactLogs_Within3Km",
                table: "SafeContactLogs",
                column: "Within3Km");

            migrationBuilder.CreateIndex(
                name: "IX_SecondDeliveryAttempts_OrderId",
                table: "SecondDeliveryAttempts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondDeliveryAttempts_OrderId_AttemptNumber",
                table: "SecondDeliveryAttempts",
                columns: new[] { "OrderId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SecondDeliveryAttempts_ScheduledAtUtc",
                table: "SecondDeliveryAttempts",
                column: "ScheduledAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SecondDeliveryAttempts_SubscriptionDayId",
                table: "SecondDeliveryAttempts",
                column: "SubscriptionDayId");

            migrationBuilder.CreateIndex(
                name: "IX_SecondDeliveryAttempts_SubscriptionDayId_AttemptNumber",
                table: "SecondDeliveryAttempts",
                columns: new[] { "SubscriptionDayId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCalendars_GeneratedAtUtc",
                table: "SubscriptionCalendars",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCalendars_SubscriptionId",
                table: "SubscriptionCalendars",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCancellations_CustomerUserId",
                table: "SubscriptionCancellations",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCancellations_RequestedAtUtc",
                table: "SubscriptionCancellations",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCancellations_Status",
                table: "SubscriptionCancellations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionCancellations_SubscriptionId",
                table: "SubscriptionCancellations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_MenuPlanVersionId",
                table: "SubscriptionDayMealSnapshots",
                column: "MenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_SubscriptionDayId",
                table: "SubscriptionDayMealSnapshots",
                column: "SubscriptionDayId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDayMealSnapshots_SubscriptionId_ServiceDate",
                table: "SubscriptionDayMealSnapshots",
                columns: new[] { "SubscriptionId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_CalendarId",
                table: "SubscriptionDays",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_LockAtUtc",
                table: "SubscriptionDays",
                column: "LockAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_ResolvedMenuPlanVersionId",
                table: "SubscriptionDays",
                column: "ResolvedMenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_RestaurantId",
                table: "SubscriptionDays",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_ServiceDate",
                table: "SubscriptionDays",
                column: "ServiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_Status",
                table: "SubscriptionDays",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_SubscriptionId",
                table: "SubscriptionDays",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDays_SubscriptionId_ServiceDate",
                table: "SubscriptionDays",
                columns: new[] { "SubscriptionId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezeRequests_Status",
                table: "SubscriptionFreezeRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezeRequests_SubscriptionId",
                table: "SubscriptionFreezeRequests",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezes_RequestedAtUtc",
                table: "SubscriptionFreezes",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezes_Status",
                table: "SubscriptionFreezes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezes_SubscriptionId",
                table: "SubscriptionFreezes",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionFreezes_SubscriptionId_StartsOn_EndsOn",
                table: "SubscriptionFreezes",
                columns: new[] { "SubscriptionId", "StartsOn", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_CorrelationId",
                table: "SubscriptionOperations",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_SubscriptionId",
                table: "SubscriptionOperations",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOperations_TimestampUtc",
                table: "SubscriptionOperations",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_CustomerUserId",
                table: "SubscriptionPayments",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_GatewayCode",
                table: "SubscriptionPayments",
                column: "GatewayCode");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_GatewayTransactionId",
                table: "SubscriptionPayments",
                column: "GatewayTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_PaidAtUtc",
                table: "SubscriptionPayments",
                column: "PaidAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_Status",
                table: "SubscriptionPayments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_SubscriptionId",
                table: "SubscriptionPayments",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_NewSubscriptionId",
                table: "SubscriptionRenewals",
                column: "NewSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_PreviousSubscriptionId",
                table: "SubscriptionRenewals",
                column: "PreviousSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_RequestedAtUtc",
                table: "SubscriptionRenewals",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRenewals_Status",
                table: "SubscriptionRenewals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_ContentHash",
                table: "SubscriptionRestaurantDaySnapshots",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_MenuPlanVersionId",
                table: "SubscriptionRestaurantDaySnapshots",
                column: "MenuPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_RestaurantSnapshotId_Ser~",
                table: "SubscriptionRestaurantDaySnapshots",
                columns: new[] { "RestaurantSnapshotId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantDaySnapshots_SubscriptionId_Restauran~",
                table: "SubscriptionRestaurantDaySnapshots",
                columns: new[] { "SubscriptionId", "RestaurantId", "ServiceDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantPoolSnapshots_EligibilityCheckedAtUtc",
                table: "SubscriptionRestaurantPoolSnapshots",
                column: "EligibilityCheckedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantPoolSnapshots_SubscriptionId",
                table: "SubscriptionRestaurantPoolSnapshots",
                column: "SubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_PoolSnapshotId_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                columns: new[] { "PoolSnapshotId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionRestaurantSnapshots_SubscriptionId_RestaurantId",
                table: "SubscriptionRestaurantSnapshots",
                columns: new[] { "SubscriptionId", "RestaurantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_BundleId",
                table: "Subscriptions",
                column: "BundleId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CampaignId",
                table: "Subscriptions",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CorrelationId",
                table: "Subscriptions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CustomerUserId",
                table: "Subscriptions",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_FamilyGroupId",
                table: "Subscriptions",
                column: "FamilyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_FamilyMemberId",
                table: "Subscriptions",
                column: "FamilyMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PackageContentVersionId",
                table: "Subscriptions",
                column: "PackageContentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentId",
                table: "Subscriptions",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentInvoiceId",
                table: "Subscriptions",
                column: "PaymentInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PricingRuleVersionId",
                table: "Subscriptions",
                column: "PricingRuleVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_ProgramId_BundleId_SelectedTier",
                table: "Subscriptions",
                columns: new[] { "ProgramId", "BundleId", "SelectedTier" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PromoCodeId",
                table: "Subscriptions",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_StartDate",
                table: "Subscriptions",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Status",
                table: "Subscriptions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshotMigrationIssues_ResolvedAtUtc_RecordedA~",
                table: "SubscriptionSnapshotMigrationIssues",
                columns: new[] { "ResolvedAtUtc", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshotMigrationIssues_SubscriptionId",
                table: "SubscriptionSnapshotMigrationIssues",
                column: "SubscriptionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshots_CapturedAtUtc",
                table: "SubscriptionSnapshots",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionSnapshots_SubscriptionId",
                table: "SubscriptionSnapshots",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierUpgrades_Status",
                table: "SubscriptionTierUpgrades",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionTierUpgrades_SubscriptionId",
                table: "SubscriptionTierUpgrades",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedToUserId",
                table: "SupportTickets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_CustomerUserId",
                table: "SupportTickets",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Priority",
                table: "SupportTickets",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status",
                table: "SupportTickets",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_IsActive",
                table: "SystemSettings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRecords_Code",
                table: "TaxRecords",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRecords_IsActive",
                table: "TaxRecords",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_CountryId",
                table: "Tenants",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Domain",
                table: "Tenants",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_IsActive",
                table: "Tenants",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceipts_PayoutId",
                table: "TransferReceipts",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceipts_UploadedAtUtc",
                table: "TransferReceipts",
                column: "UploadedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TransferReceipts_UploadedByUserId",
                table: "TransferReceipts",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DispatcherUserId",
                table: "Trips",
                column: "DispatcherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_PlannedDate",
                table: "Trips",
                column: "PlannedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RestaurantId",
                table: "Trips",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RestaurantId_PlannedDate",
                table: "Trips",
                columns: new[] { "RestaurantId", "PlannedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_Status",
                table: "Trips",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Users_AccountStatus",
                table: "Users",
                column: "AccountStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RestaurantId",
                table: "Users",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserType",
                table: "Users",
                column: "UserType");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserScopes_AccessScopeId",
                table: "UserScopes",
                column: "AccessScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserScopes_UserId_AccessScopeId",
                table: "UserScopes",
                columns: new[] { "UserId", "AccessScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CorrelationId",
                table: "WalletTransactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CustomerUserId",
                table: "WalletTransactions",
                column: "CustomerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_OccurredAtUtc",
                table: "WalletTransactions",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_ReferenceType_ReferenceId",
                table: "WalletTransactions",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Type",
                table: "WalletTransactions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId",
                table: "WalletTransactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_WalletType_OccurredAtUtc",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "WalletType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletType",
                table: "WalletTransactions",
                column: "WalletType");

            migrationBuilder.CreateIndex(
                name: "IX_WeekdayMealAssignments_MealId",
                table: "WeekdayMealAssignments",
                column: "MealId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekdayMealAssignments_MenuPlanVersionId_DayOfWeek_Program~1",
                table: "WeekdayMealAssignments",
                columns: new[] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "Slot", "IsPrimary" },
                unique: true,
                filter: "\"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_WeekdayMealAssignments_MenuPlanVersionId_DayOfWeek_ProgramI~",
                table: "WeekdayMealAssignments",
                columns: new[] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "MealId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DiscountsAndReferralLogs_Subscriptions_SubscriptionId",
                table: "DiscountsAndReferralLogs",
                column: "SubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyMemberAccesses_FamilyMembers_FamilyMemberId",
                table: "FamilyMemberAccesses",
                column: "FamilyMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyMemberPermissions_FamilyMembers_FamilyMemberId",
                table: "FamilyMemberPermissions",
                column: "FamilyMemberId",
                principalTable: "FamilyMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_FamilyMembers_Subscriptions_MemberSubscriptionId",
                table: "FamilyMembers",
                column: "MemberSubscriptionId",
                principalTable: "Subscriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bundles_Programs_ProgramId",
                table: "Bundles");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageContentVersions_Programs_ProgramId",
                table: "PackageContentVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Programs_ProgramId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageContentVersions_Bundles_BundleId",
                table: "PackageContentVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Bundles_BundleId",
                table: "Subscriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_FamilyMembers_Subscriptions_MemberSubscriptionId",
                table: "FamilyMembers");

            migrationBuilder.DropTable(
                name: "AdminComplaintReviews");

            migrationBuilder.DropTable(
                name: "AdminInvitations");

            migrationBuilder.DropTable(
                name: "AdminNotifications");

            migrationBuilder.DropTable(
                name: "AllergenDefinitions");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "AuthLoginChallenges");

            migrationBuilder.DropTable(
                name: "BranchStaffAssignments");

            migrationBuilder.DropTable(
                name: "BusyStatuses");

            migrationBuilder.DropTable(
                name: "CampaignAccountingEntries");

            migrationBuilder.DropTable(
                name: "CampaignBundles");

            migrationBuilder.DropTable(
                name: "CampaignCapacityReviews");

            migrationBuilder.DropTable(
                name: "CampaignDiscountSplits");

            migrationBuilder.DropTable(
                name: "CampaignPrograms");

            migrationBuilder.DropTable(
                name: "CampaignRestaurantApprovals");

            migrationBuilder.DropTable(
                name: "CampaignRestaurantCapacities");

            migrationBuilder.DropTable(
                name: "CampaignSubscriberCaps");

            migrationBuilder.DropTable(
                name: "CancellationFeeRecords");

            migrationBuilder.DropTable(
                name: "CapacityHolds");

            migrationBuilder.DropTable(
                name: "ComplaintClassifications");

            migrationBuilder.DropTable(
                name: "ComplaintDeductionRecords");

            migrationBuilder.DropTable(
                name: "ComplaintEvidencePhotos");

            migrationBuilder.DropTable(
                name: "CustomerAddresses");

            migrationBuilder.DropTable(
                name: "CustomerAllergyPreferences");

            migrationBuilder.DropTable(
                name: "CustomerBankDetails");

            migrationBuilder.DropTable(
                name: "CustomerCashWallets");

            migrationBuilder.DropTable(
                name: "CustomerCompensations");

            migrationBuilder.DropTable(
                name: "CustomerDietaryPreferences");

            migrationBuilder.DropTable(
                name: "CustomerProfiles");

            migrationBuilder.DropTable(
                name: "CustomerPromotionalWallets");

            migrationBuilder.DropTable(
                name: "CustomerWithdrawalRequests");

            migrationBuilder.DropTable(
                name: "DailyCapacities");

            migrationBuilder.DropTable(
                name: "DailyMealSelections");

            migrationBuilder.DropTable(
                name: "DailyRestaurantSelections");

            migrationBuilder.DropTable(
                name: "DailyRevenueRecognitions");

            migrationBuilder.DropTable(
                name: "DeferredRevenues");

            migrationBuilder.DropTable(
                name: "DeliveryConfirmationScans");

            migrationBuilder.DropTable(
                name: "DeliveryHolds");

            migrationBuilder.DropTable(
                name: "DeliveryTrackingPoints");

            migrationBuilder.DropTable(
                name: "DiscountsAndReferralLogs");

            migrationBuilder.DropTable(
                name: "DiversificationLimitSnapshots");

            migrationBuilder.DropTable(
                name: "DocumentVerifications");

            migrationBuilder.DropTable(
                name: "DriverAssignments");

            migrationBuilder.DropTable(
                name: "DriverPerformanceStats");

            migrationBuilder.DropTable(
                name: "DriverProfiles");

            migrationBuilder.DropTable(
                name: "DriverRatings");

            migrationBuilder.DropTable(
                name: "DriverRegistrations");

            migrationBuilder.DropTable(
                name: "FamilyMemberAccesses");

            migrationBuilder.DropTable(
                name: "FamilyMemberPermissions");

            migrationBuilder.DropTable(
                name: "FamilySubscriptions");

            migrationBuilder.DropTable(
                name: "FinancialAuditTrails");

            migrationBuilder.DropTable(
                name: "FinancialSettings");

            migrationBuilder.DropTable(
                name: "FinancialSettlements");

            migrationBuilder.DropTable(
                name: "FoodPreferenceDefinitions");

            migrationBuilder.DropTable(
                name: "FreeDayCompensations");

            migrationBuilder.DropTable(
                name: "FreezeLedgerEntries");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "InfluencerCancellationImpacts");

            migrationBuilder.DropTable(
                name: "InfluencerCustomerLinks");

            migrationBuilder.DropTable(
                name: "InfluencerEligibilityRules");

            migrationBuilder.DropTable(
                name: "InfluencerPayableLedgers");

            migrationBuilder.DropTable(
                name: "InfluencerPayoutHistories");

            migrationBuilder.DropTable(
                name: "InfluencerProfiles");

            migrationBuilder.DropTable(
                name: "InfluencerWithdrawalApprovals");

            migrationBuilder.DropTable(
                name: "IngredientFoodPreferences");

            migrationBuilder.DropTable(
                name: "JournalEntryLines");

            migrationBuilder.DropTable(
                name: "MealAllergens");

            migrationBuilder.DropTable(
                name: "MealBarcodes");

            migrationBuilder.DropTable(
                name: "MealChangeRequests");

            migrationBuilder.DropTable(
                name: "MealFoodPreferences");

            migrationBuilder.DropTable(
                name: "MealImages");

            migrationBuilder.DropTable(
                name: "MealIngredients");

            migrationBuilder.DropTable(
                name: "MealInvoices");

            migrationBuilder.DropTable(
                name: "MealLabels");

            migrationBuilder.DropTable(
                name: "MealPayables");

            migrationBuilder.DropTable(
                name: "MealProgramAssignments");

            migrationBuilder.DropTable(
                name: "MealRatings");

            migrationBuilder.DropTable(
                name: "MenuDayMeals");

            migrationBuilder.DropTable(
                name: "MenuPlanAudits");

            migrationBuilder.DropTable(
                name: "MenuPlanPrograms");

            migrationBuilder.DropTable(
                name: "MyFatoorahGatewayConfigs");

            migrationBuilder.DropTable(
                name: "OperatingExpenses");

            migrationBuilder.DropTable(
                name: "OrderHandovers");

            migrationBuilder.DropTable(
                name: "OtpChallenges");

            migrationBuilder.DropTable(
                name: "PaymentGatewayConfigs");

            migrationBuilder.DropTable(
                name: "PaymentWebhookReceipts");

            migrationBuilder.DropTable(
                name: "PickupScans");

            migrationBuilder.DropTable(
                name: "ProfitabilityAlerts");

            migrationBuilder.DropTable(
                name: "ProgramMealSlotRequirements");

            migrationBuilder.DropTable(
                name: "ProgramNutritionStandards");

            migrationBuilder.DropTable(
                name: "ReferralCodes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RefundCalculations");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "ResponsibilityDecisions");

            migrationBuilder.DropTable(
                name: "RestaurantAccountStatements");

            migrationBuilder.DropTable(
                name: "RestaurantBoxDeductions");

            migrationBuilder.DropTable(
                name: "RestaurantBranches");

            migrationBuilder.DropTable(
                name: "RestaurantBundlePricings");

            migrationBuilder.DropTable(
                name: "RestaurantClassifications");

            migrationBuilder.DropTable(
                name: "RestaurantCommissionRecords");

            migrationBuilder.DropTable(
                name: "RestaurantComplaintResponses");

            migrationBuilder.DropTable(
                name: "RestaurantConfirmations");

            migrationBuilder.DropTable(
                name: "RestaurantContractTerminations");

            migrationBuilder.DropTable(
                name: "RestaurantCustomRoles");

            migrationBuilder.DropTable(
                name: "RestaurantDriverLinks");

            migrationBuilder.DropTable(
                name: "RestaurantNotifications");

            migrationBuilder.DropTable(
                name: "RestaurantOperatingDayExceptions");

            migrationBuilder.DropTable(
                name: "RestaurantOperatingSchedules");

            migrationBuilder.DropTable(
                name: "RestaurantPartialPayments");

            migrationBuilder.DropTable(
                name: "RestaurantPayableLedgers");

            migrationBuilder.DropTable(
                name: "RestaurantProfiles");

            migrationBuilder.DropTable(
                name: "RestaurantProgramBundleOffers");

            migrationBuilder.DropTable(
                name: "RestaurantRatings");

            migrationBuilder.DropTable(
                name: "RestaurantRegistrationFieldReviews");

            migrationBuilder.DropTable(
                name: "RestaurantReviewHistories");

            migrationBuilder.DropTable(
                name: "RestaurantReviewItems");

            migrationBuilder.DropTable(
                name: "RestaurantRolePermissions");

            migrationBuilder.DropTable(
                name: "RestaurantServiceRegions");

            migrationBuilder.DropTable(
                name: "RestaurantSettingsChangeRequests");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "SafeContactLogs");

            migrationBuilder.DropTable(
                name: "SecondDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "StaffInvitations");

            migrationBuilder.DropTable(
                name: "SubscriptionDayMealSnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionFreezeRequests");

            migrationBuilder.DropTable(
                name: "SubscriptionOperations");

            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "SubscriptionRenewals");

            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantDaySnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionSnapshotMigrationIssues");

            migrationBuilder.DropTable(
                name: "SubscriptionSnapshots");

            migrationBuilder.DropTable(
                name: "SubscriptionTierUpgrades");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TaxRecords");

            migrationBuilder.DropTable(
                name: "TransferReceipts");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserScopes");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "WeekdayMealAssignments");

            migrationBuilder.DropTable(
                name: "CampaignRestaurantEnrollments");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "SubscriptionFreezes");

            migrationBuilder.DropTable(
                name: "InfluencerCommissionCalculations");

            migrationBuilder.DropTable(
                name: "InfluencerWithdrawalRequests");

            migrationBuilder.DropTable(
                name: "ChartOfAccounts");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "SubscriptionDays");

            migrationBuilder.DropTable(
                name: "Ingredients");

            migrationBuilder.DropTable(
                name: "MenuDays");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropTable(
                name: "SubscriptionCancellations");

            migrationBuilder.DropTable(
                name: "ClassificationRuns");

            migrationBuilder.DropTable(
                name: "Complaints");

            migrationBuilder.DropTable(
                name: "RestaurantDailyOrders");

            migrationBuilder.DropTable(
                name: "RestaurantMonthlyInvoices");

            migrationBuilder.DropTable(
                name: "RestaurantRegistrations");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantSnapshots");

            migrationBuilder.DropTable(
                name: "RestaurantPayouts");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "AccessScopes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Meals");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "InfluencerCodes");

            migrationBuilder.DropTable(
                name: "SubscriptionCalendars");

            migrationBuilder.DropTable(
                name: "MenuPlanVersions");

            migrationBuilder.DropTable(
                name: "SubscriptionRestaurantPoolSnapshots");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Menus");

            migrationBuilder.DropTable(
                name: "MenuPlans");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Programs");

            migrationBuilder.DropTable(
                name: "Bundles");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "FamilyMembers");

            migrationBuilder.DropTable(
                name: "PackageContentVersions");

            migrationBuilder.DropTable(
                name: "PricingRuleVersions");

            migrationBuilder.DropTable(
                name: "PromoCodes");

            migrationBuilder.DropTable(
                name: "FamilyGroups");

            migrationBuilder.DropTable(
                name: "RuleVersions");
        }
    }
}
