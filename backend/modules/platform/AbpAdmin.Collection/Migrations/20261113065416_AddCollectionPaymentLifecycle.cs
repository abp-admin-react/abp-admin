using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChannelCreateLeaseId",
                table: "ColCollections",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ChannelCreateLeaseUntil",
                table: "ColCollections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ChannelOrderCreated",
                table: "ColCollections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ChannelPaymentMismatch",
                table: "ColCollections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CloseRequested",
                table: "ColCollections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MerchantAccountId",
                table: "ColCollections",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextChannelQueryAt",
                table: "ColCollections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ColCollectionRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantRefundNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MerchantAccountId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChannelRefundNo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChannelResultUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    StatusNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    SucceededAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NextSubmissionAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColCollectionRefunds", x => x.Id);
                    table.CheckConstraint("CK_ColCollectionRefunds_PositiveAmount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_ColCollectionRefunds_ColCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "ColCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ColPaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColPaymentEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColCollections_Channel_MerchantAccountId_ChannelTradeNo",
                table: "ColCollections",
                columns: new[] { "Channel", "MerchantAccountId", "ChannelTradeNo" },
                unique: true,
                filter: "\"Status\" = 1 AND \"MerchantAccountId\" IS NOT NULL AND \"ChannelTradeNo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColCollections_NextChannelQueryAt_CreationTime",
                table: "ColCollections",
                columns: new[] { "NextChannelQueryAt", "CreationTime" },
                filter: "\"Status\" = 0 AND \"MerchantAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColCollectionRefunds_CollectionId_RequestId",
                table: "ColCollectionRefunds",
                columns: new[] { "CollectionId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColCollectionRefunds_MerchantRefundNo",
                table: "ColCollectionRefunds",
                column: "MerchantRefundNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColCollectionRefunds_Status_LastCheckedAt_Id",
                table: "ColCollectionRefunds",
                columns: new[] { "Status", "LastCheckedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_ColCollectionRefunds_Channel_Account_RefundNo",
                table: "ColCollectionRefunds",
                columns: new[] { "Channel", "MerchantAccountId", "ChannelRefundNo" },
                unique: true,
                filter: "\"ChannelRefundNo\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColPaymentEvents_NextAttemptAt_CreatedAt",
                table: "ColPaymentEvents",
                columns: new[] { "NextAttemptAt", "CreatedAt" },
                filter: "\"DeliveredAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColCollectionRefunds");

            migrationBuilder.DropTable(
                name: "ColPaymentEvents");

            migrationBuilder.DropIndex(
                name: "IX_ColCollections_Channel_MerchantAccountId_ChannelTradeNo",
                table: "ColCollections");

            migrationBuilder.DropIndex(
                name: "IX_ColCollections_NextChannelQueryAt_CreationTime",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "ChannelCreateLeaseId",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "ChannelCreateLeaseUntil",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "ChannelOrderCreated",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "ChannelPaymentMismatch",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "CloseRequested",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "MerchantAccountId",
                table: "ColCollections");

            migrationBuilder.DropColumn(
                name: "NextChannelQueryAt",
                table: "ColCollections");
        }
    }
}
