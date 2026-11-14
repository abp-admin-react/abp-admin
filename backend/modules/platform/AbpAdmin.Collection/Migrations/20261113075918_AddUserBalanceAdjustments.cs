using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBalanceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColBalanceAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApplicantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    BalanceTxnId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerVoucherId = table.Column<Guid>(type: "uuid", nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColBalanceAdjustments", x => x.Id);
                    table.CheckConstraint("CK_ColBalanceAdjustments_Amount", "\"Amount\" <> 0");
                    table.CheckConstraint("CK_ColBalanceAdjustments_Currency", "\"Currency\" IN (3, 4)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceAdjustments_BalanceTxnId",
                table: "ColBalanceAdjustments",
                column: "BalanceTxnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceAdjustments_LedgerVoucherId",
                table: "ColBalanceAdjustments",
                column: "LedgerVoucherId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceAdjustments_RequestId",
                table: "ColBalanceAdjustments",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceAdjustments_Status_CreationTime",
                table: "ColBalanceAdjustments",
                columns: new[] { "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceAdjustments_UserId_CreationTime",
                table: "ColBalanceAdjustments",
                columns: new[] { "UserId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColBalanceAdjustments");
        }
    }
}
