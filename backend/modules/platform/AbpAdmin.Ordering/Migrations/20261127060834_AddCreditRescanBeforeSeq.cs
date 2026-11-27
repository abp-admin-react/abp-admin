using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Ordering.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditRescanBeforeSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreditRescanBeforeSeq",
                table: "OrdJobStates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedCollectionBeforeId",
                table: "OrdJobStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedDebitBeforeId",
                table: "OrdJobStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreditRescanStartedAt",
                table: "OrdJobStates",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidCollectionBeforeId",
                table: "OrdJobStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingCollectionBeforeId",
                table: "OrdJobStates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingDebitBeforeId",
                table: "OrdJobStates",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditRescanBeforeSeq",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "ClosedCollectionBeforeId",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "ClosedDebitBeforeId",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "CreditRescanStartedAt",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "PaidCollectionBeforeId",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "PendingCollectionBeforeId",
                table: "OrdJobStates");

            migrationBuilder.DropColumn(
                name: "PendingDebitBeforeId",
                table: "OrdJobStates");
        }
    }
}
