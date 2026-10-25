using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddHotWalletWatchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ColWithdrawOrders_TxHash",
                table: "ColWithdrawOrders",
                column: "TxHash",
                filter: "\"TxHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColHotWalletTxns_BlockNumber_LogIndex",
                table: "ColHotWalletTxns",
                columns: new[] { "BlockNumber", "LogIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColWithdrawOrders_TxHash",
                table: "ColWithdrawOrders");

            migrationBuilder.DropIndex(
                name: "IX_ColHotWalletTxns_BlockNumber_LogIndex",
                table: "ColHotWalletTxns");
        }
    }
}
