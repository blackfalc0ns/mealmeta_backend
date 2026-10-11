using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddSubscriptionDayReplacementWindow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "ReplacementWindowOpensAtUtc", table: "SubscriptionDays",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReplacementWindowClosesAtUtc", table: "SubscriptionDays",
            type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReplacementWindowOpensAtUtc", table: "SubscriptionDays");
        migrationBuilder.DropColumn(name: "ReplacementWindowClosesAtUtc", table: "SubscriptionDays");
    }
}
