using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeRestaurantRegistrationDraftSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TradeNameEn",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "TradeNameAr",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "LegalNameEn",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "LegalNameAr",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ContactPhone",
                table: "RestaurantRegistrations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmail",
                table: "RestaurantRegistrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.Sql(
                """
                UPDATE "RestaurantRegistrations"
                SET "RestaurantCode" = 'RST-' || UPPER(REPLACE("Id"::text, '-', ''))
                WHERE "RestaurantCode" IS NULL OR BTRIM("RestaurantCode") = '';

                WITH duplicate_codes AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY BTRIM("RestaurantCode")
                               ORDER BY "Id") AS duplicate_rank
                    FROM "RestaurantRegistrations"
                    WHERE "RestaurantCode" IS NOT NULL
                      AND BTRIM("RestaurantCode") <> ''
                )
                UPDATE "RestaurantRegistrations" AS registrations
                SET "RestaurantCode" = 'RST-' || UPPER(REPLACE(registrations."Id"::text, '-', ''))
                FROM duplicate_codes
                WHERE registrations."Id" = duplicate_codes."Id"
                  AND duplicate_codes.duplicate_rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantRegistrations_RestaurantCode",
                table: "RestaurantRegistrations",
                column: "RestaurantCode",
                unique: true,
                filter: "\"RestaurantCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RestaurantRegistrations_RestaurantCode",
                table: "RestaurantRegistrations");

            migrationBuilder.AlterColumn<string>(
                name: "TradeNameEn",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TradeNameAr",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LegalNameEn",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LegalNameAr",
                table: "RestaurantRegistrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactPhone",
                table: "RestaurantRegistrations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactEmail",
                table: "RestaurantRegistrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
