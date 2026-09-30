using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260930235901_AddSubscriptionAutoRenewalConsent")]
public sealed class AddSubscriptionAutoRenewalConsent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("AutoRenewConsentAtUtc", "Subscriptions", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>("AutoRenewConsentWithdrawnAtUtc", "Subscriptions", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("AutoRenewConsentVersion", "Subscriptions", "character varying(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<decimal>("AutoRenewConsentAmount", "Subscriptions", "numeric(18,3)", precision: 18, scale: 3, nullable: true);
        migrationBuilder.AddColumn<string>("AutoRenewConsentCurrency", "Subscriptions", "character varying(3)", maxLength: 3, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("AutoRenewConsentAtUtc", "Subscriptions");
        migrationBuilder.DropColumn("AutoRenewConsentWithdrawnAtUtc", "Subscriptions");
        migrationBuilder.DropColumn("AutoRenewConsentVersion", "Subscriptions");
        migrationBuilder.DropColumn("AutoRenewConsentAmount", "Subscriptions");
        migrationBuilder.DropColumn("AutoRenewConsentCurrency", "Subscriptions");
    }
}
