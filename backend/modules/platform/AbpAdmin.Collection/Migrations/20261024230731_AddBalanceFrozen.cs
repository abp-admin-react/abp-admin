using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceFrozen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Frozen",
                table: "ColBalances",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ColBalances_FrozenNonNegative",
                table: "ColBalances",
                sql: "\"Frozen\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ColBalances_FrozenNonNegative",
                table: "ColBalances");

            migrationBuilder.DropColumn(
                name: "Frozen",
                table: "ColBalances");
        }
    }
}
