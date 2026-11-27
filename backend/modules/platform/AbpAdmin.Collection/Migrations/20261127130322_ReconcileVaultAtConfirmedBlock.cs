using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileVaultAtConfirmedBlock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PendingCredit",
                table: "ColVaultChecks",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PostedAfterBlock",
                table: "ColVaultChecks",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReconciledDifference",
                table: "ColVaultChecks",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationStatus",
                table: "ColVaultChecks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReferenceBlock",
                table: "ColVaultChecks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceVaultAddress",
                table: "ColVaultChecks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingCredit",
                table: "ColVaultChecks");

            migrationBuilder.DropColumn(
                name: "PostedAfterBlock",
                table: "ColVaultChecks");

            migrationBuilder.DropColumn(
                name: "ReconciledDifference",
                table: "ColVaultChecks");

            migrationBuilder.DropColumn(
                name: "ReconciliationStatus",
                table: "ColVaultChecks");

            migrationBuilder.DropColumn(
                name: "ReferenceBlock",
                table: "ColVaultChecks");

            migrationBuilder.DropColumn(
                name: "ReferenceVaultAddress",
                table: "ColVaultChecks");
        }
    }
}
