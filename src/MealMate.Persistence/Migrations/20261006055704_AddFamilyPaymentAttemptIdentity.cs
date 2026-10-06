using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyPaymentAttemptIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GatewayPaymentUrl",
                table: "FamilyCheckoutPaymentAttempts",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "FamilyCheckoutPaymentAttempts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GatewayPaymentUrl",
                table: "FamilyCheckoutPaymentAttempts");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "FamilyCheckoutPaymentAttempts");
        }
    }
}
