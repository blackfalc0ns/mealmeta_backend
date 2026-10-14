using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace MealMate.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261013074645_AddAdvertisingModule")]
public class AddAdvertisingModule : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable("AdCommercialPolicies", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> version = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 3;
			OperationBuilder<AddColumnOperation> currencyCode = table.Column<string>("character varying(3)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> minimumBid = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> bidStep = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 32;
			OperationBuilder<AddColumnOperation> tiePolicy = table.Column<string>("character varying(32)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> winnerPaymentDeadlineMinutes = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 48;
			OperationBuilder<AddColumnOperation> nextBidderPolicy = table.Column<string>("character varying(48)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 48;
			return new
			{
				Id = id,
				Version = version,
				CurrencyCode = currencyCode,
				MinimumBid = minimumBid,
				BidStep = bidStep,
				TiePolicy = tiePolicy,
				WinnerPaymentDeadlineMinutes = winnerPaymentDeadlineMinutes,
				NextBidderPolicy = nextBidderPolicy,
				StopSettlementRule = table.Column<string>("character varying(48)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ApprovedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ApprovedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdCommercialPolicies", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdFinancialLedgerEntries", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> adAuctionId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> restaurantId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 48;
			OperationBuilder<AddColumnOperation> source = table.Column<string>("character varying(48)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 32;
			OperationBuilder<AddColumnOperation> action = table.Column<string>("character varying(32)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> amount = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 3;
			OperationBuilder<AddColumnOperation> currencyCode = table.Column<string>("character varying(3)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 128;
			OperationBuilder<AddColumnOperation> providerTransactionId = table.Column<string>("character varying(128)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 128;
			return new
			{
				Id = id,
				AdAuctionId = adAuctionId,
				RestaurantId = restaurantId,
				Source = source,
				Action = action,
				Amount = amount,
				CurrencyCode = currencyCode,
				ProviderTransactionId = providerTransactionId,
				ExternalRefundIdentifier = table.Column<string>("character varying(128)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				OccurredAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdFinancialLedgerEntries", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdPaymentTransactions", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> adAuctionId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> restaurantId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 64;
			OperationBuilder<AddColumnOperation> paymentProvider = table.Column<string>("character varying(64)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 128;
			OperationBuilder<AddColumnOperation> providerTransactionId = table.Column<string>("character varying(128)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> amount = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 3;
			OperationBuilder<AddColumnOperation> currencyCode = table.Column<string>("character varying(3)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 32;
			return new
			{
				Id = id,
				AdAuctionId = adAuctionId,
				RestaurantId = restaurantId,
				PaymentProvider = paymentProvider,
				ProviderTransactionId = providerTransactionId,
				Amount = amount,
				CurrencyCode = currencyCode,
				Status = table.Column<string>("character varying(32)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				InitiatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CompletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdPaymentTransactions", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdPlacementEvents", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> placementId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 128;
			OperationBuilder<AddColumnOperation> eventId = table.Column<string>("character varying(128)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 16;
			return new
			{
				Id = id,
				PlacementId = placementId,
				EventId = eventId,
				EventType = table.Column<string>("character varying(16)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				OccurredAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdPlacementEvents", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdSpaces", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 64;
			OperationBuilder<AddColumnOperation> placementKey = table.Column<string>("character varying(64)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 200;
			OperationBuilder<AddColumnOperation> nameAr = table.Column<string>("character varying(200)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 200;
			return new
			{
				Id = id,
				PlacementKey = placementKey,
				NameAr = nameAr,
				NameEn = table.Column<string>("character varying(200)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				WidthPx = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				HeightPx = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				MarketIdsJson = table.Column<string>("text", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsActive = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdSpaces", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdvertisingSettings", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> auctionsEnabled = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 64;
			OperationBuilder<AddColumnOperation> businessTimezone = table.Column<string>("character varying(64)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> expectedVersion = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> lastChangedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> lastChangedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 500;
			return new
			{
				Id = id,
				AuctionsEnabled = auctionsEnabled,
				BusinessTimezone = businessTimezone,
				ExpectedVersion = expectedVersion,
				LastChangedByUserId = lastChangedByUserId,
				LastChangedAtUtc = lastChangedAtUtc,
				LastChangeReason = table.Column<string>("character varying(500)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdvertisingSettings", x => (object)x.Id);
		}, (string)null);
		migrationBuilder.CreateTable("AdAuctions", (ColumnsBuilder table) => new
		{
			Id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			AdSpaceId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			Status = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			BidsOpenAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			BidsCloseAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			DisplayStartsAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			DisplayEndsAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			PolicyId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			PolicyVersion = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			WinningRestaurantId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			WinningBidId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			WinningAmount = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			CurrencyCode = table.Column<string>("text", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			ClosedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
			DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdAuctions", x => (object)x.Id);
			table.ForeignKey("FK_AdAuctions_AdSpaces_AdSpaceId", x => (object)x.AdSpaceId, "AdSpaces", "Id", (string)null, (ReferentialAction)0, (ReferentialAction)1);
		}, (string)null);
		migrationBuilder.CreateTable("AdBids", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> adAuctionId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> restaurantId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> amount = table.Column<decimal>("numeric", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 3;
			OperationBuilder<AddColumnOperation> currencyCode = table.Column<string>("character varying(3)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 128;
			return new
			{
				Id = id,
				AdAuctionId = adAuctionId,
				RestaurantId = restaurantId,
				Amount = amount,
				CurrencyCode = currencyCode,
				IdempotencyKey = table.Column<string>("character varying(128)", (bool?)null, num, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsWinning = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				AcceptedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdBids", x => (object)x.Id);
			table.ForeignKey("FK_AdBids_AdAuctions_AdAuctionId", x => (object)x.AdAuctionId, "AdAuctions", "Id", (string)null, (ReferentialAction)0, (ReferentialAction)1);
		}, (string)null);
		migrationBuilder.CreateTable("AdPlacements", delegate(ColumnsBuilder table)
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> adAuctionId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> restaurantId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> adSpaceId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> status = table.Column<int>("integer", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> creativeStorageKey = table.Column<string>("text", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			int? num = 64;
			OperationBuilder<AddColumnOperation> targetType = table.Column<string>("character varying(64)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> targetId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> displayStartsAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> displayEndsAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> reviewedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> reviewedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 1000;
			OperationBuilder<AddColumnOperation> reviewNotes = table.Column<string>("character varying(1000)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			OperationBuilder<AddColumnOperation> stoppedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null);
			num = 500;
			return new
			{
				Id = id,
				AdAuctionId = adAuctionId,
				RestaurantId = restaurantId,
				AdSpaceId = adSpaceId,
				Status = status,
				CreativeStorageKey = creativeStorageKey,
				TargetType = targetType,
				TargetId = targetId,
				DisplayStartsAtUtc = displayStartsAtUtc,
				DisplayEndsAtUtc = displayEndsAtUtc,
				ReviewedByUserId = reviewedByUserId,
				ReviewedAtUtc = reviewedAtUtc,
				ReviewNotes = reviewNotes,
				StoppedAtUtc = stoppedAtUtc,
				StopReason = table.Column<string>("character varying(500)", (bool?)null, num, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				StoppedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				CreatedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				ModifiedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				IsDeleted = table.Column<bool>("boolean", (bool?)null, (int?)null, false, (string)null, false, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedAtUtc = table.Column<DateTime>("timestamp with time zone", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null),
				DeletedByUserId = table.Column<Guid>("uuid", (bool?)null, (int?)null, false, (string)null, true, (object)null, (string)null, (string)null, (bool?)null, (string)null, (string)null, (int?)null, (int?)null, (bool?)null)
			};
		}, (string)null, table =>
		{
			table.PrimaryKey("PK_AdPlacements", x => (object)x.Id);
			table.ForeignKey("FK_AdPlacements_AdAuctions_AdAuctionId", x => (object)x.AdAuctionId, "AdAuctions", "Id", (string)null, (ReferentialAction)0, (ReferentialAction)1);
		}, (string)null);
		migrationBuilder.CreateIndex("IX_AdAuctions_AdSpaceId", "AdAuctions", "AdSpaceId", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdAuctions_BidsCloseAtUtc", "AdAuctions", "BidsCloseAtUtc", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdAuctions_Status", "AdAuctions", "Status", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdBids_AdAuctionId_RestaurantId", "AdBids", new string[2] { "AdAuctionId", "RestaurantId" }, (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdBids_AdAuctionId_RestaurantId_IdempotencyKey", "AdBids", new string[3] { "AdAuctionId", "RestaurantId", "IdempotencyKey" }, (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdCommercialPolicies_Version", "AdCommercialPolicies", "Version", (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdFinancialLedgerEntries_AdAuctionId", "AdFinancialLedgerEntries", "AdAuctionId", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdFinancialLedgerEntries_Source_ProviderTransactionId_Action", "AdFinancialLedgerEntries", new string[3] { "Source", "ProviderTransactionId", "Action" }, (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPaymentTransactions_AdAuctionId", "AdPaymentTransactions", "AdAuctionId", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPaymentTransactions_PaymentProvider_ProviderTransactionId", "AdPaymentTransactions", new string[2] { "PaymentProvider", "ProviderTransactionId" }, (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacementEvents_OccurredAtUtc", "AdPlacementEvents", "OccurredAtUtc", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacementEvents_PlacementId_EventId", "AdPlacementEvents", new string[2] { "PlacementId", "EventId" }, (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacements_AdAuctionId", "AdPlacements", "AdAuctionId", (string)null, true, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacements_AdSpaceId_Status", "AdPlacements", new string[2] { "AdSpaceId", "Status" }, (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacements_RestaurantId", "AdPlacements", "RestaurantId", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdPlacements_Status", "AdPlacements", "Status", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdSpaces_IsActive", "AdSpaces", "IsActive", (string)null, false, (string)null, (bool[])null);
		migrationBuilder.CreateIndex("IX_AdSpaces_PlacementKey", "AdSpaces", "PlacementKey", (string)null, true, (string)null, (bool[])null);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable("AdBids", (string)null);
		migrationBuilder.DropTable("AdCommercialPolicies", (string)null);
		migrationBuilder.DropTable("AdFinancialLedgerEntries", (string)null);
		migrationBuilder.DropTable("AdPaymentTransactions", (string)null);
		migrationBuilder.DropTable("AdPlacementEvents", (string)null);
		migrationBuilder.DropTable("AdPlacements", (string)null);
		migrationBuilder.DropTable("AdvertisingSettings", (string)null);
		migrationBuilder.DropTable("AdAuctions", (string)null);
		migrationBuilder.DropTable("AdSpaces", (string)null);
	}

	protected override void BuildTargetModel(ModelBuilder modelBuilder)
	{
		modelBuilder.HasAnnotation("ProductVersion", (object)"9.0.3").HasAnnotation("Relational:MaxIdentifierLength", (object)63);
		NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
		modelBuilder.Entity("MealMate.Domain.AdminDashboard.AdminNotification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyAr").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyEn").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Channel"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRead"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleAr").IsRequired(true).HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleEn").IsRequired(true).HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Channel" });
			b.HasIndex(new string[1] { "IsRead" });
			b.HasIndex(new string[1] { "SentAtUtc" });
			b.HasIndex(new string[1] { "UserId" });
			b.HasIndex(new string[3] { "UserId", "IsRead", "SentAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdminNotifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.AdminDashboard.ProfitabilityAlert", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ClassificationRunId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CustomerDailyPrice").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("DailySupplyGross").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ExpectedProfit").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MeanReference").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MinimumProfit").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ReferenceDailyBox").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ResolvedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "ClassificationRunId" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[1] { "ResolvedAtUtc" });
			b.HasIndex(new string[1] { "ResolvedByUserId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[3] { "RestaurantId", "ProgramId", "BundleId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ProfitabilityAlerts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.AdminDashboard.SystemSetting", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Key").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ValueJson").IsConcurrencyToken(true).IsRequired(true), "jsonb");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "Key" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SystemSettings", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdAuction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdSpaceId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("BidsCloseAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("BidsOpenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ClosedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("DisplayEndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("DisplayStartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PolicyId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PolicyVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("WinningAmount"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("WinningBidId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("WinningRestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AdSpaceId" });
			b.HasIndex(new string[1] { "BidsCloseAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdAuctions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdBid", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("AcceptedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdAuctionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("IdempotencyKey").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsWinning"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "AdAuctionId", "RestaurantId" });
			b.HasIndex(new string[3] { "AdAuctionId", "RestaurantId", "IdempotencyKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdBids", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdCommercialPolicy", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("BidStep"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MinimumBid"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NextBidderPolicy").IsRequired(true).HasMaxLength(48), "character varying(48)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StopSettlementRule").IsRequired(true).HasMaxLength(48), "character varying(48)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TiePolicy").IsRequired(true).HasMaxLength(32), "character varying(32)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Version"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("WinnerPaymentDeadlineMinutes"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Version" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdCommercialPolicies", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdFinancialLedgerEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(32), "character varying(32)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdAuctionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ExternalRefundIdentifier").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderTransactionId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Source").IsRequired(true).HasMaxLength(48), "character varying(48)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AdAuctionId" });
			b.HasIndex(new string[3] { "Source", "ProviderTransactionId", "Action" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdFinancialLedgerEntries", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdPaymentTransaction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdAuctionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("InitiatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentProvider").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderTransactionId").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(32), "character varying(32)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AdAuctionId" });
			b.HasIndex(new string[2] { "PaymentProvider", "ProviderTransactionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdPaymentTransactions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdPlacement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdAuctionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AdSpaceId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CreativeStorageKey"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DisplayEndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DisplayStartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReviewNotes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StopReason").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("StoppedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("StoppedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TargetId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TargetType").HasMaxLength(64), "character varying(64)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AdAuctionId" }).IsUnique(true);
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "AdSpaceId", "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdPlacements", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdPlacementEvent", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventId").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventType").IsRequired(true).HasMaxLength(16), "character varying(16)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PlacementId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "OccurredAtUtc" });
			b.HasIndex(new string[2] { "PlacementId", "EventId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdPlacementEvents", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdSpace", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("HeightPx"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MarketIdsJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PlacementKey").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("WidthPx"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "PlacementKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdSpaces", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdvertisingSettings", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AuctionsEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BusinessTimezone").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ExpectedVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LastChangeReason").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastChangedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("LastChangedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdvertisingSettings", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.CustomerAllergyPreference", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenNameAr").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenNameEn").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDislike"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Severity").HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AllergenCode" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[3] { "CustomerUserId", "AllergenCode", "IsDislike" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerAllergyPreferences", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.CustomerDietaryPreference", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferenceCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PreferenceCode" });
			b.HasIndex(new string[2] { "CustomerUserId", "PreferenceCode" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerDietaryPreferences", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.DailyMealSelection", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedMenuDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedMenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("ScheduleMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SelectedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[1] { "ResolvedMenuDayId" });
			b.HasIndex(new string[1] { "ResolvedMenuPlanVersionId" });
			b.HasIndex(new string[1] { "ResolvedProgramId" });
			b.HasIndex(new string[1] { "ScheduleMode" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "SubscriptionDayId", "MealId" }).IsUnique(true), "\"IsDeleted\" = FALSE");
			b.HasIndex(new string[2] { "SubscriptionDayId", "SortOrder" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DailyMealSelections", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.DailyRestaurantSelection", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SelectedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SelectedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SelectionSource").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SelectionSource" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[3] { "SubscriptionDayId", "RestaurantId", "SelectedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DailyRestaurantSelections", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.DiversificationLimitSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DurationDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LMax"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LMin"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LimitValue"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("NSnapshot"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RawLimit").HasPrecision(18, 6), "numeric(18,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CapturedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DiversificationLimitSnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.FreezeLedgerEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DaysAdded"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EndDateAfter"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EndDateBefore"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FreezeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecordedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FreezeId" });
			b.HasIndex(new string[1] { "RecordedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FreezeLedgerEntries", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.MealChangeRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FromMealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RequestedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ToMealId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FromMealId" });
			b.HasIndex(new string[1] { "RequestedByUserId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "ToMealId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealChangeRequests", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionCalendar", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("GeneratedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("TotalServiceDays"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "GeneratedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionCalendars", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CalendarId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConfirmedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeliveryAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeliveryAddressLine").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeliveryBlockRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeliveryRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FailureReasonCode").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsAutoSelected"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LockAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReplacementWindowClosesAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReplacementWindowOpensAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedMenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SelectionDeadlineUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SourceFamilyDeliveryAmendmentId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CalendarId" });
			b.HasIndex(new string[1] { "LockAtUtc" });
			b.HasIndex(new string[1] { "ResolvedMenuPlanVersionId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "ServiceDate" });
			b.HasIndex(new string[1] { "SourceFamilyDeliveryAmendmentId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[2] { "SubscriptionId", "ServiceDate" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionDays", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionDayMealSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RequiredSlotsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MenuPlanVersionId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" }).IsUnique(true);
			b.HasIndex(new string[2] { "SubscriptionId", "ServiceDate" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionDayMealSnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionFreeze", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DaysCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EndsOn"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("StartsOn"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[3] { "SubscriptionId", "StartsOn", "EndsOn" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionFreezes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.ChartOfAccount", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AccountType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ParentId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AccountType" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "ParentId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ChartOfAccounts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.DailyRevenueRecognition", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("JournalEntryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "JournalEntryId" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[2] { "SubscriptionDayId", "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DailyRevenueRecognitions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.DeferredRevenue", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeferredRevenues", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.FinancialAuditTrail", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AfterJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BeforeJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("EntityId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EntityType").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Action" });
			b.HasIndex(new string[1] { "ActorUserId" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "EntityId" });
			b.HasIndex(new string[1] { "EntityType" });
			b.HasIndex(new string[1] { "OccurredAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FinancialAuditTrails", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.FinancialSetting", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SettingKey").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ValueJson").IsRequired(true), "jsonb");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			b.HasIndex(new string[1] { "SettingKey" });
			b.HasIndex(new string[2] { "SettingKey", "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FinancialSettings", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.FinancialSettlement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodMonth"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodYear"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SettledAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SettledAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "PeriodYear", "PeriodMonth" });
			b.HasIndex(new string[3] { "RestaurantId", "PeriodYear", "PeriodMonth" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FinancialSettlements", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.InfluencerPayableLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerPayableLedgers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.JournalEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").IsRequired(true).HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EntryNumber").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PostedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SourceReference").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "EntryNumber" }).IsUnique(true);
			b.HasIndex(new string[1] { "PostedAtUtc" });
			b.HasIndex(new string[1] { "SourceReference" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "JournalEntries", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.JournalEntryLine", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AccountId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("JournalEntryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Memo").HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AccountId" });
			b.HasIndex(new string[1] { "JournalEntryId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "JournalEntryLines", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.OperatingExpense", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Category").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("IncurredOn"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Category" });
			b.HasIndex(new string[1] { "IncurredOn" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OperatingExpenses", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.PaymentGatewayConfig", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("RelativeFee").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Weight"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			b.HasIndex(new string[1] { "Weight" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PaymentGatewayConfigs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.RestaurantPayableLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[2] { "RestaurantId", "RecognizedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantPayableLedgers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.TaxRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Rate").HasPrecision(9, 6), "numeric(9,6)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "TaxRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.Campaign", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("ApprovedSnapshotVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CurrentSnapshotVersion").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CurrentSubscribers"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EnrollmentClosesAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EnrollmentOpensAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FundingMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MaxSubscribers"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MealMateShareRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RestaurantShareRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewEndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewStartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("RowVersion").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("TotalDiscountRate").HasPrecision(9, 6), "numeric(9,6)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FundingMode" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "EnrollmentOpensAtUtc", "EnrollmentClosesAtUtc" });
			b.HasIndex(new string[2] { "StartsAtUtc", "EndsAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Campaigns", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignAccountingEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FundingMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "FundingMode" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[3] { "CampaignId", "RestaurantId", "RecognizedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignAccountingEntries", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignAuditLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ActorId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AfterStateJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BeforeStateJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DetailsJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("JustificationReason").HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Action" });
			b.HasIndex(new string[1] { "ActorId" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "CreatedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignAuditLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignBundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[2] { "CampaignId", "BundleId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignBundles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignBundleDurationPrice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignBundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DurationDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Source").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Version"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignBundleId" });
			b.HasIndex(new string[2] { "CampaignBundleId", "DurationDays" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignBundleDurationPrices", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignCapacityReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Decision"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReviewedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "Decision" });
			b.HasIndex(new string[1] { "ReviewedAtUtc" });
			b.HasIndex(new string[1] { "ReviewedByUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignCapacityReviews", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("CampaignSnapshotVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("FinalAmount"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PriceQuoteSnapshotId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PricingPolicyId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("PricingPolicyVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ProcessingLeaseExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignCheckoutSessions");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignDiscountSplit", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FundingMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MealMateShareRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RestaurantShareRate").HasPrecision(9, 6), "numeric(9,6)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "FundingMode" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignDiscountSplits", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignFinancialLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CheckoutSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Currency").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("EntryType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("JournalId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "CheckoutSessionId" });
			b.HasIndex(new string[1] { "CreatedAtUtc" });
			b.HasIndex(new string[1] { "EntryType" });
			b.HasIndex(new string[1] { "JournalId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignFinancialLedgers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignPaymentTransaction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CampaignCheckoutSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CheckoutSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Currency").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("FailedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("InitiatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentProvider").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ProcessingLeaseExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderPaymentReference").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderTransactionId").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RefundedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignCheckoutSessionId" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "CheckoutSessionId" });
			b.HasIndex(new string[1] { "ProcessingLeaseExpiresAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "PaymentProvider", "ProviderTransactionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignPaymentTransactions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignPricingPolicy", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContributionRulesJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Model").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RefundRulesJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoundingRule").IsRequired(true).HasMaxLength(32), "character varying(32)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StackingRulesJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Version"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Version" }).IsUnique(true);
			b.HasIndex(new string[2] { "CurrencyCode", "ApprovedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignPricingPolicies", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignProfitabilitySnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DetailsJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EvaluatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsViable"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MinimumExpectedMarginPct"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("OverallExpectedMarginPct"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("OverallWorstCaseMarginPct"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("OverrideApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OverrideApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OverrideReason"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ProfitabilityVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("RequiresOverride"), "boolean");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignProfitabilitySnapshots");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[2] { "CampaignId", "ProgramId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignPrograms", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignReservationAllocation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CheckoutSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Units"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CheckoutSessionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignReservationAllocations");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantApproval", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("DecidedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Decision"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("EnrollmentId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ApprovedByUserId" });
			b.HasIndex(new string[1] { "DecidedAtUtc" });
			b.HasIndex(new string[1] { "Decision" });
			b.HasIndex(new string[1] { "EnrollmentId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignRestaurantApprovals", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantCapacity", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MaxBoxes"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ReservedBoxes"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("ServiceDate"), "date");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "ServiceDate" });
			b.HasIndex(new string[3] { "CampaignId", "RestaurantId", "ServiceDate" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignRestaurantCapacities", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantEnrollment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("AcceptedSnapshotVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ApprovedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("CapacityOffered"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeclineReason"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("DedicatedDailyCapacity"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ParticipationStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RespondedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "CampaignId", "RestaurantId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignRestaurantEnrollments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignSubscriberCap", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Cap"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CurrentCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "LastReviewedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignSubscriberCaps", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignTierSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RankingsJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("TierVersion"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CampaignTierSnapshots");
		});
		modelBuilder.Entity("MealMate.Domain.Common.InboxProcessedEvent", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ConsumerName").IsRequired(true).HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("EventId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ProcessedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "ConsumerName", "EventId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InboxProcessedEvents", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Common.OutboxMessage", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AggregateId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AggregateType").HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeduplicationKey").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventType").IsRequired(true).HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("EventVersion").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastAttemptedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LastError").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("LeaseToken"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LockExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LockedByWorkerId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("NextAttemptAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PoisonedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ProcessedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("RetryCount"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DeduplicationKey" }).IsUnique(true);
			b.HasIndex(new string[2] { "ProcessedAtUtc", "CreatedAtUtc" });
			RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[4] { "ProcessedAtUtc", "PoisonedAtUtc", "LockExpiresAtUtc", "NextAttemptAtUtc" }), "IX_OutboxMessages_Lease_Pending");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OutboxMessages", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Common.UploadFileRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentType").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FileName").IsRequired(true).HasMaxLength(260), "character varying(260)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Folder").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Scope").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("SizeBytes"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StorageKey").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("UploadedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("UploadedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "StorageKey" }).IsUnique(true);
			b.HasIndex(new string[1] { "UploadedByUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UploadFileRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.AdminComplaintReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DecisionNotes").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReviewerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "ReviewedAtUtc" });
			b.HasIndex(new string[1] { "ReviewerUserId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdminComplaintReviews", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.Complaint", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Category").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").IsRequired(true).HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(10, 7), "numeric(10,7)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(10, 7), "numeric(10,7)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SubmittedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Title").IsRequired(true).HasMaxLength(300), "character varying(300)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Category" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubmittedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			RelationalIndexBuilderExtensions.HasFilter(RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[2] { "OrderId", "Category" }), "IX_Complaints_Active_ReassignmentRequest"), "\"Category\" = 'DriverReassignmentRequested' AND \"Status\" IN (1, 2) AND \"OrderId\" IS NOT NULL");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Complaints", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ComplaintClassification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CategoryCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ClassifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ClassifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Severity").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CategoryCode" });
			b.HasIndex(new string[1] { "ClassifiedAtUtc" });
			b.HasIndex(new string[1] { "ClassifiedByUserId" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "Severity" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ComplaintClassifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ComplaintEvidencePhoto", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentType").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StoragePath").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("UploadedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "UploadedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ComplaintEvidencePhotos", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.CustomerCompensation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CompensationType").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("FreeDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CompensationType" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerCompensations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.FreeDayCompensation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DaysGranted"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FreeDayCompensations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ResponsibilityDecision", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("DecidedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DecidedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Party"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(2000), "character varying(2000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "DecidedAtUtc" });
			b.HasIndex(new string[1] { "DecidedByUserId" });
			b.HasIndex(new string[1] { "Party" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ResponsibilityDecisions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.RestaurantBoxDeduction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("BoxesDeducted"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantBoxDeductions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.RestaurantComplaintResponse", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RespondedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RespondedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResponseText").IsRequired(true).HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "RespondedAtUtc" });
			b.HasIndex(new string[1] { "RespondedByUserId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantComplaintResponses", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.SupportTicket", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("AssignedToUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Body").IsRequired(true).HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Priority").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Subject").IsRequired(true).HasMaxLength(300), "character varying(300)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AssignedToUserId" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "Priority" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SupportTickets", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CancellationFeeRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CancellationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FeeRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CancellationId" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CancellationFeeRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerBankDetails", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AccountHolderName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Iban").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsVerified"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "Iban" });
			b.HasIndex(new string[1] { "IsVerified" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerBankDetails", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerCashWallet", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerCashWallets", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerPromotionalWallet", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerPromotionalWallets", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerWithdrawalRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "ReviewedByUserId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerWithdrawalRequests", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.RefundCalculation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CalculatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CancellationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SnapshotJson"), "jsonb");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CalculatedAtUtc" });
			b.HasIndex(new string[1] { "CancellationId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RefundCalculations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.RefundOperation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConfirmedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Currency").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Detail").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InvoiceId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LocalAppliedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OperationReference").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId").IsRequired(true).HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentProvider").IsRequired(true).HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderRefundId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderRefundReference").HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReconciledAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("State"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SubmittedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version").IsConcurrencyToken(true), "bigint");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "OperationReference" }).IsUnique(true);
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "ProviderRefundId" }).IsUnique(true), "\"ProviderRefundId\" IS NOT NULL");
			b.HasIndex(new string[1] { "State" });
			b.HasIndex(new string[2] { "PaymentProvider", "PaymentId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RefundOperations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.SubscriptionCancellation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BatchId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BatchLineId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContributionRefundsJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CountedDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FamilyGroupId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PayerCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResultMetadataJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SettlementKind").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("UnusedDays"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "SubscriptionId", "SettlementKind" }).IsUnique(true), "\"SettlementKind\" = 'FamilyCancellation'");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionCancellations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.SubscriptionPayment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayCode").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayTransactionId").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "GatewayCode" });
			b.HasIndex(new string[1] { "GatewayTransactionId" });
			b.HasIndex(new string[1] { "PaidAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionPayments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.WalletTransaction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReferenceId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReferenceType").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Type"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("WalletId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("WalletType"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "OccurredAtUtc" });
			b.HasIndex(new string[1] { "Type" });
			b.HasIndex(new string[1] { "WalletId" });
			b.HasIndex(new string[1] { "WalletType" });
			b.HasIndex(new string[2] { "ReferenceType", "ReferenceId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[3] { "ReferenceType", "ReferenceId", "CustomerUserId" }).IsUnique(true), "\"ReferenceType\" = 'FamilyCancellationRefund'");
			b.HasIndex(new string[3] { "WalletId", "WalletType", "OccurredAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "WalletTransactions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.CustomerPhoneRevealGrant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ClientRequestId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConsumedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ContactCaseId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FirstAttemptCallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SecondAttemptCallId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ContactCaseId" });
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			b.HasIndex(new string[2] { "DriverUserId", "ClientRequestId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerPhoneRevealGrants", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryConfirmationScan", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BarcodeValue").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PhotoPath").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ScannedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BarcodeValue" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "ScannedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryConfirmationScans", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryContactAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ContactCaseId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EndedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("InitiatorType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InitiatorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Ordinal"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Outcome").IsRequired(true).HasMaxLength(32), "character varying(32)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("QualifiesAsNoAnswer"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CallId" }).IsUnique(true);
			b.HasIndex(new string[2] { "ContactCaseId", "Ordinal" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryContactAttempts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryContactCase", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ActiveDriverAssignmentId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ContactCycleId"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("HoldId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("LastCallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("QualifiedMissedCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("State").IsRequired(true).HasMaxLength(16), "character varying(16)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripStopId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version").IsConcurrencyToken(true), "bigint");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "TripStopId", "ActiveDriverAssignmentId" }).IsUnique(true), "\"State\" = 'Open'");
			b.HasIndex(new string[2] { "TripStopId", "State" });
			b.HasIndex(new string[3] { "TripStopId", "ActiveDriverAssignmentId", "ContactCycleId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryContactCases", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryHold", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ContactCaseId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FirstAttemptCallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("HeldAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReleasedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResolutionReason"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ResolvedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ResumedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TripId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TripStopId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "HeldAtUtc" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "TripId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryHolds", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryTrackingPoint", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("AccuracyMeters"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("Heading"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReceivedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecordedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("SpeedKmh"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "RecordedAtUtc" });
			b.HasIndex(new string[1] { "TripId" });
			b.HasIndex(new string[2] { "TripId", "RecordedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryTrackingPoints", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverAppSettings", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("AutoLaunchNavigation"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("AutoSyncOnReconnect"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("BiometricLoginEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferredLanguage").HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferredNavigationApp").HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("SoundAlertsEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("VibrationEnabled"), "boolean");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DriverProfileId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverAppSettings", (string)null, (Action<TableBuilder>)delegate(TableBuilder t)
			{
				t.HasCheckConstraint("CK_DriverAppSettings_PreferredLanguage", "\"PreferredLanguage\" IS NULL OR \"PreferredLanguage\" IN ('ar', 'en')");
				t.HasCheckConstraint("CK_DriverAppSettings_PreferredNavigationApp", "\"PreferredNavigationApp\" IS NULL OR \"PreferredNavigationApp\" IN ('GoogleMaps', 'Waze', 'AppleMaps')");
			});
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("AssignedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AssignedAtUtc" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "TripId" });
			b.HasIndex(new string[2] { "TripId", "DriverProfileId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverAssignments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverPerformanceStat", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("AvgRating").HasPrecision(4, 2), "numeric(4,2)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DeliveredCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FailedCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("OnTimeRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("PeriodEnd"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("PeriodStart"), "date");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "PeriodEnd" });
			b.HasIndex(new string[1] { "PeriodStart" });
			b.HasIndex(new string[3] { "DriverProfileId", "PeriodStart", "PeriodEnd" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverPerformanceStats", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverRating", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Comment").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Score"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "Score" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[3] { "DriverProfileId", "CustomerUserId", "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverRatings", (string)null, (Action<TableBuilder>)delegate(TableBuilder t)
			{
				t.HasCheckConstraint("CK_DriverRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
			});
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverWorkSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("AverageDeliveryMinutes"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CompletedOrdersCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DailyTargetOrders"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("EndLatitude"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("EndLongitude"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EndedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FailedOrdersCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("InDeliveryOrdersCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("OnTimeRatePercentage"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ShiftLabel"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("StartLatitude"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<double?>(b.Property<double?>("StartLongitude"), "double precision");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("TotalAssignedOrders"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<double>(b.Property<double>("TotalDistanceKm"), "double precision");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasFilter(RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "DriverProfileId" }).IsUnique(true), "IX_DriverWorkSessions_DriverProfileId_Active"), "\"Status\" = 'Active'");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverWorkSessions");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.PickupConditionPhoto", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BoxId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Checksum").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConfirmedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentType").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PickupValidationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("SizeBytes"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StorageKey").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("UploadedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "StorageKey" }).IsUnique(true);
			b.HasIndex(new string[4] { "TripId", "BoxId", "DriverProfileId", "UploadedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PickupConditionPhotos", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.PickupScan", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BarcodeValue").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasDefaultValue<bool>(RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsManualEntry").ValueGeneratedOnAdd(), "boolean"), (object)false);
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ManualEntryReason").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PhotoPath").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ScannedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BarcodeValue" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "ScannedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "TripId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PickupScans", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.PickupValidation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BarcodeValue").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BoxId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConsumedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverAssignmentId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsManualEntry"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TokenHash").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ValidatedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "TokenHash" }).IsUnique(true);
			b.HasIndex(new string[4] { "TripId", "BoxId", "DriverProfileId", "ValidatedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PickupValidations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.SafeContactLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactType").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("Within3Km"), "boolean");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "OccurredAtUtc" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "Within3Km" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SafeContactLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.SecondDeliveryAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AttemptNumber"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Outcome").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ScheduledAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "ScheduledAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[2] { "OrderId", "AttemptNumber" });
			b.HasIndex(new string[2] { "SubscriptionDayId", "AttemptNumber" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SecondDeliveryAttempts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.Trip", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DispatcherUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("PlannedDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ShiftName").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("StartedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" });
			b.HasIndex(new string[1] { "DispatcherUserId" });
			b.HasIndex(new string[1] { "PlannedDate" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "RestaurantId", "PlannedDate" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Trips", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.TripStop", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ArrivedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("BoxCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CustomerMaskedId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CustomerName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CustomerPhone").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DailyOrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeliveredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FailureReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullAddress").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(10, 7), "numeric(10,7)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(10, 7), "numeric(10,7)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("MealBarcodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealSummaryAr").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealSummaryEn").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OrderCode").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SequenceNumber"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SlotLabel").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("StartedDeliveryAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ZoneNameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ZoneNameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DailyOrderId" });
			b.HasIndex(new string[1] { "MealBarcodeId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalIndexBuilderExtensions.HasFilter(RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "SubscriptionDayId" }).IsUnique(true), "IX_TripStops_Active_SubscriptionDayId"), "\"SubscriptionDayId\" IS NOT NULL AND \"Status\" IN (1, 2)");
			b.HasIndex(new string[1] { "TripId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "TripStops", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Entities.PaymentGatewayAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CustomerReference").IsRequired(true).HasMaxLength(255), "character varying(255)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ExpectedAmount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ExpectedCurrency").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsSandbox"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId").IsRequired(true).HasMaxLength(255), "character varying(255)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Provider").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderReference"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TrackId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "TrackId" }).IsUnique(true);
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[3] { "Provider", "IsSandbox", "PaymentId" }).IsUnique(true), "\"PaymentId\" <> ''");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PaymentGatewayAttempts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Entities.PayzahGatewayConfig", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BaseUrl").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CountryCode").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DefaultCurrency").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EncryptedPrivateKey").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayName").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsSandbox"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MaskedToken").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PayzahGatewayConfigs");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Country", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DialCode").IsRequired(true).ValueGeneratedOnAdd()
				.HasMaxLength(8), "character varying(8)"), (object)"+965");
			RelationalPropertyBuilderExtensions.HasDefaultValue<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FlagEmoji").IsRequired(true).ValueGeneratedOnAdd()
				.HasMaxLength(16), "character varying(16)"), (object)"\ud83c\uddf0\ud83c\uddfc");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Countries", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Document", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Checksum").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentType").IsRequired(true).HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DocumentType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FileName").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OwnerId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("OwnerType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("SizeBytes"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StoragePath").IsRequired(true).HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DocumentType" });
			b.HasIndex(new string[2] { "OwnerType", "OwnerId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Documents", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.DocumentVerification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DocumentId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ReviewedByUserId" });
			b.HasIndex(new string[2] { "DocumentId", "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DocumentVerifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.IdempotencyRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("HttpStatusCode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Key").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RequestHash").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResourceUri").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResponseBody"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Scope").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			b.HasIndex(new string[1] { "Key" });
			b.HasIndex(new string[1] { "UserId" });
			b.HasIndex(new string[2] { "Scope", "Key" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "IdempotencyRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Region", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("CentroidLatitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("CentroidLongitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ParentRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Type").IsRequired(true).HasMaxLength(20), "character varying(20)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "ParentRegionId" });
			b.HasIndex(new string[2] { "CountryId", "Code" }).IsUnique(true);
			b.HasIndex(new string[3] { "CountryId", "ParentRegionId", "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Regions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.RuleVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ApprovalStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EffectiveFromUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EffectiveToUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OwnerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PreviousVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RuleKey").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ScopeEntityId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ScopeType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VersionCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PreviousVersionId" });
			b.HasIndex(new string[1] { "ScopeEntityId" });
			b.HasIndex(new string[2] { "RuleKey", "VersionCode" }).IsUnique(true);
			b.HasIndex(new string[3] { "RuleKey", "ApprovalStatus", "EffectiveFromUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RuleVersions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Tenant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DefaultLanguage").IsRequired(true).HasMaxLength(5), "character varying(5)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Domain").IsRequired(true).HasMaxLength(253), "character varying(253)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TimeZoneId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "CountryId" });
			b.HasIndex(new string[1] { "Domain" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Tenants", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AccessScope", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("EntityId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EntityType").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ScopeType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TenantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "TenantId" });
			b.HasIndex(new string[5] { "ScopeType", "CountryId", "RegionId", "RestaurantId", "TenantId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AccessScopes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AdminDeviceToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Platform"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Token").IsRequired(true).HasMaxLength(512), "character varying(512)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Token" }).IsUnique(true);
			b.HasIndex(new string[2] { "UserId", "DeviceId" });
			b.HasIndex(new string[2] { "UserId", "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdminDeviceTokens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AdminInvitation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AcceptedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("AcceptedUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("AccessScopeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("InvitedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InvitedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NormalizedEmail").IsRequired(true).HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RevokedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RevokedReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoleName").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AccessScopeId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "NormalizedEmail" }).IsUnique(true), "\"Status\" = 1");
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AdminInvitations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AuditLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AfterJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BeforeJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("EntityId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EntityType").IsRequired(true).HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("OccurredAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SourceChannel").HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ActorUserId" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "OccurredAtUtc" });
			b.HasIndex(new string[2] { "EntityType", "EntityId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AuditLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AuthLoginChallenge", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AttemptCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConsumedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceInfo").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("IpAddress").HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MaxAttempts"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NextStep").IsRequired(true).HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TokenHash").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			b.HasIndex(new string[1] { "TokenHash" }).IsUnique(true);
			b.HasIndex(new string[1] { "UserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AuthLoginChallenges", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.CustomerAddress", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressLine").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Area").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Avenue").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Block").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BlockRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Building").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Floor").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Governorate").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDefault"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Label").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("NotifyOnDelivery"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Street").HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Unit").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BlockRegionId" });
			b.HasIndex(new string[2] { "UserId", "IsDefault" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerAddresses", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.CustomerDeviceToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Platform"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Token").IsRequired(true).HasMaxLength(512), "character varying(512)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Token" }).IsUnique(true);
			b.HasIndex(new string[2] { "UserId", "DeviceId" });
			b.HasIndex(new string[2] { "UserId", "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerDeviceTokens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.CustomerNotification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyAr").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyEn").IsRequired(true).HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeepLink").HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventKey").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReadAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "UserId", "ReadAtUtc" });
			b.HasIndex(new string[3] { "UserId", "EventKey", "SubjectId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerNotifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.CustomerProfile", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActiveDeliveryAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ActiveDeliveryAddressLine").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActiveDeliveryBlockRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActiveDeliveryRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AllergiesCompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("DateOfBirth"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DefaultRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeliveryAddressLine").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DeliveryLatitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DeliveryLongitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DietaryNotes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DietaryPattern").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DietaryPreferencesCompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("OnboardingCompleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PendingDeliveryAddressEffectiveFromUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PendingDeliveryAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferredLanguage").HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProfileImageStorageKey").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "UserId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerProfiles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.CustomerPushDeliveryLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventKey").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[3] { "UserId", "EventKey", "SubjectId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CustomerPushDeliveryLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DeliveryManagerDeviceToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceId"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Platform"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Token").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryManagerDeviceTokens");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DeliveryManagerNotification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeepLink"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRead"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Section"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Type").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryManagerNotifications");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DeliveryManagerRegistration", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangeRequestNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ContractExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CoverageScopeId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CoverageScopeName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CoverageType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Email").IsRequired(true).HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NationalId").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("NationalIdExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Phone").IsRequired(true).HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResultingUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubmittedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Email" });
			b.HasIndex(new string[1] { "NationalId" });
			b.HasIndex(new string[1] { "Phone" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DeliveryManagerRegistrations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DriverDeviceToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceId"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Platform"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Token").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverDeviceTokens");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DriverNotification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeepLink"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRead"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Section"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Type").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverNotifications");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DriverProfile", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ActivatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ContractExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DateOfBirthUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsAvailable"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("IsVehicleOwned"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LicenseExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LicenseNumber").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NationalId").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("NationalIdExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Nationality").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ProfileImageStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProfileImageStorageKey").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleColor").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleModel").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehiclePlate").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("VehicleRegistrationExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleType").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("VehicleYear"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("VerificationStatus"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "UserId" }).IsUnique(true);
			b.HasIndex(new string[1] { "VerificationStatus" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverProfiles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.DriverRegistration", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AdminApprovalStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AdminReviewNotes"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangeRequestNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ContractExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DateOfBirthUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Email").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("IsVehicleOwned"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LicenseExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LicenseNumber").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc").IsConcurrencyToken(true), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NationalId").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("NationalIdExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Nationality"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Phone").IsRequired(true).HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProfileImageStorageKey").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RegistrationSource").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("RestaurantApprovalStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RestaurantReviewNotes"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RestaurantReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResultingDriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status").IsConcurrencyToken(true), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubmittedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleColor"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleModel").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehiclePhotoStorageKey"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehiclePlate").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("VehicleRegistrationExpiryUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VehicleType").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("VehicleYear"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DriverRegistrations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.InfluencerProfile", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AccountHolderName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultCommissionRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DisplayName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("HasCompletedSocialOnboarding"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Iban").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InfluencerCode").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("LinkedCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LinkedCustomerUserIdRaw").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SocialLinksJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("VerificationStatus"), "integer");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "InfluencerCode" }).IsUnique(true), "\"InfluencerCode\" IS NOT NULL");
			b.HasIndex(new string[1] { "LinkedCustomerUserId" });
			b.HasIndex(new string[1] { "UserId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerProfiles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.OtpChallenge", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AttemptCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Channel").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CodeHash").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConsumedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Destination").IsRequired(true).HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MaxAttempts"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Purpose").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "UserId" });
			b.HasIndex(new string[3] { "Destination", "Purpose", "CreatedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OtpChallenges", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.Permission", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(150), "character varying(150)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Module").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "Module" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Permissions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RefreshToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceInfo").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("IpAddress").HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReplacedByTokenId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TokenHash").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			b.HasIndex(new string[1] { "TokenHash" }).IsUnique(true);
			b.HasIndex(new string[1] { "UserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RefreshTokens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantDeviceToken", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeviceId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Platform"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Token").IsRequired(true).HasMaxLength(512), "character varying(512)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Token" }).IsUnique(true);
			b.HasIndex(new string[2] { "RestaurantId", "IsActive" });
			b.HasIndex(new string[2] { "UserId", "DeviceId" });
			b.HasIndex(new string[2] { "UserId", "IsActive" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantDeviceTokens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantNotification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyAr").IsRequired(true).HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BodyEn").IsRequired(true).HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeepLink").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRead"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Section").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("SentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleAr").IsRequired(true).HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TitleEn").IsRequired(true).HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Type").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[3] { "UserId", "IsRead", "SentAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantNotifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantNotificationPreference", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("EmailEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventKey").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("PushEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "RestaurantId", "EventKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantNotificationPreferences", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantProfile", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ActivatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasDefaultValue<long>(RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("ApprovedVersion").ValueGeneratedOnAdd(), "bigint"), (object)1L);
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AutoConfirmOrders"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ClosedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CommercialLicenseNumber").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("CommissionPercent").HasPrecision(5, 2), "numeric(5,2)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactEmail").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactPhone").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsArchived"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsOperationallyActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LegalNameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LegalNameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ManualBusyOverride"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PrimaryRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TradeNameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TradeNameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("VerificationStatus"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsOperationallyActive" });
			b.HasIndex(new string[1] { "UserId" }).IsUnique(true);
			b.HasIndex(new string[1] { "VerificationStatus" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantProfiles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantRegistration", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Address").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasDefaultValue<long>(RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("ApprovedVersion").ValueGeneratedOnAdd(), "bigint"), (object)1L);
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AuthorizedSignatoryCivilId").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AuthorizedSignatoryTitle").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankAccountNumber").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankBeneficiaryName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangeRequestNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("CommercialLicenseExpiry"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CommercialLicenseNumber").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("CommercialRegistrationExpiry"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CommercialRegistrationNumber").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CompanyCivilId").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactEmail").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactPhone").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("DataResponsibilityConfirmed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool?>(b.Property<bool?>("DeliveryByRestaurantConfirmed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("FoodLicenseExpiry"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FoodLicenseNumber").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Iban").HasMaxLength(34), "character varying(34)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InstagramHandle").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Latitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LegalEntityType").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LegalNameAr").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LegalNameEn").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Longitude").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OwnerName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PrimaryContactEmail").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PrimaryContactName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PrimaryContactPhone").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PrimaryContactTitle").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RestaurantCode").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RestaurantNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResultingRestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SelectedBundleIdsJson").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SelectedProgramIdsJson").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ServiceRegionIdsJson").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SettlementCurrency").HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("SubmissionDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubmittedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SwiftCode").HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TradeNameAr").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TradeNameEn").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Website").HasMaxLength(500), "character varying(500)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CommercialRegistrationNumber" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "RestaurantCode" }).IsUnique(true), "\"RestaurantCode\" IS NOT NULL");
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubmittedByUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantRegistrations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantRegistrationFieldReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FieldKey").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "RegistrationId", "FieldKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantRegistrationFieldReviews", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantReviewHistory", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FromStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReviewItemId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ToStatus"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "ReviewItemId", "CreatedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantReviewHistories", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantReviewItem", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FieldKey").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Section").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubjectId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SubjectType").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SubmittedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VersionId").HasMaxLength(120), "character varying(120)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[3] { "RestaurantId", "Section", "Status" });
			b.HasIndex(new string[4] { "RegistrationId", "SubjectType", "SubjectId", "FieldKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantReviewItems", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantSettingsChangeRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AdminNotes").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasDefaultValue<long>(RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("BaseVersion").ValueGeneratedOnAdd(), "bigint"), (object)1L);
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Category").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangedFieldsJson").HasMaxLength(4000), "character varying(4000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DecisionEventsJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadJson").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PayloadVersion").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SubmittedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[3] { "RestaurantId", "Category", "Status" });
			b.HasIndex(new string[3] { "RestaurantId", "Status", "CreatedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantSettingsChangeRequests", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RolePermission", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PermissionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RoleId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PermissionId" });
			b.HasIndex(new string[2] { "RoleId", "PermissionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RolePermissions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.UserScope", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AccessScopeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AccessScopeId" });
			b.HasIndex(new string[2] { "UserId", "AccessScopeId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UserScopes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCancellationImpact", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CancellationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CommissionCalculationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecordedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CancellationId" });
			b.HasIndex(new string[1] { "CommissionCalculationId" });
			b.HasIndex(new string[1] { "RecordedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerCancellationImpacts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CommissionRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DiscountPercent").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EligibleDurationsJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MaxUses"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("UsedCount"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[2] { "StartsAtUtc", "EndsAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerCodes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCommissionCalculation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CalculatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CommissionRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CalculatedAtUtc" });
			b.HasIndex(new string[1] { "CodeId" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerCommissionCalculations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCustomerLink", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AttributionSource").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LinkedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "LinkedAtUtc" });
			b.HasIndex(new string[2] { "InfluencerProfileId", "CustomerUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerCustomerLinks", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerEligibilityRule", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerEligibilityRules", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerPayoutHistory", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankReference").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReceiptPath").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("WithdrawalRequestId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "PaidAtUtc" });
			b.HasIndex(new string[1] { "WithdrawalRequestId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerPayoutHistories", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerWithdrawalApproval", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("DecidedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Decision"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("WithdrawalRequestId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ApprovedByUserId" });
			b.HasIndex(new string[1] { "DecidedAtUtc" });
			b.HasIndex(new string[1] { "Decision" });
			b.HasIndex(new string[1] { "WithdrawalRequestId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerWithdrawalApprovals", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerWithdrawalRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AccountHolderName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Iban").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("InfluencerProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReferenceNumber").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("WithdrawalMethod").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InfluencerProfileId" });
			b.HasIndex(new string[1] { "ReferenceNumber" }).IsUnique(true);
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "ReviewedByUserId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "InfluencerWithdrawalRequests", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.Offer", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<bool>(RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AllowPromoCodeStacking").ValueGeneratedOnAdd(), "boolean"), (object)false);
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllowedGovernoratesJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BannerImageUrl").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CurrentUsageCount").IsConcurrencyToken(true), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CustomerSegment"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DiscountRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DiscountType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FundingMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MaxSubscriptionDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MinSubscriptionDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PerCustomerLimit"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("PlatformShareRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PromoCode").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("RestaurantShareRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TermsAndConditionsAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TermsAndConditionsEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("TotalUsageLimit"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Type"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "PromoCode" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "StartsAtUtc", "EndsAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Offers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferAuditLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ActorId").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AfterStateJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BeforeStateJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DetailsJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("JustificationReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OfferId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CreatedAtUtc" });
			b.HasIndex(new string[1] { "OfferId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OfferAuditLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferBundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OfferId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "OfferId", "BundleId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OfferBundles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OfferId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "OfferId", "ProgramId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OfferPrograms", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferRedemption", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OfferId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PromoCodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RedeemedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerId" });
			b.HasIndex(new string[1] { "OfferId" });
			b.HasIndex(new string[1] { "RedeemedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[2] { "OfferId", "SubscriptionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OfferRedemptions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferRestaurant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OfferId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "OfferId", "RestaurantId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OfferRestaurants", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Bundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AvailableForFamily"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("BreakfastCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DinnerCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ImageStorageKey").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LunchCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MealsPerDay"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<byte[]>(b.Property<byte[]>("RowVersion").IsConcurrencyToken(true).IsRequired(true)
				.ValueGeneratedOnAddOrUpdate(), "bytea");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SnackCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StatusReason").HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AvailableForFamily" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "ProgramId", "Code" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Bundles", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.ClassificationRun", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ConfidenceLevel").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("LowerBound").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("MeanReferenceDaily").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("RestaurantCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RunAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("SdReferenceDaily").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("UpperBound").HasPrecision(18, 3), "numeric(18,3)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			b.HasIndex(new string[3] { "ProgramId", "BundleId", "RunAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ClassificationRuns", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.DiscountsAndReferralLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("AppliedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DiscountType").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FundingMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MetadataJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AppliedAtUtc" });
			b.HasIndex(new string[1] { "Code" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DiscountsAndReferralLogs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FamilyDiscountAmount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FamilyDiscountPercent").HasPrecision(5, 4), "numeric(5,4)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FamilyGroupId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FinalPrice").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayInvoiceId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayPaymentId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("IdempotencyKey").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PackageId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("PackageMaxMembers"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("PackageMinMembers"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PackageNameAr").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PackageNameEn").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PayerCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PaymentAnchorSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Purpose").IsRequired(true).ValueGeneratedOnAdd()
				.HasMaxLength(20), "character varying(20)"), (object)"Initial");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RenewalRequestId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Subtotal").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version").IsConcurrencyToken(true), "bigint");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FamilyGroupId" });
			b.HasIndex(new string[1] { "GatewayPaymentId" }).IsUnique(true);
			b.HasIndex(new string[1] { "PayerCustomerUserId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "RenewalRequestId" }).IsUnique(true), "\"RenewalRequestId\" IS NOT NULL");
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[2] { "FamilyGroupId", "IdempotencyKey" }).IsUnique(true);
			RelationalIndexBuilderExtensions.HasFilter(RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "FamilyGroupId" }, "IX_FamilyCheckoutBatches_FamilyGroupId").IsUnique(true), "IX_FamilyCheckoutBatches_FamilyGroupId1"), "\"Status\" IN (0, 1) AND \"Purpose\" = 'Initial'");
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "FamilyGroupId" }, "IX_FamilyCheckoutBatches_FamilyGroupId_PaymentPending").IsUnique(true), "\"Status\" = 1");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyCheckoutBatches", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatchLine", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressLabel").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressLine").HasMaxLength(300), "character varying(300)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Area").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BatchId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Block").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BlockRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Building").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("CheckoutSelected"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeliveryAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("DurationDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FamilyDiscountAllocated").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FamilyMemberId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Floor").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Governorate").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IncludeFriday"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MenuPlanVersionsJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("NetPaidAmount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PreviousSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("PriceBeforeFamilyDiscount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RenewalRequestId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedRestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("StartDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Street").HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("Tier"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Unit").HasMaxLength(80), "character varying(80)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BatchId" });
			b.HasIndex(new string[1] { "FamilyMemberId" });
			b.HasIndex(new string[1] { "PreviousSubscriptionId" });
			b.HasIndex(new string[1] { "RenewalRequestId" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyCheckoutBatchLines", (string)null, (Action<TableBuilder>)delegate(TableBuilder t)
			{
				t.HasCheckConstraint("CK_FamilyCheckoutBatchLines_NetPaid", "\"NetPaidAmount\" >= 0 AND \"FamilyDiscountAllocated\" >= 0 AND \"PriceBeforeFamilyDiscount\" >= 0 AND \"NetPaidAmount\" <= \"PriceBeforeFamilyDiscount\"");
			});
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutPaymentAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AttemptKey").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BatchId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayInvoiceId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayPaymentId").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GatewayPaymentUrl").HasMaxLength(1024), "character varying(1024)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResultCode").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("VerifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version").IsConcurrencyToken(true), "bigint");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BatchId" });
			b.HasIndex(new string[1] { "GatewayPaymentId" }).IsUnique(true);
			b.HasIndex(new string[2] { "BatchId", "AttemptKey" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyCheckoutPaymentAttempts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyGroup", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ConvertedFromSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("DiscountPercentSnapshot").HasPrecision(5, 4), "numeric(5,4)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ManagerCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name").IsRequired(true).HasMaxLength(120), "character varying(120)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PaymentAnchorSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentInvoiceId"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("QuotedFinalPrice"), "numeric");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SelectedFamilyPackageId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ConvertedFromSubscriptionId" });
			b.HasIndex(new string[1] { "ManagerCustomerUserId" });
			b.HasIndex(new string[1] { "SelectedFamilyPackageId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyGroups", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMember", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ActivityLevel"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AgeYears"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeliveryAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DisplayName").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("DurationDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FamilyGroupId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Gender"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IncludeFriday"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("MemberSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NutritionalGoalCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Occupation").HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PhoneNumber").HasMaxLength(32), "character varying(32)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RelationshipCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Role"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("SelectedTier"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SetupStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("StartDate"), "date");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FamilyGroupId" });
			b.HasIndex(new string[1] { "MemberSubscriptionId" });
			b.HasIndex(new string[1] { "PhoneNumber" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyMembers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMemberAccess", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FamilyMemberId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("HasSeparateAccount"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("InviteExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InviteTokenHash").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPaused"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("LinkedCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PausedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RemovedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FamilyMemberId" }).IsUnique(true);
			b.HasIndex(new string[1] { "InviteTokenHash" });
			b.HasIndex(new string[1] { "LinkedCustomerUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyMemberAccesses", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMemberPermissions", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("CancelSubscription"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ChangeDeliveryTime"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ChangePassword"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ChangeProgram"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ConfirmMeals"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("EditAddress"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("EditDailyMeals"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("EditMealsAndDays"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("FamilyMemberId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("RequestFreeze"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("RequestRenewal"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("RequestUpgrade"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("SelectRestaurant"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("UnitExchange"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version").IsConcurrencyToken(true), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewCalendar"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewFamilyCalendars"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewFamilyInvoices"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewFamilyPlans"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewInvoices"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ViewProgram"), "boolean");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "FamilyMemberId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilyMemberPermissions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilySubscription", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DisplayName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MaxMealsAllowedPerDay"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MemberCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PrimarySubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Relationship").HasMaxLength(100), "character varying(100)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "MemberCustomerUserId" });
			b.HasIndex(new string[2] { "PrimarySubscriptionId", "MemberCustomerUserId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilySubscriptions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilySubscriptionDeliveryAmendment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("AddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressLabel").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressLine").IsRequired(true).HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AffectedDayIdsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Area"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Block"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BlockRegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EffectiveFromDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Governorate"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Version"), "bigint");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "SubscriptionId", "Version" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FamilySubscriptionDeliveryAmendments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PackageContentVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangeSummary").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentSnapshotJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByAdminUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EffectiveFromUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EffectiveToUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("VersionNumber"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[2] { "ProgramId", "VersionNumber" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PackageContentVersions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PaymentWebhookReceipt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Amount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CurrencyCode").HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CustomerReference").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EventKey").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FulfillmentAttemptCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("FulfillmentCompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Gateway").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InvoiceId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastFulfillmentAttemptUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LastFulfillmentErrorCode").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PayloadHash").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ProcessedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("VerifiedAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[2] { "Gateway", "EventKey" }).IsUnique(true);
			b.HasIndex(new string[2] { "Gateway", "InvoiceId" });
			b.HasIndex(new string[2] { "Gateway", "PaymentId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PaymentWebhookReceipts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PricingRuleVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DLong"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DShort"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("Gamma").HasPrecision(18, 6), "numeric(18,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RMax").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RMin").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("RoundingStepKwd").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RuleVersionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RuleVersionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PricingRuleVersions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Program", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AvailableForFamily"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CurrentVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ImageStorageKey").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPrimitiveCollectionBuilderExtensions.HasColumnType<string[]>(b.PrimitiveCollection<string[]>("KeywordsAr").IsRequired(true), "text[]");
			RelationalPrimitiveCollectionBuilderExtensions.HasColumnType<string[]>(b.PrimitiveCollection<string[]>("KeywordsEn").IsRequired(true), "text[]");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<byte[]>(b.Property<byte[]>("RowVersion").IsConcurrencyToken(true).IsRequired(true)
				.ValueGeneratedOnAddOrUpdate(), "bytea");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ShortCode").HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StatusReason").HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AvailableForFamily" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "ShortCode" });
			b.HasIndex(new string[1] { "SortOrder" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Programs", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PromoCode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DiscountPercent").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EligibleDurationDaysJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MaxUses"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("UsedCount"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[2] { "StartsAtUtc", "EndsAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "PromoCodes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.ReferralAttribution", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("AttributedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReferralCodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReferredCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReferrerUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ReferredCustomerUserId" }).IsUnique(true);
			b.HasIndex(new string[1] { "ReferrerUserId" });
			b.HasIndex(new string[2] { "ReferrerUserId", "ReferredCustomerUserId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ReferralAttributions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.ReferralCode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("MaxUses"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("OwnerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("RewardPercent").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("UsedCount"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "OwnerUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ReferralCodes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantBundlePricing", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ChangeRequestReason").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EffectiveFromUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EffectiveToUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LockStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PreviousPricingId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "LockStatus" });
			b.HasIndex(new string[1] { "PreviousPricingId" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[4] { "RestaurantId", "ProgramId", "BundleId", "EffectiveFromUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantBundlePricings", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantClassification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ClassificationRunId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsOutlierFlagged"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OutlierReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ReferenceDailyBox").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Tier"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "ClassificationRunId", "RestaurantId" }).IsUnique(true);
			b.HasIndex(new string[3] { "ProgramId", "BundleId", "Tier" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantClassifications", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantProgramBundleOffer", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[2] { "RestaurantId", "IsActive" });
			b.HasIndex(new string[3] { "RestaurantId", "ProgramId", "BundleId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantProgramBundleOffers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Subscription", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("AutoRenewConsentActorUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("AutoRenewConsentAmount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AutoRenewConsentAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AutoRenewConsentCurrency").HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AutoRenewConsentVersion").HasMaxLength(40), "character varying(40)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AutoRenewConsentWithdrawnAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("AutoRenewConsentWithdrawnByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("CheckoutAnchorDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CountryId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DeliveryAddress").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DurationDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EndDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FamilyGroupId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FamilyMemberId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FulfillmentAttemptCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("FulfillmentCompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IncludeFriday"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("InfluencerCodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastFulfillmentAttemptUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LastFulfillmentErrorCode").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastOperationAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PackageContentVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentInvoiceId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PaymentVerifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PricingRuleVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PromoCodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<byte[]>(b.Property<byte[]>("RowVersion").IsConcurrencyToken(true).IsRequired(true)
				.ValueGeneratedOnAddOrUpdate(), "bytea");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SelectedTier"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("StartDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "FamilyGroupId" });
			b.HasIndex(new string[1] { "FamilyMemberId" });
			b.HasIndex(new string[1] { "PackageContentVersionId" });
			b.HasIndex(new string[1] { "PaymentId" }).IsUnique(true);
			b.HasIndex(new string[1] { "PaymentInvoiceId" });
			b.HasIndex(new string[1] { "PricingRuleVersionId" });
			b.HasIndex(new string[1] { "PromoCodeId" });
			b.HasIndex(new string[1] { "StartDate" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[3] { "ProgramId", "BundleId", "SelectedTier" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Subscriptions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionFreezeRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ApprovedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FreezeDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("FreezeEndDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("FreezeStartDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ProcessedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionFreezeRequests", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionOperation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CorrelationId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MetadataJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("NewStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OperationType").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PerformedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PerformedByUserType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("PreviousStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("TimestampUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CorrelationId" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			b.HasIndex(new string[1] { "TimestampUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionOperations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRenewal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BatchId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<long?>(b.Property<long?>("BatchVersion"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CheckoutFingerprint").HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CompletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FamilyGroupId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FamilyMemberId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("NewSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PreviousSubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RequestedAddressId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("RequestedStartDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "BatchId" }).IsUnique(true), "\"BatchId\" IS NOT NULL");
			b.HasIndex(new string[1] { "FamilyGroupId" });
			b.HasIndex(new string[1] { "FamilyMemberId" });
			b.HasIndex(new string[1] { "NewSubscriptionId" });
			b.HasIndex(new string[1] { "PreviousSubscriptionId" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionRenewals", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantDaySnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContentHash").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantSnapshotId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SlotRequirementsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ContentHash" });
			b.HasIndex(new string[1] { "MenuPlanVersionId" });
			b.HasIndex(new string[2] { "RestaurantSnapshotId", "ServiceDate" }).IsUnique(true);
			b.HasIndex(new string[3] { "SubscriptionId", "RestaurantId", "ServiceDate" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionRestaurantDaySnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantPoolSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("EligibilityCheckedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsBackfilled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("RestaurantCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "EligibilityCheckedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionRestaurantPoolSnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsInitiallySelected"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OfferingSnapshotJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PoolSnapshotId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Tier"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "PoolSnapshotId", "RestaurantId" }).IsUnique(true);
			b.HasIndex(new string[2] { "SubscriptionId", "RestaurantId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionRestaurantSnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("A_t").HasPrecision(18, 6), "numeric(18,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CapturedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LMax"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LMin"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("LimitValue"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("NSnapshot"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ROfD").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReferencePricesJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RuleVersionsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SnapshotJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SupplyPricesJson").IsRequired(true), "jsonb");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CapturedAtUtc" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionSnapshots", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionSnapshotMigrationIssue", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DetailsJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReasonCode").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecordedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ResolvedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "SubscriptionId" }).IsUnique(true);
			b.HasIndex(new string[2] { "ResolvedAtUtc", "RecordedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionSnapshotMigrationIssues", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionTierUpgrade", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EffectiveAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("EffectiveServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("EffectiveServiceDatesJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("FromTier"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PayerCustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentCreationState").HasMaxLength(30), "character varying(30)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentId").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PaymentUrl").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("QuoteJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("QuotedRemainingDays"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RequestFingerprint").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RequestIdempotencyKey").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ToTier"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VerifiedCurrencyCode"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VerifiedInvoiceId"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("VerifiedPaidAmount").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("VerifiedPaidAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PaymentId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "SubscriptionId", "RequestIdempotencyKey" }).IsUnique(true), "\"RequestIdempotencyKey\" IS NOT NULL");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "SubscriptionTierUpgrades", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.ComplaintDeductionRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AppliedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ComplaintId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AppliedAtUtc" });
			b.HasIndex(new string[1] { "ComplaintId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ComplaintDeductionRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.MealPayable", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SnapshotJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[2] { "RestaurantId", "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealPayables", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantAccountStatement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("GeneratedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodMonth"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodYear"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SnapshotJson"), "jsonb");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "GeneratedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[3] { "RestaurantId", "PeriodYear", "PeriodMonth" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantAccountStatements", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantCommissionRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CommissionRate").HasPrecision(9, 6), "numeric(9,6)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RecognizedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RecognizedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantCommissionRecords", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantMonthlyInvoice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InvoiceNumber").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("IssuedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodMonth"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodYear"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InvoiceNumber" }).IsUnique(true);
			b.HasIndex(new string[1] { "IssuedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[3] { "RestaurantId", "PeriodYear", "PeriodMonth" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantMonthlyInvoices", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantPartialPayment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MonthlyInvoiceId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reference").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MonthlyInvoiceId" });
			b.HasIndex(new string[1] { "PaidAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantPartialPayments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantPayout", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BankReference").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PaidAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodMonth"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PeriodYear"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PaidAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[3] { "RestaurantId", "PeriodYear", "PeriodMonth" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantPayouts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.TransferReceipt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("PayoutId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StoragePath").IsRequired(true).HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("UploadedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("UploadedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "PayoutId" });
			b.HasIndex(new string[1] { "UploadedAtUtc" });
			b.HasIndex(new string[1] { "UploadedByUserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "TransferReceipts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.AllergenDefinition", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[2] { "IsActive", "SortOrder" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "AllergenDefinitions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.BranchStaffAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsBranchManager"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantBranchId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoleId").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "BranchStaffAssignments");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.BusyStatus", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EndsAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsBusy"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartsAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "EndsAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[3] { "RestaurantId", "IsBusy", "StartsAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "BusyStatuses", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.CapacityHold", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConfirmedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Quantity"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReleasedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			b.HasIndex(new string[2] { "SubscriptionId", "ServiceDate" }).IsUnique(true);
			b.HasIndex(new string[3] { "RestaurantId", "ServiceDate", "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CapacityHolds", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.DailyCapacity", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CampaignId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CapacityType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MaxBoxes"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ReservedBoxes"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CampaignId" });
			b.HasIndex(new string[1] { "CapacityType" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "ServiceDate" });
			b.HasIndex(new string[4] { "RestaurantId", "ServiceDate", "CapacityType", "CampaignId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DailyCapacities", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.DocumentTemplate", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AccentColor").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BackgroundColor").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Kind"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Language").IsRequired(true).HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LayoutJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("MarginPx"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PageHeightPx"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PageSizeLabel").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("PageWidthPx"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PublishedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ShowAppLogo"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("ShowRestaurantLogo"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Version"), "integer");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "Kind" }).IsUnique(true), "\"Status\" = 2");
			b.HasIndex(new string[2] { "Kind", "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "DocumentTemplates", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.FoodPreferenceDefinition", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "Code" }).IsUnique(true);
			b.HasIndex(new string[2] { "IsActive", "SortOrder" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "FoodPreferenceDefinitions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Ingredient", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenCodes").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Category"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultCaloriesPer100g").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultCarbsPer100g").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultFatPer100g").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultProteinPer100g").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("DefaultWeightGrams").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsCore"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsHalalCertified"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsLocal"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsOriginVisibleToCustomers"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealIconUrl").HasMaxLength(2048), "character varying(2048)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("OriginCountryCode").HasMaxLength(3), "character varying(3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RestaurantIconUrl").HasMaxLength(2048), "character varying(2048)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SupplierName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("TraceabilityNote").HasMaxLength(1000), "character varying(1000)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "Code" }).IsUnique(true);
			b.HasIndex(new string[2] { "RestaurantId", "IsCore" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Ingredients", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.IngredientFoodPreference", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("IngredientId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferenceCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "IngredientId", "PreferenceCode" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "IngredientFoodPreferences", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Meal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("AddedSugarsG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AllergensReviewed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AnalysisReference").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ApprovalStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("Calories").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("CarbsG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Code").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CrossContactNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("DataResponsibilityConfirmed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedForNewOrdersAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("FatG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("FiberG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsAvailableForNewOrders"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MayContainAllergenCodes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("NetCarbsG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NutritionSource").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NutritionTagCodes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PresentAllergenCodes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("ProteinG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RecipeVersion").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RestaurantNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("SaturatedFatG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SearchExclusionTagCodes").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("ServingWeightG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Slot"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("SodiumMg").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("SugarAlcoholG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SugarAlcoholType").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("TotalSugarsG").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("VerificationDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("VerifiedBy").HasMaxLength(200), "character varying(200)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ApprovalStatus" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "IsAvailableForNewOrders" });
			b.HasIndex(new string[1] { "MenuId" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "Code" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Meals", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealAllergen", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenNameAr").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AllergenNameEn").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AllergenCode" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[2] { "MealId", "AllergenCode" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealAllergens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealBarcode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BarcodeValue").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("GeneratedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRevoked"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PrintedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReissuedBarcodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RevocationReason").HasMaxLength(500), "character varying(500)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RevokedBy").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ScannedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TripStopId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BarcodeValue" }).IsUnique(true);
			b.HasIndex(new string[1] { "GeneratedAtUtc" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[2] { "SubscriptionDayId", "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealBarcodes", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealFoodPreference", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PreferenceCode").IsRequired(true).HasMaxLength(50), "character varying(50)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "MealId", "PreferenceCode" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealFoodPreferences", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealImage", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AltTextAr").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AltTextEn").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DisplayOrder"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ImageUrl").IsRequired(true).HasMaxLength(2048), "character varying(2048)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPrimary"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[2] { "MealId", "DisplayOrder" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealImages", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealIngredient", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("IngredientId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsOptional"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("PortionGrams").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IngredientId" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[2] { "MealId", "IngredientId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealIngredients", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealInvoice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BarcodeValue").HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InvoiceNumber").IsRequired(true).HasMaxLength(100), "character varying(100)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SnapshotJson"), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TemplateId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("TemplateVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("TotalBoxes"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "InvoiceNumber" }).IsUnique(true);
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "ServiceDate" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "TemplateId" });
			b.HasIndex(new string[2] { "RestaurantId", "ServiceDate" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealInvoices", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealLabel", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("GeneratedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Kind").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LabelPayloadJson").IsRequired(true), "jsonb");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TemplateId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("TemplateVersion"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "GeneratedAtUtc" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[1] { "TemplateId" });
			b.HasIndex(new string[3] { "SubscriptionDayId", "Kind", "MealId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealLabels", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealProgramAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "MealId", "ProgramId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealProgramAssignments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealRating", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Comment").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Score"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[1] { "Score" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[3] { "MealId", "CustomerUserId", "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MealRatings", (string)null, (Action<TableBuilder>)delegate(TableBuilder t)
			{
				t.HasCheckConstraint("CK_MealRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
			});
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Menu", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "NameEn" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Menus", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DayNumber"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<bool>(RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActiveInCycle").ValueGeneratedOnAdd(), "boolean"), (object)true);
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "MenuPlanVersionId", "DayNumber" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuDays", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuDayMeal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPrimary"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Slot").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[3] { "MenuDayId", "ProgramId", "MealId" }).IsUnique(true);
			b.HasIndex(new string[3] { "MenuDayId", "ProgramId", "Slot" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[4] { "MenuDayId", "ProgramId", "Slot", "IsPrimary" }).IsUnique(true), "\"IsPrimary\" = true");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuDayMeals", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlan", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsTemplate"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Mode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true).HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Source"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "RestaurantId", "IsTemplate" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuPlans", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanAudit", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Action").IsRequired(true).HasMaxLength(80), "character varying(80)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("PerformedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MenuPlanVersionId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuPlanAudits", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EffectiveFrom"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly?>(b.Property<DateOnly?>("EffectiveTo"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPrimaryOwner"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "MenuPlanId", "ProgramId" }).IsUnique(true);
			b.HasIndex(new string[3] { "ProgramId", "EffectiveFrom", "EffectiveTo" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuPlanPrograms", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EffectiveFrom"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPaused"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PausedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PendingSequenceJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReviewNotes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReviewedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReviewedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("VersionNumber"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "MenuPlanId", "VersionNumber" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "MenuPlanVersions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.OrderHandover", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasDefaultValue<int>(RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AttemptNumber").ValueGeneratedOnAdd(), "integer"), (object)1);
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DailyOrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("HandedOverAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("MealBarcodeId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RevocationReason").HasMaxLength(512), "character varying(512)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RevokedBy").HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(50), "character varying(50)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SupersedesHandoverId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TripId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TripStopId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DailyOrderId" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "HandedOverAtUtc" });
			b.HasIndex(new string[1] { "MealBarcodeId" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "TripId" });
			b.HasIndex(new string[1] { "TripStopId" });
			b.HasIndex(new string[2] { "DailyOrderId", "AttemptNumber" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OrderHandovers", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.OrderHandoverRevocation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("HandoverId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true).HasMaxLength(512), "character varying(512)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RevokedByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "HandoverId" });
			b.HasIndex(new string[1] { "RevokedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "OrderHandoverRevocations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.ProgramMealSlotRequirement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsRequired"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Slot").IsRequired(true).HasMaxLength(20), "character varying(20)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "ProgramId", "Slot" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ProgramMealSlotRequirements", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.ProgramNutritionStandard", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("CarbMode").IsRequired(true).HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CarbsGMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CarbsGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CookedCarbsGMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CookedCarbsGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CookedProteinGMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("CookedProteinGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FatGMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FatGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("FiberGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("KcalMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("KcalMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal?>(b.Property<decimal?>("KetoDayNetCarbsLimit").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("MealCategory").IsRequired(true).HasMaxLength(10), "character varying(10)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ProteinGMax").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<decimal>(b.Property<decimal>("ProteinGMin").HasPrecision(18, 3), "numeric(18,3)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("StandardVersion").IsRequired(true).HasMaxLength(40), "character varying(40)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "ProgramId", "IsActive" });
			b.HasIndex(new string[3] { "ProgramId", "MealCategory", "StandardVersion" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "ProgramNutritionStandards", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantBranch", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("AddressEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BranchCode").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ContactPhone").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GovernorateAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("GovernorateEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPrimary"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ManagerStaffId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PhotoStorageKey"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantRegistrationId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantBranches");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantConfirmation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ConfirmedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DailyOrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Notes").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RespondedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ConfirmedByUserId" });
			b.HasIndex(new string[1] { "DailyOrderId" });
			b.HasIndex(new string[1] { "RespondedAtUtc" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantConfirmations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantContractTermination", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("BundleId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EffectiveAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("RequestedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "BundleId" });
			b.HasIndex(new string[1] { "ProgramId" });
			b.HasIndex(new string[1] { "RequestedAtUtc" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Status" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantContractTerminations", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantCustomRole", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("DescriptionEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NameEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantRegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoleId").IsRequired(true), "text");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantCustomRoles");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantDailyOrder", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("BoxCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConfirmedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("OrderId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReadyForPickupAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RejectedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RejectionReason").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedMenuDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedMenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("ScheduleMode"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("ServiceDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("SubscriptionDayId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("xmin").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "OrderId" });
			b.HasIndex(new string[1] { "ReadyForPickupAtUtc" });
			b.HasIndex(new string[1] { "ResolvedMenuDayId" });
			b.HasIndex(new string[1] { "ResolvedMenuPlanVersionId" });
			b.HasIndex(new string[1] { "ResolvedProgramId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "ScheduleMode" });
			b.HasIndex(new string[1] { "ServiceDate" });
			b.HasIndex(new string[1] { "Status" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[2] { "RestaurantId", "ServiceDate" });
			b.HasIndex(new string[2] { "RestaurantId", "SubscriptionDayId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantDailyOrders", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantDriverLink", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DriverProfileId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "DriverProfileId" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "DriverProfileId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantDriverLinks", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantEmergencyOverride", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("EndDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsClosed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsResolved"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("ReducedCapacity"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ResolutionReason"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ResolvedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ResolvedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("StartDate"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TriggeredByUserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantEmergencyOverrides");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantOperatingDayException", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateOnly>(b.Property<DateOnly>("Date"), "date");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsWorkingDay"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Reason").HasMaxLength(1000), "character varying(1000)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "Date" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantOperatingDayExceptions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DayOfWeek"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsWorkingDay"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "DayOfWeek" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantOperatingSchedules", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantRating", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Comment").HasMaxLength(2000), "character varying(2000)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CustomerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Score"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("SubscriptionDayId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CustomerUserId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "Score" });
			b.HasIndex(new string[1] { "SubscriptionDayId" });
			b.HasIndex(new string[3] { "RestaurantId", "CustomerUserId", "SubscriptionDayId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantRatings", (string)null, (Action<TableBuilder>)delegate(TableBuilder t)
			{
				t.HasCheckConstraint("CK_RestaurantRatings_Score", "\"Score\" >= 1 AND \"Score\" <= 5");
			});
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantRolePermission", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ModuleKey").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PermissionLevel").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantRegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoleId").IsRequired(true), "text");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantRolePermissions");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantServiceRegion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<TimeOnly>(b.Property<TimeOnly>("DeliveryWindowEnd"), "time without time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<TimeOnly>(b.Property<TimeOnly>("DeliveryWindowStart"), "time without time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsActive"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("PreparationLeadTimeMinutes"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RegionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "IsActive" });
			b.HasIndex(new string[1] { "RegionId" });
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[2] { "RestaurantId", "RegionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RestaurantServiceRegions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.StaffInvitation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("AllBranches"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("BranchIdsJson"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Email").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameAr").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullNameEn").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InviteToken").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Phone").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RestaurantRegistrationId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("RoleId").IsRequired(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true), "text");
			b.HasKey(new string[1] { "Id" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "StaffInvitations");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.WeekdayMealAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("DayOfWeek"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsPrimary"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MealId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("MenuPlanVersionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ProgramId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Slot").IsRequired(true).HasMaxLength(20), "character varying(20)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("SortOrder"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "MealId" });
			b.HasIndex(new string[4] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "MealId" }).IsUnique(true);
			RelationalIndexBuilderExtensions.HasFilter(RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[5] { "MenuPlanVersionId", "DayOfWeek", "ProgramId", "Slot", "IsPrimary" }).IsUnique(true), "IX_WeekdayMealAssignments_MenuPlanVersionId_DayOfWeek_Program~1"), "\"IsPrimary\" = true");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "WeekdayMealAssignments", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.CallSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("AcceptedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CallerConnectedReportedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CallerDeviceSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("CallerLastHeartbeatAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CallerType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CallerUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ClientRequestId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConnectedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ConnectingAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("DurationSeconds"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int?>(b.Property<int?>("EndReason"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("EndedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("EndedByDeviceSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("EndedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("MediaLeaseExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ProtocolVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReceiverConnectedReportedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ReceiverDeviceSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReceiverLastHeartbeatAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ReceiverRingingAckAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("ReceiverType"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("ReceiverUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RingExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RingingAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Sequence"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("SetupExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("StartedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Status"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("TerminalEventId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("TripStopId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnName<uint>(RelationalPropertyBuilderExtensions.HasColumnType<uint>(b.Property<uint>("Version").IsConcurrencyToken(true).ValueGeneratedOnAddOrUpdate(), "xid"), "xmin");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CreatedAtUtc" });
			b.HasIndex(new string[1] { "TripStopId" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "CallerUserId", "ClientRequestId" }).IsUnique(true), "\"IsDeleted\" = FALSE");
			b.HasIndex(new string[2] { "CallerUserId", "Status" });
			b.HasIndex(new string[2] { "ReceiverUserId", "Status" });
			b.HasIndex(new string[2] { "Status", "AcceptedAtUtc" });
			b.HasIndex(new string[2] { "Status", "ConnectingAtUtc" });
			b.HasIndex(new string[2] { "Status", "RingingAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "CallSessions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallBusyParticipant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id"), "uuid");
			b.HasKey(new string[1] { "UserId" });
			b.HasIndex(new string[1] { "CallId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "VoiceCallBusyParticipants", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallDeviceReceipt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("DeviceSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("LastTerminalAckSequence"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RingingAckAtUtc"), "timestamp with time zone");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[2] { "CallId", "DeviceSessionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "VoiceCallDeviceReceipts", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallDeviceSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ApnsVoipToken").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("CapabilityVersion"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ControlProofHash").IsRequired(true).HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("ExpiresAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("FcmDeviceTokenId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("InstallationId").IsRequired(true).HasMaxLength(128), "character varying(128)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("LastSeenAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Platform").IsRequired(true).HasMaxLength(16), "character varying(16)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("RevokedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "ControlProofHash" }).IsUnique(true);
			b.HasIndex(new string[1] { "ExpiresAtUtc" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[2] { "UserId", "InstallationId" }).IsUnique(true), "\"RevokedAtUtc\" IS NULL");
			b.HasIndex(new string[2] { "UserId", "RevokedAtUtc" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "VoiceCallDeviceSessions", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallEventDelivery", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AttemptCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("CallId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Channel").IsRequired(true).HasMaxLength(16), "character varying(16)");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("CreatedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("DeletedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("DeletedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("EventId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("IsDeleted"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("ModifiedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("ModifiedByUserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("NextAttemptAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderMessageId").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ReasonCode").HasMaxLength(64), "character varying(64)");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RecipientDeviceSessionId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<long>(b.Property<long>("Sequence"), "bigint");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Status").IsRequired(true).HasMaxLength(24), "character varying(24)");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "CallId" });
			b.HasIndex(new string[2] { "Status", "NextAttemptAtUtc" });
			b.HasIndex(new string[3] { "EventId", "Channel", "RecipientDeviceSessionId" }).IsUnique(true);
			RelationalEntityTypeBuilderExtensions.ToTable(b, "VoiceCallEventDeliveries", (string)null);
		});
		modelBuilder.Entity("MealMate.Persistence.Identity.ApplicationRole", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ConcurrencyStamp").IsConcurrencyToken(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Description"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NormalizedName").HasMaxLength(256), "character varying(256)");
			b.HasKey(new string[1] { "Id" });
			RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "NormalizedName" }).IsUnique(true), "RoleNameIndex");
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Roles", (string)null);
		});
		modelBuilder.Entity("MealMate.Persistence.Identity.ApplicationUser", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("Id").ValueGeneratedOnAdd(), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AccessFailedCount"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("AccountStatus"), "integer");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ConcurrencyStamp").IsConcurrencyToken(true), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime>(b.Property<DateTime>("CreatedAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Email").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("EmailConfirmed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("FullName").HasMaxLength(200), "character varying(200)");
			RelationalPropertyBuilderExtensions.HasDefaultValue<bool>(RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("HasSetPassword").ValueGeneratedOnAdd(), "boolean"), (object)false);
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("LastLoginAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("LockoutEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTimeOffset?>(b.Property<DateTimeOffset?>("LockoutEnd"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NormalizedEmail").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("NormalizedUserName").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PasswordHash"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<DateTime?>(b.Property<DateTime?>("PasswordSetAtUtc"), "timestamp with time zone");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("PhoneNumber"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("PhoneNumberConfirmed"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid?>(b.Property<Guid?>("RestaurantId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("SecurityStamp"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<bool>(b.Property<bool>("TwoFactorEnabled"), "boolean");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("UserName").HasMaxLength(256), "character varying(256)");
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("UserType"), "integer");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "AccountStatus" });
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "Email" }).IsUnique(true), "\"Email\" IS NOT NULL");
			RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "NormalizedEmail" }), "EmailIndex");
			RelationalIndexBuilderExtensions.HasDatabaseName(b.HasIndex(new string[1] { "NormalizedUserName" }).IsUnique(true), "UserNameIndex");
			RelationalIndexBuilderExtensions.HasFilter(b.HasIndex(new string[1] { "PhoneNumber" }).IsUnique(true), "\"PhoneNumber\" IS NOT NULL");
			b.HasIndex(new string[1] { "RestaurantId" });
			b.HasIndex(new string[1] { "UserType" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "Users", (string)null);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Id").ValueGeneratedOnAdd(), "integer");
			NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn<int>(b.Property<int>("Id"));
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ClaimType"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ClaimValue"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RoleId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "RoleId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "RoleClaims", (string)null);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<int>(b.Property<int>("Id").ValueGeneratedOnAdd(), "integer");
			NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn<int>(b.Property<int>("Id"));
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ClaimType"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ClaimValue"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[1] { "Id" });
			b.HasIndex(new string[1] { "UserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UserClaims", (string)null);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LoginProvider"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderKey"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("ProviderDisplayName"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			b.HasKey(new string[2] { "LoginProvider", "ProviderKey" });
			b.HasIndex(new string[1] { "UserId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UserLogins", (string)null);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("RoleId"), "uuid");
			b.HasKey(new string[2] { "UserId", "RoleId" });
			b.HasIndex(new string[1] { "RoleId" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UserRoles", (string)null);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			RelationalPropertyBuilderExtensions.HasColumnType<Guid>(b.Property<Guid>("UserId"), "uuid");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("LoginProvider"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Name"), "text");
			RelationalPropertyBuilderExtensions.HasColumnType<string>(b.Property<string>("Value"), "text");
			b.HasKey(new string[3] { "UserId", "LoginProvider", "Name" });
			RelationalEntityTypeBuilderExtensions.ToTable(b, "UserTokens", (string)null);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdAuction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Advertising.AdSpace", "AdSpace").WithMany((string)null).HasForeignKey(new string[1] { "AdSpaceId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("AdSpace");
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdBid", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Advertising.AdAuction", "AdAuction").WithMany("Bids").HasForeignKey(new string[1] { "AdAuctionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("AdAuction");
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdPlacement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Advertising.AdAuction", "AdAuction").WithOne((string)null).HasForeignKey("MealMate.Domain.Advertising.AdPlacement", new string[1] { "AdAuctionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("AdAuction");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.DailyMealSelection", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CalendarMeals.SubscriptionDay", "SubscriptionDay").WithMany("MealSelections").HasForeignKey(new string[1] { "SubscriptionDayId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("SubscriptionDay");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.DailyRestaurantSelection", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CalendarMeals.SubscriptionDay", "SubscriptionDay").WithMany("RestaurantSelections").HasForeignKey(new string[1] { "SubscriptionDayId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("SubscriptionDay");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.FreezeLedgerEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CalendarMeals.SubscriptionFreeze", "Freeze").WithMany("LedgerEntries").HasForeignKey(new string[1] { "FreezeId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Freeze");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.MealChangeRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CalendarMeals.SubscriptionDay", "SubscriptionDay").WithMany("MealChangeRequests").HasForeignKey(new string[1] { "SubscriptionDayId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("SubscriptionDay");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CalendarMeals.SubscriptionCalendar", "Calendar").WithMany("Days").HasForeignKey(new string[1] { "CalendarId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilySubscriptionDeliveryAmendment", "SourceFamilyDeliveryAmendment").WithMany((string)null).HasForeignKey(new string[1] { "SourceFamilyDeliveryAmendmentId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("Calendar");
			b.Navigation("SourceFamilyDeliveryAmendment");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.ChartOfAccount", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CentralAccounting.ChartOfAccount", "Parent").WithMany("Children").HasForeignKey(new string[1] { "ParentId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("Parent");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.DailyRevenueRecognition", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CentralAccounting.JournalEntry", "JournalEntry").WithMany((string)null).HasForeignKey(new string[1] { "JournalEntryId" })
				.OnDelete((DeleteBehavior)1);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("DailyRevenueRecognitionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "DailyRevenueRecognitionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "DailyRevenueRecognitions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "DailyRevenueRecognitionId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("JournalEntry");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.DeferredRevenue", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "OriginalAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("DeferredRevenueId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "OriginalAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "OriginalAmount_CurrencyCode");
				val.HasKey(new string[1] { "DeferredRevenueId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "DeferredRevenues");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "DeferredRevenueId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "RemainingAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("DeferredRevenueId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "RemainingAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "RemainingAmount_CurrencyCode");
				val.HasKey(new string[1] { "DeferredRevenueId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "DeferredRevenues");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "DeferredRevenueId" });
			});
			b.Navigation("OriginalAmount").IsRequired(true);
			b.Navigation("RemainingAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.FinancialSettlement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "GrossTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("FinancialSettlementId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "GrossTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "GrossTotal_CurrencyCode");
				val.HasKey(new string[1] { "FinancialSettlementId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "FinancialSettlements");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "FinancialSettlementId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "NetTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("FinancialSettlementId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "NetTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "NetTotal_CurrencyCode");
				val.HasKey(new string[1] { "FinancialSettlementId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "FinancialSettlements");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "FinancialSettlementId" });
			});
			b.Navigation("GrossTotal").IsRequired(true);
			b.Navigation("NetTotal").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.InfluencerPayableLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CommissionAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerPayableLedgerId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CommissionAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CommissionAmount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerPayableLedgerId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerPayableLedgers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerPayableLedgerId" });
			});
			b.Navigation("CommissionAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.JournalEntryLine", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CentralAccounting.ChartOfAccount", "Account").WithMany("JournalEntryLines").HasForeignKey(new string[1] { "AccountId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.CentralAccounting.JournalEntry", "JournalEntry").WithMany("Lines").HasForeignKey(new string[1] { "JournalEntryId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Credit", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("JournalEntryLineId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Credit_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Credit_CurrencyCode");
				val.HasKey(new string[1] { "JournalEntryLineId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "JournalEntryLines");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "JournalEntryLineId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Debit", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("JournalEntryLineId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Debit_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Debit_CurrencyCode");
				val.HasKey(new string[1] { "JournalEntryLineId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "JournalEntryLines");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "JournalEntryLineId" });
			});
			b.Navigation("Account");
			b.Navigation("Credit");
			b.Navigation("Debit");
			b.Navigation("JournalEntry");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.OperatingExpense", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OperatingExpenseId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "OperatingExpenseId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "OperatingExpenses");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OperatingExpenseId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.PaymentGatewayConfig", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "FixedFee", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("PaymentGatewayConfigId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "FixedFee_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "FixedFee_CurrencyCode");
				val.HasKey(new string[1] { "PaymentGatewayConfigId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "PaymentGatewayConfigs");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "PaymentGatewayConfigId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "MaxFee", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("PaymentGatewayConfigId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "MaxFee_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "MaxFee_CurrencyCode");
				val.HasKey(new string[1] { "PaymentGatewayConfigId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "PaymentGatewayConfigs");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "PaymentGatewayConfigId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "MinFee", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("PaymentGatewayConfigId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "MinFee_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "MinFee_CurrencyCode");
				val.HasKey(new string[1] { "PaymentGatewayConfigId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "PaymentGatewayConfigs");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "PaymentGatewayConfigId" });
			});
			b.Navigation("FixedFee");
			b.Navigation("MaxFee");
			b.Navigation("MinFee");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.RestaurantPayableLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CampaignContribution", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPayableLedgerId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CampaignContribution_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CampaignContribution_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPayableLedgerId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPayableLedgers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPayableLedgerId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CommissionAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPayableLedgerId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CommissionAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CommissionAmount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPayableLedgerId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPayableLedgers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPayableLedgerId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "GrossAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPayableLedgerId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "GrossAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "GrossAmount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPayableLedgerId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPayableLedgers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPayableLedgerId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "NetAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPayableLedgerId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "NetAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "NetAmount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPayableLedgerId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPayableLedgers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPayableLedgerId" });
			});
			b.Navigation("CampaignContribution");
			b.Navigation("CommissionAmount").IsRequired(true);
			b.Navigation("GrossAmount").IsRequired(true);
			b.Navigation("NetAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignAccountingEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("AccountingEntries").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "BaseSupplyGross", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "BaseSupplyGross_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "BaseSupplyGross_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CampaignSupplyGross", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CampaignSupplyGross_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CampaignSupplyGross_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Commission", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Commission_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Commission_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CustomerDiscount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CustomerDiscount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CustomerDiscount_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CustomerPriceAfter", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CustomerPriceAfter_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CustomerPriceAfter_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CustomerPriceBefore", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CustomerPriceBefore_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CustomerPriceBefore_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "MealMateFundingNet", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "MealMateFundingNet_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "MealMateFundingNet_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ReferenceDailyBox", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ReferenceDailyBox_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ReferenceDailyBox_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "RestaurantFundingNet", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "RestaurantFundingNet_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "RestaurantFundingNet_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "RestaurantNet", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignAccountingEntryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "RestaurantNet_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "RestaurantNet_CurrencyCode");
				val.HasKey(new string[1] { "CampaignAccountingEntryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignAccountingEntries");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignAccountingEntryId" });
			});
			b.Navigation("BaseSupplyGross").IsRequired(true);
			b.Navigation("Campaign");
			b.Navigation("CampaignSupplyGross");
			b.Navigation("Commission").IsRequired(true);
			b.Navigation("CustomerDiscount").IsRequired(true);
			b.Navigation("CustomerPriceAfter").IsRequired(true);
			b.Navigation("CustomerPriceBefore").IsRequired(true);
			b.Navigation("MealMateFundingNet").IsRequired(true);
			b.Navigation("ReferenceDailyBox").IsRequired(true);
			b.Navigation("RestaurantFundingNet").IsRequired(true);
			b.Navigation("RestaurantNet").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignAuditLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany((string)null).HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignBundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("Bundles").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignBundleDurationPrice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.CampaignBundle", "CampaignBundle").WithMany("DurationPrices").HasForeignKey(new string[1] { "CampaignBundleId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ReferenceCustomerPrice", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CampaignBundleDurationPriceId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ReferenceCustomerPrice_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ReferenceCustomerPrice_CurrencyCode");
				val.HasKey(new string[1] { "CampaignBundleDurationPriceId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CampaignBundleDurationPrices");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CampaignBundleDurationPriceId" });
			});
			b.Navigation("CampaignBundle");
			b.Navigation("ReferenceCustomerPrice").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignCapacityReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("CapacityReviews").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany((string)null).HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignDiscountSplit", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("DiscountSplits").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignFinancialLedger", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany((string)null).HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignPaymentTransaction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession", (string)null).WithMany("PaymentTransactions").HasForeignKey(new string[1] { "CampaignCheckoutSessionId" });
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignProfitabilitySnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany((string)null).HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("Programs").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignReservationAllocation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession", "CheckoutSession").WithMany("Allocations").HasForeignKey(new string[1] { "CheckoutSessionId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("CheckoutSession");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantApproval", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantEnrollment", "Enrollment").WithMany("Approvals").HasForeignKey(new string[1] { "EnrollmentId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Enrollment");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantCapacity", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("RestaurantCapacities").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantEnrollment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("RestaurantEnrollments").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignSubscriberCap", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany("SubscriberCaps").HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignTierSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CollaborativeCampaigns.Campaign", "Campaign").WithMany((string)null).HasForeignKey(new string[1] { "CampaignId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Campaign");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.AdminComplaintReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("AdminReviews").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ComplaintClassification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("Classifications").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ComplaintEvidencePhoto", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("EvidencePhotos").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.CustomerCompensation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("Compensations").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CustomerCompensationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "CustomerCompensationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CustomerCompensations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CustomerCompensationId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "PromoCreditAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CustomerCompensationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "PromoCreditAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "PromoCreditAmount_CurrencyCode");
				val.HasKey(new string[1] { "CustomerCompensationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CustomerCompensations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CustomerCompensationId" });
			});
			b.Navigation("Amount");
			b.Navigation("Complaint");
			b.Navigation("PromoCreditAmount");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.FreeDayCompensation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("FreeDayCompensations").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.ResponsibilityDecision", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("ResponsibilityDecisions").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.RestaurantBoxDeduction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("BoxDeductions").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantBoxDeductionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantBoxDeductionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantBoxDeductions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantBoxDeductionId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.RestaurantComplaintResponse", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ComplaintsSupport.Complaint", "Complaint").WithMany("RestaurantResponses").HasForeignKey(new string[1] { "ComplaintId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Complaint");
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CancellationFeeRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CustomerFinance.SubscriptionCancellation", "Cancellation").WithMany("FeeRecords").HasForeignKey(new string[1] { "CancellationId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "FeeAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CancellationFeeRecordId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "FeeAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "FeeAmount_CurrencyCode");
				val.HasKey(new string[1] { "CancellationFeeRecordId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CancellationFeeRecords");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CancellationFeeRecordId" });
			});
			b.Navigation("Cancellation");
			b.Navigation("FeeAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerCashWallet", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Balance", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CustomerCashWalletId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Balance_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Balance_CurrencyCode");
				val.HasKey(new string[1] { "CustomerCashWalletId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CustomerCashWallets");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CustomerCashWalletId" });
			});
			b.Navigation("Balance").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerPromotionalWallet", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Balance", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CustomerPromotionalWalletId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Balance_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Balance_CurrencyCode");
				val.HasKey(new string[1] { "CustomerPromotionalWalletId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CustomerPromotionalWallets");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CustomerPromotionalWalletId" });
			});
			b.Navigation("Balance").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.CustomerWithdrawalRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("CustomerWithdrawalRequestId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "CustomerWithdrawalRequestId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "CustomerWithdrawalRequests");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "CustomerWithdrawalRequestId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.RefundCalculation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.CustomerFinance.SubscriptionCancellation", "Cancellation").WithMany("RefundCalculations").HasForeignKey(new string[1] { "CancellationId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CancellationFee", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RefundCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CancellationFee_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CancellationFee_CurrencyCode");
				val.HasKey(new string[1] { "RefundCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RefundCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RefundCalculationId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "FinalCashRefund", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RefundCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "FinalCashRefund_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "FinalCashRefund_CurrencyCode");
				val.HasKey(new string[1] { "RefundCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RefundCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RefundCalculationId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "RefundEligiblePaid", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RefundCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "RefundEligiblePaid_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "RefundEligiblePaid_CurrencyCode");
				val.HasKey(new string[1] { "RefundCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RefundCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RefundCalculationId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "UnusedCashValue", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RefundCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "UnusedCashValue_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "UnusedCashValue_CurrencyCode");
				val.HasKey(new string[1] { "RefundCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RefundCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RefundCalculationId" });
			});
			b.Navigation("Cancellation");
			b.Navigation("CancellationFee").IsRequired(true);
			b.Navigation("FinalCashRefund").IsRequired(true);
			b.Navigation("RefundEligiblePaid").IsRequired(true);
			b.Navigation("UnusedCashValue").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.SubscriptionPayment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("SubscriptionPaymentId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "SubscriptionPaymentId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "SubscriptionPayments");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "SubscriptionPaymentId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.WalletTransaction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("WalletTransactionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "WalletTransactionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "WalletTransactions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "WalletTransactionId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "BalanceAfter", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("WalletTransactionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "BalanceAfter_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "BalanceAfter_CurrencyCode");
				val.HasKey(new string[1] { "WalletTransactionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "WalletTransactions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "WalletTransactionId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("BalanceAfter").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.CustomerPhoneRevealGrant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.DeliveryContactCase", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "ContactCaseId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryContactAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.VoiceCalls.CallSession", (string)null).WithOne((string)null).HasForeignKey("MealMate.Domain.DriverDelivery.DeliveryContactAttempt", new string[1] { "CallId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryHold", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.Trip", "Trip").WithMany((string)null).HasForeignKey(new string[1] { "TripId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("Trip");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DeliveryTrackingPoint", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.Trip", "Trip").WithMany("TrackingPoints").HasForeignKey(new string[1] { "TripId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Trip");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.DriverAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.Trip", "Trip").WithMany("DriverAssignments").HasForeignKey(new string[1] { "TripId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Trip");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.PickupScan", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.Trip", "Trip").WithMany("PickupScans").HasForeignKey(new string[1] { "TripId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Trip");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.TripStop", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.MealBarcode", "MealBarcode").WithMany((string)null).HasForeignKey(new string[1] { "MealBarcodeId" })
				.OnDelete((DeleteBehavior)2);
			b.HasOne("MealMate.Domain.DriverDelivery.Trip", "Trip").WithMany("Stops").HasForeignKey(new string[1] { "TripId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("MealBarcode");
			b.Navigation("Trip");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.DocumentVerification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.Document", "Document").WithMany("Verifications").HasForeignKey(new string[1] { "DocumentId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Document");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Region", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.Country", "Country").WithMany("Regions").HasForeignKey(new string[1] { "CountryId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.Foundation.Region", "ParentRegion").WithMany("ChildRegions").HasForeignKey(new string[1] { "ParentRegionId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("Country");
			b.Navigation("ParentRegion");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.RuleVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.RuleVersion", "PreviousVersion").WithMany((string)null).HasForeignKey(new string[1] { "PreviousVersionId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("PreviousVersion");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Tenant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.Country", "Country").WithMany((string)null).HasForeignKey(new string[1] { "CountryId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Country");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AccessScope", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.Tenant", "Tenant").WithMany((string)null).HasForeignKey(new string[1] { "TenantId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("Tenant");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AdminInvitation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.IdentityAccess.AccessScope", "AccessScope").WithMany((string)null).HasForeignKey(new string[1] { "AccessScopeId" })
				.OnDelete((DeleteBehavior)2);
			b.Navigation("AccessScope");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AuthLoginChallenge", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationUser", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "UserId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantRegistrationFieldReview", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.IdentityAccess.RestaurantRegistration", "Registration").WithMany("FieldReviews").HasForeignKey(new string[1] { "RegistrationId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Registration");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RolePermission", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.IdentityAccess.Permission", "Permission").WithMany("RolePermissions").HasForeignKey(new string[1] { "PermissionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Permission");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.UserScope", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.IdentityAccess.AccessScope", "AccessScope").WithMany("UserScopes").HasForeignKey(new string[1] { "AccessScopeId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("AccessScope");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCancellationImpact", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Influencers.InfluencerCommissionCalculation", "CommissionCalculation").WithMany("CancellationImpacts").HasForeignKey(new string[1] { "CommissionCalculationId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ReversedAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerCancellationImpactId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ReversedAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ReversedAmount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerCancellationImpactId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerCancellationImpacts");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerCancellationImpactId" });
			});
			b.Navigation("CommissionCalculation");
			b.Navigation("ReversedAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "DiscountAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerCodeId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "DiscountAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "DiscountAmount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerCodeId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerCodes");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerCodeId" });
			});
			b.Navigation("DiscountAmount");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCommissionCalculation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Influencers.InfluencerCode", "Code").WithMany((string)null).HasForeignKey(new string[1] { "CodeId" })
				.OnDelete((DeleteBehavior)1);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CommissionAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerCommissionCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CommissionAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CommissionAmount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerCommissionCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerCommissionCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerCommissionCalculationId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "PaidAmountAfterDiscount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerCommissionCalculationId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "PaidAmountAfterDiscount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "PaidAmountAfterDiscount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerCommissionCalculationId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerCommissionCalculations");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerCommissionCalculationId" });
			});
			b.Navigation("Code");
			b.Navigation("CommissionAmount").IsRequired(true);
			b.Navigation("PaidAmountAfterDiscount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerPayoutHistory", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Influencers.InfluencerWithdrawalRequest", "WithdrawalRequest").WithMany((string)null).HasForeignKey(new string[1] { "WithdrawalRequestId" })
				.OnDelete((DeleteBehavior)1);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerPayoutHistoryId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerPayoutHistoryId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerPayoutHistories");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerPayoutHistoryId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("WithdrawalRequest");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerWithdrawalApproval", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Influencers.InfluencerWithdrawalRequest", "WithdrawalRequest").WithMany("Approvals").HasForeignKey(new string[1] { "WithdrawalRequestId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("WithdrawalRequest");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerWithdrawalRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("InfluencerWithdrawalRequestId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "InfluencerWithdrawalRequestId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "InfluencerWithdrawalRequests");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "InfluencerWithdrawalRequestId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.Offer", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "DiscountAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OfferId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "DiscountAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "DiscountAmount_CurrencyCode");
				val.HasKey(new string[1] { "OfferId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "Offers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OfferId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "MinOrderAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OfferId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "MinOrderAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "MinOrderAmount_CurrencyCode");
				val.HasKey(new string[1] { "OfferId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "Offers");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OfferId" });
			});
			b.Navigation("DiscountAmount");
			b.Navigation("MinOrderAmount");
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferAuditLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Offers.Offer", "Offer").WithMany("AuditLogs").HasForeignKey(new string[1] { "OfferId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Offer");
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferBundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Offers.Offer", "Offer").WithMany("EligibleBundles").HasForeignKey(new string[1] { "OfferId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Offer");
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Offers.Offer", "Offer").WithMany("EligiblePrograms").HasForeignKey(new string[1] { "OfferId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Offer");
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferRedemption", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Offers.Offer", "Offer").WithMany("Redemptions").HasForeignKey(new string[1] { "OfferId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "DiscountAppliedAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OfferRedemptionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "DiscountAppliedAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "DiscountAppliedAmount_CurrencyCode");
				val.HasKey(new string[1] { "OfferRedemptionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "OfferRedemptions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OfferRedemptionId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "PlatformFundedAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OfferRedemptionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "PlatformFundedAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "PlatformFundedAmount_CurrencyCode");
				val.HasKey(new string[1] { "OfferRedemptionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "OfferRedemptions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OfferRedemptionId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "RestaurantFundedAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("OfferRedemptionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "RestaurantFundedAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "RestaurantFundedAmount_CurrencyCode");
				val.HasKey(new string[1] { "OfferRedemptionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "OfferRedemptions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "OfferRedemptionId" });
			});
			b.Navigation("DiscountAppliedAmount").IsRequired(true);
			b.Navigation("Offer");
			b.Navigation("PlatformFundedAmount").IsRequired(true);
			b.Navigation("RestaurantFundedAmount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.Offers.OfferRestaurant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Offers.Offer", "Offer").WithMany("EligibleRestaurants").HasForeignKey(new string[1] { "OfferId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Offer");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Bundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany("Bundles").HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Program");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.ClassificationRun", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", "Bundle").WithMany((string)null).HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany((string)null).HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Bundle");
			b.Navigation("Program");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.DiscountsAndReferralLog", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany("DiscountsAndReferralLogs").HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("DiscountsAndReferralLogId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "DiscountsAndReferralLogId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "DiscountsAndReferralLogs");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "DiscountsAndReferralLogId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyGroup", "FamilyGroup").WithMany((string)null).HasForeignKey(new string[1] { "FamilyGroupId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("FamilyGroup");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatchLine", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch", "Batch").WithMany("Lines").HasForeignKey(new string[1] { "BatchId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyMember", "FamilyMember").WithMany((string)null).HasForeignKey(new string[1] { "FamilyMemberId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)2);
			b.Navigation("Batch");
			b.Navigation("FamilyMember");
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutPaymentAttempt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch", "Batch").WithMany("PaymentAttempts").HasForeignKey(new string[1] { "BatchId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Batch");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMember", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyGroup", "FamilyGroup").WithMany("Members").HasForeignKey(new string[1] { "FamilyGroupId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "MemberSubscription").WithMany((string)null).HasForeignKey(new string[1] { "MemberSubscriptionId" })
				.OnDelete((DeleteBehavior)2);
			b.Navigation("FamilyGroup");
			b.Navigation("MemberSubscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMemberAccess", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyMember", "FamilyMember").WithOne((string)null).HasForeignKey("MealMate.Domain.ProgramsSubscriptions.FamilyMemberAccess", new string[1] { "FamilyMemberId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("FamilyMember");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyMemberPermissions", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyMember", "FamilyMember").WithOne((string)null).HasForeignKey("MealMate.Domain.ProgramsSubscriptions.FamilyMemberPermissions", new string[1] { "FamilyMemberId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("FamilyMember");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilySubscription", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "PrimarySubscription").WithMany((string)null).HasForeignKey(new string[1] { "PrimarySubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("PrimarySubscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilySubscriptionDeliveryAmendment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PackageContentVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", "Bundle").WithMany("ContentVersions").HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany("ContentVersions").HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Bundle");
			b.Navigation("Program");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PricingRuleVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.Foundation.RuleVersion", "RuleVersion").WithMany((string)null).HasForeignKey(new string[1] { "RuleVersionId" })
				.OnDelete((DeleteBehavior)1);
			b.Navigation("RuleVersion");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.PromoCode", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "DiscountAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("PromoCodeId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "DiscountAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "DiscountAmount_CurrencyCode");
				val.HasKey(new string[1] { "PromoCodeId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "PromoCodes");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "PromoCodeId" });
			});
			b.Navigation("DiscountAmount");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantBundlePricing", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", "Bundle").WithMany((string)null).HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.RestaurantBundlePricing", "PreviousPricing").WithMany((string)null).HasForeignKey(new string[1] { "PreviousPricingId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany((string)null).HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ContractedSupplyPrice26", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantBundlePricingId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ContractedSupplyPrice26_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ContractedSupplyPrice26_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantBundlePricingId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantBundlePricings");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantBundlePricingId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ReferencePrice26", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantBundlePricingId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ReferencePrice26_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ReferencePrice26_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantBundlePricingId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantBundlePricings");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantBundlePricingId" });
			});
			b.Navigation("Bundle");
			b.Navigation("ContractedSupplyPrice26").IsRequired(true);
			b.Navigation("PreviousPricing");
			b.Navigation("Program");
			b.Navigation("ReferencePrice26").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantClassification", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", "Bundle").WithMany((string)null).HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.ClassificationRun", "ClassificationRun").WithMany("Classifications").HasForeignKey(new string[1] { "ClassificationRunId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany((string)null).HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Bundle");
			b.Navigation("ClassificationRun");
			b.Navigation("Program");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.RestaurantProgramBundleOffer", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Subscription", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Bundle", "Bundle").WithMany((string)null).HasForeignKey(new string[1] { "BundleId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyGroup", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "FamilyGroupId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.FamilyMember", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "FamilyMemberId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.PackageContentVersion", "PackageContentVersion").WithMany((string)null).HasForeignKey(new string[1] { "PackageContentVersionId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.PricingRuleVersion", "PricingRuleVersion").WithMany((string)null).HasForeignKey(new string[1] { "PricingRuleVersionId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Program", "Program").WithMany((string)null).HasForeignKey(new string[1] { "ProgramId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.PromoCode", "PromoCode").WithMany((string)null).HasForeignKey(new string[1] { "PromoCodeId" })
				.OnDelete((DeleteBehavior)1);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "PaidAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("SubscriptionId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "PaidAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "PaidAmount_CurrencyCode");
				val.HasKey(new string[1] { "SubscriptionId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "Subscriptions");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "SubscriptionId" });
			});
			b.Navigation("Bundle");
			b.Navigation("PackageContentVersion");
			b.Navigation("PaidAmount").IsRequired(true);
			b.Navigation("PricingRuleVersion");
			b.Navigation("Program");
			b.Navigation("PromoCode");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionFreezeRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionOperation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany("Operations").HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRenewal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "NewSubscription").WithMany((string)null).HasForeignKey(new string[1] { "NewSubscriptionId" })
				.OnDelete((DeleteBehavior)1);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "PreviousSubscription").WithMany((string)null).HasForeignKey(new string[1] { "PreviousSubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("NewSubscription");
			b.Navigation("PreviousSubscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantDaySnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "RestaurantSnapshotId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantPoolSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.SubscriptionRestaurantPoolSnapshot", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "PoolSnapshotId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionSnapshot", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany("Snapshots").HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionSnapshotMigrationIssue", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.SubscriptionTierUpgrade", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.ProgramsSubscriptions.Subscription", "Subscription").WithMany((string)null).HasForeignKey(new string[1] { "SubscriptionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "PriceDifference", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("SubscriptionTierUpgradeId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "PriceDifference_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "PriceDifference_CurrencyCode");
				val.HasKey(new string[1] { "SubscriptionTierUpgradeId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "SubscriptionTierUpgrades");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "SubscriptionTierUpgradeId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "QuotedTotalDue", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("SubscriptionTierUpgradeId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "QuotedTotalDue_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "QuotedTotalDue_CurrencyCode");
				val.HasKey(new string[1] { "SubscriptionTierUpgradeId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "SubscriptionTierUpgrades");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "SubscriptionTierUpgradeId" });
			});
			b.Navigation("PriceDifference").IsRequired(true);
			b.Navigation("QuotedTotalDue").IsRequired(true);
			b.Navigation("Subscription");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.ComplaintDeductionRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("ComplaintDeductionRecordId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "ComplaintDeductionRecordId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "ComplaintDeductionRecords");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "ComplaintDeductionRecordId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.MealPayable", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CampaignContributionNet", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("MealPayableId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CampaignContributionNet_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CampaignContributionNet_CurrencyCode");
				val.HasKey(new string[1] { "MealPayableId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "MealPayables");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "MealPayableId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Commission", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("MealPayableId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Commission_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Commission_CurrencyCode");
				val.HasKey(new string[1] { "MealPayableId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "MealPayables");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "MealPayableId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "GrossSupply", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("MealPayableId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "GrossSupply_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "GrossSupply_CurrencyCode");
				val.HasKey(new string[1] { "MealPayableId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "MealPayables");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "MealPayableId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "NetPayable", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("MealPayableId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "NetPayable_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "NetPayable_CurrencyCode");
				val.HasKey(new string[1] { "MealPayableId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "MealPayables");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "MealPayableId" });
			});
			b.Navigation("CampaignContributionNet");
			b.Navigation("Commission").IsRequired(true);
			b.Navigation("GrossSupply").IsRequired(true);
			b.Navigation("NetPayable").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantAccountStatement", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "ClosingBalance", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantAccountStatementId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "ClosingBalance_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "ClosingBalance_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantAccountStatementId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantAccountStatements");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantAccountStatementId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "OpeningBalance", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantAccountStatementId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "OpeningBalance_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "OpeningBalance_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantAccountStatementId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantAccountStatements");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantAccountStatementId" });
			});
			b.Navigation("ClosingBalance").IsRequired(true);
			b.Navigation("OpeningBalance").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantCommissionRecord", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CommissionAmount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantCommissionRecordId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CommissionAmount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CommissionAmount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantCommissionRecordId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantCommissionRecords");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantCommissionRecordId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "GrossSupply", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantCommissionRecordId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "GrossSupply_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "GrossSupply_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantCommissionRecordId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantCommissionRecords");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantCommissionRecordId" });
			});
			b.Navigation("CommissionAmount").IsRequired(true);
			b.Navigation("GrossSupply").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantMonthlyInvoice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "CommissionTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantMonthlyInvoiceId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "CommissionTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "CommissionTotal_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantMonthlyInvoiceId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantMonthlyInvoices");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantMonthlyInvoiceId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "DeductionsTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantMonthlyInvoiceId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "DeductionsTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "DeductionsTotal_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantMonthlyInvoiceId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantMonthlyInvoices");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantMonthlyInvoiceId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "GrossTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantMonthlyInvoiceId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "GrossTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "GrossTotal_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantMonthlyInvoiceId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantMonthlyInvoices");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantMonthlyInvoiceId" });
			});
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "NetTotal", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantMonthlyInvoiceId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "NetTotal_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "NetTotal_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantMonthlyInvoiceId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantMonthlyInvoices");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantMonthlyInvoiceId" });
			});
			b.Navigation("CommissionTotal").IsRequired(true);
			b.Navigation("DeductionsTotal").IsRequired(true);
			b.Navigation("GrossTotal").IsRequired(true);
			b.Navigation("NetTotal").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantPartialPayment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantFinance.RestaurantMonthlyInvoice", "MonthlyInvoice").WithMany("PartialPayments").HasForeignKey(new string[1] { "MonthlyInvoiceId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPartialPaymentId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPartialPaymentId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPartialPayments");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPartialPaymentId" });
			});
			b.Navigation("Amount").IsRequired(true);
			b.Navigation("MonthlyInvoice");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantPayout", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.OwnsOne("MealMate.Domain.ValueObjects.Money", "Amount", (Action<OwnedNavigationBuilder>)delegate(OwnedNavigationBuilder val)
			{
				RelationalPropertyBuilderExtensions.HasColumnType<Guid>(val.Property<Guid>("RestaurantPayoutId"), "uuid");
				RelationalPropertyBuilderExtensions.HasColumnName<decimal>(RelationalPropertyBuilderExtensions.HasColumnType<decimal>(val.Property<decimal>("Amount").HasPrecision(18, 3), "numeric(18,3)"), "Amount_Amount");
				RelationalPropertyBuilderExtensions.HasColumnName<string>(RelationalPropertyBuilderExtensions.HasColumnType<string>(val.Property<string>("CurrencyCode").IsRequired(true).HasMaxLength(3), "character varying(3)"), "Amount_CurrencyCode");
				val.HasKey(new string[1] { "RestaurantPayoutId" });
				RelationalEntityTypeBuilderExtensions.ToTable(val, "RestaurantPayouts");
				val.WithOwner((string)null).HasForeignKey(new string[1] { "RestaurantPayoutId" });
			});
			b.Navigation("Amount").IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.TransferReceipt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantFinance.RestaurantPayout", "Payout").WithMany("TransferReceipts").HasForeignKey(new string[1] { "PayoutId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Payout");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Meal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Menu", "Menu").WithMany("Meals").HasForeignKey(new string[1] { "MenuId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Menu");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealAllergen", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("Allergens").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Meal");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealImage", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("Images").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Meal");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealIngredient", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Ingredient", "Ingredient").WithMany("MealIngredients").HasForeignKey(new string[1] { "IngredientId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("Ingredients").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.Navigation("Ingredient");
			b.Navigation("Meal");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealProgramAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("ProgramAssignments").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Meal");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MealRating", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("Ratings").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Meal");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.MenuPlanVersion", "MenuPlanVersion").WithMany("Days").HasForeignKey(new string[1] { "MenuPlanVersionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("MenuPlanVersion");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuDayMeal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("MenuDayMeals").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.RestaurantOperations.MenuDay", "MenuDay").WithMany("Meals").HasForeignKey(new string[1] { "MenuDayId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Meal");
			b.Navigation("MenuDay");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanProgram", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.MenuPlan", "MenuPlan").WithMany("Programs").HasForeignKey(new string[1] { "MenuPlanId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("MenuPlan");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.MenuPlan", "MenuPlan").WithMany("Versions").HasForeignKey(new string[1] { "MenuPlanId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("MenuPlan");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.OrderHandover", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.RestaurantDailyOrder", "DailyOrder").WithMany("Handovers").HasForeignKey(new string[1] { "DailyOrderId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("DailyOrder");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.OrderHandoverRevocation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.OrderHandover", "Handover").WithMany("Revocations").HasForeignKey(new string[1] { "HandoverId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Handover");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantConfirmation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.RestaurantDailyOrder", "DailyOrder").WithMany("Confirmations").HasForeignKey(new string[1] { "DailyOrderId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("DailyOrder");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.WeekdayMealAssignment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.RestaurantOperations.Meal", "Meal").WithMany("WeekdayAssignments").HasForeignKey(new string[1] { "MealId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.HasOne("MealMate.Domain.RestaurantOperations.MenuPlanVersion", "MenuPlanVersion").WithMany("WeekdayAssignments").HasForeignKey(new string[1] { "MenuPlanVersionId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("Meal");
			b.Navigation("MenuPlanVersion");
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.CallSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.DriverDelivery.TripStop", "TripStop").WithMany((string)null).HasForeignKey(new string[1] { "TripStopId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
			b.Navigation("TripStop");
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallBusyParticipant", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.VoiceCalls.CallSession", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "CallId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallDeviceReceipt", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.VoiceCalls.CallSession", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "CallId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.VoiceCalls.VoiceCallEventDelivery", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Domain.VoiceCalls.CallSession", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "CallId" })
				.OnDelete((DeleteBehavior)1)
				.IsRequired(true);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationRole", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "RoleId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationUser", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "UserId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationUser", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "UserId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationRole", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "RoleId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
			b.HasOne("MealMate.Persistence.Identity.ApplicationUser", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "UserId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<System.Guid>", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.HasOne("MealMate.Persistence.Identity.ApplicationUser", (string)null).WithMany((string)null).HasForeignKey(new string[1] { "UserId" })
				.OnDelete((DeleteBehavior)3)
				.IsRequired(true);
		});
		modelBuilder.Entity("MealMate.Domain.Advertising.AdAuction", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Bids");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionCalendar", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Days");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("MealChangeRequests");
			b.Navigation("MealSelections");
			b.Navigation("RestaurantSelections");
		});
		modelBuilder.Entity("MealMate.Domain.CalendarMeals.SubscriptionFreeze", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("LedgerEntries");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.ChartOfAccount", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Children");
			b.Navigation("JournalEntryLines");
		});
		modelBuilder.Entity("MealMate.Domain.CentralAccounting.JournalEntry", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Lines");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.Campaign", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("AccountingEntries");
			b.Navigation("Bundles");
			b.Navigation("CapacityReviews");
			b.Navigation("DiscountSplits");
			b.Navigation("Programs");
			b.Navigation("RestaurantCapacities");
			b.Navigation("RestaurantEnrollments");
			b.Navigation("SubscriberCaps");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignBundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("DurationPrices");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignCheckoutSession", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Allocations");
			b.Navigation("PaymentTransactions");
		});
		modelBuilder.Entity("MealMate.Domain.CollaborativeCampaigns.CampaignRestaurantEnrollment", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Approvals");
		});
		modelBuilder.Entity("MealMate.Domain.ComplaintsSupport.Complaint", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("AdminReviews");
			b.Navigation("BoxDeductions");
			b.Navigation("Classifications");
			b.Navigation("Compensations");
			b.Navigation("EvidencePhotos");
			b.Navigation("FreeDayCompensations");
			b.Navigation("ResponsibilityDecisions");
			b.Navigation("RestaurantResponses");
		});
		modelBuilder.Entity("MealMate.Domain.CustomerFinance.SubscriptionCancellation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("FeeRecords");
			b.Navigation("RefundCalculations");
		});
		modelBuilder.Entity("MealMate.Domain.DriverDelivery.Trip", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("DriverAssignments");
			b.Navigation("PickupScans");
			b.Navigation("Stops");
			b.Navigation("TrackingPoints");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Country", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Regions");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Document", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Verifications");
		});
		modelBuilder.Entity("MealMate.Domain.Foundation.Region", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("ChildRegions");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.AccessScope", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("UserScopes");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.Permission", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("RolePermissions");
		});
		modelBuilder.Entity("MealMate.Domain.IdentityAccess.RestaurantRegistration", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("FieldReviews");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerCommissionCalculation", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("CancellationImpacts");
		});
		modelBuilder.Entity("MealMate.Domain.Influencers.InfluencerWithdrawalRequest", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Approvals");
		});
		modelBuilder.Entity("MealMate.Domain.Offers.Offer", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("AuditLogs");
			b.Navigation("EligibleBundles");
			b.Navigation("EligiblePrograms");
			b.Navigation("EligibleRestaurants");
			b.Navigation("Redemptions");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Bundle", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("ContentVersions");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.ClassificationRun", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Classifications");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyCheckoutBatch", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Lines");
			b.Navigation("PaymentAttempts");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.FamilyGroup", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Members");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Program", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Bundles");
			b.Navigation("ContentVersions");
		});
		modelBuilder.Entity("MealMate.Domain.ProgramsSubscriptions.Subscription", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("DiscountsAndReferralLogs");
			b.Navigation("Operations");
			b.Navigation("Snapshots");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantMonthlyInvoice", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("PartialPayments");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantFinance.RestaurantPayout", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("TransferReceipts");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Ingredient", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("MealIngredients");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Meal", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Allergens");
			b.Navigation("Images");
			b.Navigation("Ingredients");
			b.Navigation("MenuDayMeals");
			b.Navigation("ProgramAssignments");
			b.Navigation("Ratings");
			b.Navigation("WeekdayAssignments");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.Menu", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Meals");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuDay", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Meals");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlan", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Programs");
			b.Navigation("Versions");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.MenuPlanVersion", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Days");
			b.Navigation("WeekdayAssignments");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.OrderHandover", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Revocations");
		});
		modelBuilder.Entity("MealMate.Domain.RestaurantOperations.RestaurantDailyOrder", (Action<EntityTypeBuilder>)delegate(EntityTypeBuilder b)
		{
			b.Navigation("Confirmations");
			b.Navigation("Handovers");
		});
	}
}
