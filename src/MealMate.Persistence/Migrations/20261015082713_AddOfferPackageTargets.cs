using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferPackageTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OfferPackageTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    AllowedDurationDays = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferPackageTargets", x => x.Id);
                    table.ForeignKey("FK_OfferPackageTargets_Bundles_BundleId", x => x.BundleId, "Bundles", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_OfferPackageTargets_Offers_OfferId", x => x.OfferId, "Offers", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_OfferPackageTargets_Programs_ProgramId", x => x.ProgramId, "Programs", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OfferPackageTargets_OfferId_SubscriptionType_ProgramId_BundleId_Tier",
                table: "OfferPackageTargets",
                columns: new[] { "OfferId", "SubscriptionType", "ProgramId", "BundleId", "Tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferPackageTargets_ProgramId_BundleId_Tier",
                table: "OfferPackageTargets",
                columns: new[] { "ProgramId", "BundleId", "Tier" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OfferPackageTargets");
        }
    }
}
