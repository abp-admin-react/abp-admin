using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddTodayWindowIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ColWithdrawOrders_CreationTime",
                table: "ColWithdrawOrders",
                column: "CreationTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColWithdrawOrders_CreationTime",
                table: "ColWithdrawOrders");
        }
    }
}
