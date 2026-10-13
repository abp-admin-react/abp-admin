using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrdOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderType = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PayChannel = table.Column<int>(type: "integer", nullable: true),
                    PayAmount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    PayCurrency = table.Column<int>(type: "integer", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    BalanceTxnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CloseReason = table.Column<int>(type: "integer", nullable: true),
                    PaymentAnomaly = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrdPaymentAnomalyLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    Currency = table.Column<int>(type: "integer", nullable: true),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChannelTradeNo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdPaymentAnomalyLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrdQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayMethod = table.Column<int>(type: "integer", nullable: false),
                    PayCurrency = table.Column<int>(type: "integer", nullable: false),
                    PayAmount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    UsdCnyRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdQuotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_BalanceTxnId",
                table: "OrdOrders",
                column: "BalanceTxnId",
                unique: true,
                filter: "\"OrderType\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_OrderNo",
                table: "OrdOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_Status",
                table: "OrdOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_UserId",
                table: "OrdOrders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdPaymentAnomalyLines_OrderId",
                table: "OrdPaymentAnomalyLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdQuotes_OrderId_PayMethod",
                table: "OrdQuotes",
                columns: new[] { "OrderId", "PayMethod" },
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrdOrders");

            migrationBuilder.DropTable(
                name: "OrdPaymentAnomalyLines");

            migrationBuilder.DropTable(
                name: "OrdQuotes");
        }
    }
}
