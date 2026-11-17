using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColWalletRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDebitTxnId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BalanceTxnId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColWalletRefunds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColWalletRefunds_OriginalDebitTxnId",
                table: "ColWalletRefunds",
                column: "OriginalDebitTxnId");

            migrationBuilder.CreateIndex(
                name: "IX_ColWalletRefunds_OriginalDebitTxnId_RequestId",
                table: "ColWalletRefunds",
                columns: new[] { "OriginalDebitTxnId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColWalletRefunds");
        }
    }
}
