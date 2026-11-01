using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusSyncScanIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_ExpiresAt",
                table: "OrdOrders",
                column: "ExpiresAt",
                filter: "\"Status\" = 2 AND \"OrderType\" = 2");

            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_PaidAt",
                table: "OrdOrders",
                column: "PaidAt",
                filter: "\"Status\" = 1 AND \"OrderType\" = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdOrders_ExpiresAt",
                table: "OrdOrders");

            migrationBuilder.DropIndex(
                name: "IX_OrdOrders_PaidAt",
                table: "OrdOrders");
        }
    }
}
