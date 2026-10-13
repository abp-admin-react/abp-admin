using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCreationTimeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrdOrders_CreationTime",
                table: "OrdOrders",
                column: "CreationTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdOrders_CreationTime",
                table: "OrdOrders");
        }
    }
}
