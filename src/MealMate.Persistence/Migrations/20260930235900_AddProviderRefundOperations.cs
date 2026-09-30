using MealMate.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260930235900_AddProviderRefundOperations")]
public sealed partial class AddProviderRefundOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RefundOperations", columns: table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false),
            OperationReference = table.Column<string>("character varying(100)", maxLength: 100, nullable: false),
            PaymentProvider = table.Column<string>("character varying(30)", maxLength: 30, nullable: false),
            PaymentId = table.Column<string>("character varying(150)", maxLength: 150, nullable: false),
            InvoiceId = table.Column<string>("character varying(100)", maxLength: 100, nullable: false),
            Amount = table.Column<decimal>("numeric(18,3)", nullable: false),
            Currency = table.Column<string>("character varying(3)", maxLength: 3, nullable: false),
            Reason = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: false),
            State = table.Column<int>("integer", nullable: false),
            ProviderRefundId = table.Column<string>("character varying(100)", maxLength: 100, nullable: true),
            ProviderRefundReference = table.Column<string>("character varying(150)", maxLength: 150, nullable: true),
            Detail = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", nullable: false),
            SubmittedAtUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            ReconciledAtUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            ConfirmedAtUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            LocalAppliedAtUtc = table.Column<DateTime>("timestamp with time zone", nullable: true),
            Version = table.Column<long>("bigint", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_RefundOperations", x => x.Id));
        migrationBuilder.CreateIndex("IX_RefundOperations_OperationReference", "RefundOperations", "OperationReference", unique: true);
        migrationBuilder.CreateIndex("IX_RefundOperations_ProviderRefundId", "RefundOperations", "ProviderRefundId", unique: true, filter: "\"ProviderRefundId\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_RefundOperations_PaymentProvider_PaymentId", "RefundOperations", new[] { "PaymentProvider", "PaymentId" });
        migrationBuilder.CreateIndex("IX_RefundOperations_State", "RefundOperations", "State");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("RefundOperations");
}
