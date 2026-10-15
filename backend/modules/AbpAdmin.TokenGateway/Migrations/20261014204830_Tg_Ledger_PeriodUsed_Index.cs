using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.TokenGateway.Migrations
{
    /// <inheritdoc />
    public partial class Tg_Ledger_PeriodUsed_Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TgLedgerEntries_UserId_CreatedAt",
                table: "TgLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TgLedgerEntries_UserId_Kind_CreatedAt",
                table: "TgLedgerEntries",
                columns: new[] { "UserId", "Kind", "CreatedAt" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TgLedgerEntries_UserId_Kind_CreatedAt",
                table: "TgLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_TgLedgerEntries_UserId_CreatedAt",
                table: "TgLedgerEntries",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}
