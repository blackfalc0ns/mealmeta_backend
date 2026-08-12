using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20260812113000_AddCustomerAddresses")]
public partial class AddCustomerAddresses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CustomerAddresses",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                AddressLine = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                RegionId = table.Column<Guid>(type: "uuid", nullable: true),
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
            constraints: table => table.PrimaryKey("PK_CustomerAddresses", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_CustomerAddresses_UserId_IsDefault",
            table: "CustomerAddresses",
            columns: new[] { "UserId", "IsDefault" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "CustomerAddresses");
}
