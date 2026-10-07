using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceActiveGatewayWithPayzah : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MyFatoorahGatewayConfigs");
            // Preserve old financial history and deactivate only the obsolete accounting gateway.
            migrationBuilder.Sql("UPDATE \"PaymentGatewayConfigs\" SET \"IsActive\" = FALSE WHERE LOWER(\"Code\") = 'myfatoorah' OR LOWER(\"Name\") = 'myfatoorah';");
            migrationBuilder.Sql("UPDATE \"RefundOperations\" SET \"State\" = 5, \"Detail\" = 'Historical provider operation requires merchant review; no Payzah request was sent.', \"Version\" = \"Version\" + 1 WHERE LOWER(\"PaymentProvider\") = 'myfatoorah' AND \"State\" IN (0, 1, 2);");

            migrationBuilder.CreateTable(
                name: "PaymentGatewayAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    TrackId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedCurrency = table.Column<string>(type: "text", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_PaymentGatewayAttempts", x => x.Id);
                });
            migrationBuilder.CreateTable(
                name: "PayzahGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayName = table.Column<string>(type: "text", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: false),
                    EncryptedPrivateKey = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_PayzahGatewayConfigs", x => x.Id);
                });
            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayAttempts_Provider_IsSandbox_PaymentId",
                table: "PaymentGatewayAttempts",
                columns: new[] { "Provider", "IsSandbox", "PaymentId" },
                unique: true,
                filter: "\"PaymentId\" <> ''");
            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayAttempts_TrackId",
                table: "PaymentGatewayAttempts",
                column: "TrackId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentGatewayAttempts");
            migrationBuilder.DropTable(
                name: "PayzahGatewayConfigs");
            migrationBuilder.CreateTable(
                name: "MyFatoorahGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: false),
                    CountryCode = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultCurrency = table.Column<string>(type: "text", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncryptedApiToken = table.Column<string>(type: "text", nullable: false),
                    EncryptedWebhookSecret = table.Column<string>(type: "text", nullable: false),
                    GatewayName = table.Column<string>(type: "text", nullable: false),
                    IsApplePayEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsKnetEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    IsVisaMasterEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MaskedToken = table.Column<string>(type: "text", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyFatoorahGatewayConfigs", x => x.Id);
                });
        }
    }
}
