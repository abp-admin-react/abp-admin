using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleSweepBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastProcessedAt",
                table: "ColSweepTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColSweepTasks_TokenId_Status_LastProcessedAt",
                table: "ColSweepTasks",
                columns: new[] { "TokenId", "Status", "LastProcessedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColSweepTasks_TokenId_Status_LastProcessedAt",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "LastProcessedAt",
                table: "ColSweepTasks");
        }
    }
}
