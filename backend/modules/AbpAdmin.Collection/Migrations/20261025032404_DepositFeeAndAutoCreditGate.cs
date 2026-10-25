using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DepositFeeAndAutoCreditGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AutoCreditMaxAmount",
                table: "ColNetworks",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositFeeRate",
                table: "ColNetworks",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualAmount",
                table: "ColDepositOrders",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Fee",
                table: "ColDepositOrders",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FeeRateSnapshot",
                table: "ColDepositOrders",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoCreditMaxAmount",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "DepositFeeRate",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "ActualAmount",
                table: "ColDepositOrders");

            migrationBuilder.DropColumn(
                name: "Fee",
                table: "ColDepositOrders");

            migrationBuilder.DropColumn(
                name: "FeeRateSnapshot",
                table: "ColDepositOrders");
        }
    }
}
