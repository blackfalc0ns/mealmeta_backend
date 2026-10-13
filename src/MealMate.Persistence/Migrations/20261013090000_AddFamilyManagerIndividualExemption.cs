using System;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261013090000_AddFamilyManagerIndividualExemption")]
public sealed class AddFamilyManagerIndividualExemption : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ManagerIndividualSubscriptionId",
            table: "FamilyGroups",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ManagerIndividualSeatExempt",
            table: "FamilyGroups",
            type: "boolean",
            nullable: true);

        migrationBuilder.Sql("UPDATE \"FamilyGroups\" g SET \"ManagerIndividualSubscriptionId\" = g.\"ConvertedFromSubscriptionId\", \"ManagerIndividualSeatExempt\" = EXISTS (SELECT 1 FROM \"FamilyMembers\" m JOIN \"Subscriptions\" s ON s.\"Id\" = g.\"ConvertedFromSubscriptionId\" WHERE m.\"FamilyGroupId\" = g.\"Id\" AND m.\"Role\" = 1 AND m.\"MemberSubscriptionId\" = s.\"Id\" AND s.\"CustomerUserId\" = g.\"ManagerCustomerUserId\" AND s.\"FamilyGroupId\" = g.\"Id\" AND s.\"FamilyMemberId\" = m.\"Id\") WHERE g.\"ConvertedFromSubscriptionId\" IS NOT NULL;");
        migrationBuilder.Sql("UPDATE \"FamilyGroups\" SET \"ManagerIndividualSeatExempt\" = FALSE WHERE \"ManagerIndividualSeatExempt\" IS NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_FamilyGroups_ManagerIndividualSubscriptionId",
            table: "FamilyGroups",
            column: "ManagerIndividualSubscriptionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FamilyGroups_ManagerIndividualSubscriptionId",
            table: "FamilyGroups");

        migrationBuilder.DropColumn(
            name: "ManagerIndividualSeatExempt",
            table: "FamilyGroups");

        migrationBuilder.DropColumn(
            name: "ManagerIndividualSubscriptionId",
            table: "FamilyGroups");
    }
}
