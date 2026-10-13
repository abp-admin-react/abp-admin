using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCommerceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelTradeNo",
                table: "OrdOrders",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FulfilledAt",
                table: "OrdOrders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                table: "OrdOrders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelTradeNo",
                table: "OrdOrders");

            migrationBuilder.DropColumn(
                name: "FulfilledAt",
                table: "OrdOrders");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "OrdOrders");
        }
    }
}
