using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColWithdrawOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Fee = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    FeeRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: true),
                    NonceUsed = table.Column<long>(type: "bigint", nullable: true),
                    BroadcastAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    FailReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AuditUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuditRemark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    AuditTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompleteTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColWithdrawOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColWithdrawOrders_Status_StatusChangedAt",
                table: "ColWithdrawOrders",
                columns: new[] { "Status", "StatusChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ColWithdrawOrders_ToAddress_CompleteTime",
                table: "ColWithdrawOrders",
                columns: new[] { "ToAddress", "CompleteTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColWithdrawOrders_UserId_CreationTime",
                table: "ColWithdrawOrders",
                columns: new[] { "UserId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColWithdrawOrders");
        }
    }
}
