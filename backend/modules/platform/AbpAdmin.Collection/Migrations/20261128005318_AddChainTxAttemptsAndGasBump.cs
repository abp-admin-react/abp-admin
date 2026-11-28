using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddChainTxAttemptsAndGasBump : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiscoverCursorBlock",
                table: "ColSweepTasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FuelBumpSteps",
                table: "ColSweepTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GasBroadcastAt",
                table: "ColSweepTasks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GasBumpSteps",
                table: "ColSweepTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GasTxGasPriceWei",
                table: "ColSweepTasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SweepTxGasPriceWei",
                table: "ColSweepTasks",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ColChainTxAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    SenderAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    ToAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    Nonce = table.Column<long>(type: "bigint", nullable: false),
                    AttemptNo = table.Column<int>(type: "integer", nullable: false),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    GasPriceWei = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    GasLimit = table.Column<int>(type: "integer", nullable: false),
                    RawSignedTx = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BroadcastAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColChainTxAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColSweepTasks_NetworkId_Status",
                table: "ColSweepTasks",
                columns: new[] { "NetworkId", "Status" },
                filter: "\"Status\" = 1 AND \"GasTxHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColChainTxAttempts_NetworkId_SenderAddress_Nonce",
                table: "ColChainTxAttempts",
                columns: new[] { "NetworkId", "SenderAddress", "Nonce" });

            migrationBuilder.CreateIndex(
                name: "IX_ColChainTxAttempts_NetworkId_TxHash",
                table: "ColChainTxAttempts",
                columns: new[] { "NetworkId", "TxHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColChainTxAttempts_Purpose_RefId_AttemptNo",
                table: "ColChainTxAttempts",
                columns: new[] { "Purpose", "RefId", "AttemptNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColChainTxAttempts");

            migrationBuilder.DropIndex(
                name: "IX_ColSweepTasks_NetworkId_Status",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "DiscoverCursorBlock",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "FuelBumpSteps",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "GasBroadcastAt",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "GasBumpSteps",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "GasTxGasPriceWei",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "SweepTxGasPriceWei",
                table: "ColSweepTasks");
        }
    }
}
