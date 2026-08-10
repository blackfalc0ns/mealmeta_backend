using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantProgramBundleOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantProgramBundleOffers");
        }
    }
}
