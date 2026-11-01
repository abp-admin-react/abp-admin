using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPayNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayNetwork",
                table: "OrdOrders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayNetwork",
                table: "OrdOrders");
        }
    }
}
