using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ledger.Migrations
{
    /// <inheritdoc />
    public partial class F2_VoucherPostedTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PostedTime",
                table: "FinVouchers",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_FinVouchers_TenantId_PostedTime",
                table: "FinVouchers",
                columns: new[] { "TenantId", "PostedTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinVouchers_TenantId_PostedTime",
                table: "FinVouchers");

            migrationBuilder.DropColumn(
                name: "PostedTime",
                table: "FinVouchers");
        }
    }
}
