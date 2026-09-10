using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInfluencerPartnerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountHolderName",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawalMethod",
                table: "InfluencerWithdrawalRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccountHolderName",
                table: "InfluencerProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasCompletedSocialOnboarding",
                table: "InfluencerProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SocialLinksJson",
                table: "InfluencerProfiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfluencerWithdrawalRequests_ReferenceNumber",
                table: "InfluencerWithdrawalRequests",
                column: "ReferenceNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InfluencerWithdrawalRequests_ReferenceNumber",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "AccountHolderName",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "WithdrawalMethod",
                table: "InfluencerWithdrawalRequests");

            migrationBuilder.DropColumn(
                name: "AccountHolderName",
                table: "InfluencerProfiles");

            migrationBuilder.DropColumn(
                name: "HasCompletedSocialOnboarding",
                table: "InfluencerProfiles");

            migrationBuilder.DropColumn(
                name: "SocialLinksJson",
                table: "InfluencerProfiles");
        }
    }
}
