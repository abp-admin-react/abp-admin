using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PreserveDepositReleaseAndSweepDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BroadcastVaultAddress",
                table: "ColSweepTasks",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyReleased",
                table: "ColDepositOrders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_SweepTaskId_Status",
                table: "ColDepositOrders",
                columns: new[] { "SweepTaskId", "Status" },
                filter: "\"SweepTaskId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColDepositOrders_SweepTaskId_Status",
                table: "ColDepositOrders");

            migrationBuilder.DropColumn(
                name: "BroadcastVaultAddress",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "ManuallyReleased",
                table: "ColDepositOrders");
        }
    }
}
