using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PersistSweepRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GasSignedTransaction",
                table: "ColSweepTasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptCursor",
                table: "ColSweepTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GasSignedTransaction",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "ReceiptCursor",
                table: "ColSweepTasks");
        }
    }
}
