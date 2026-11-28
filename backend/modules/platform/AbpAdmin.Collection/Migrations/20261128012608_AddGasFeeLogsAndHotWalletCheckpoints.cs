using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddGasFeeLogsAndHotWalletCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCheckpointAt",
                table: "ColHotWalletWatchStates",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCheckpointBalanceRaw",
                table: "ColHotWalletWatchStates",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastCheckpointBlock",
                table: "ColHotWalletWatchStates",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ColGasFeeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: true),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    SenderAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    GasUsed = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveGasPriceWei = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FeeBnb = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColGasFeeLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColGasFeeLogs_NetworkId_CreationTime",
                table: "ColGasFeeLogs",
                columns: new[] { "NetworkId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColGasFeeLogs_NetworkId_TxHash",
                table: "ColGasFeeLogs",
                columns: new[] { "NetworkId", "TxHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColGasFeeLogs_Purpose_CreationTime",
                table: "ColGasFeeLogs",
                columns: new[] { "Purpose", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColGasFeeLogs");

            migrationBuilder.DropColumn(
                name: "LastCheckpointAt",
                table: "ColHotWalletWatchStates");

            migrationBuilder.DropColumn(
                name: "LastCheckpointBalanceRaw",
                table: "ColHotWalletWatchStates");

            migrationBuilder.DropColumn(
                name: "LastCheckpointBlock",
                table: "ColHotWalletWatchStates");
        }
    }
}
