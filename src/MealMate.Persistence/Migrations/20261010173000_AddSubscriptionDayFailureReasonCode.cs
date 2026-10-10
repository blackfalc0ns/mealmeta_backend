using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

public partial class AddSubscriptionDayFailureReasonCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "FailureReasonCode", table: "SubscriptionDays",
            type: "character varying(120)", maxLength: 120, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "FailureReasonCode", table: "SubscriptionDays");
}
