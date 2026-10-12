using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddChainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColDepositAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    AddressIndex = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColDepositAddresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColDepositOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    FromAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    LogIndex = table.Column<int>(type: "integer", nullable: false),
                    RawAmount = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: false),
                    BlockTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SweepTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColDepositOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColNetworks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChainId = table.Column<long>(type: "bigint", nullable: false),
                    MnemonicCipher = table.Column<string>(type: "text", nullable: true),
                    RpcCipher = table.Column<string>(type: "text", nullable: true),
                    NodeLogResultCap = table.Column<int>(type: "integer", nullable: true),
                    ScanHeartbeatAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SweepHeartbeatAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LatestChainHead = table.Column<long>(type: "bigint", nullable: true),
                    GasLowAlertedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ScanBehindAlertedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SweepStuckAlertedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColNetworks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColSweepTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    AddressIndex = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    FailReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: true),
                    NonceUsed = table.Column<long>(type: "bigint", nullable: true),
                    TxBlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    GasTxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColSweepTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ContractAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    MinDeposit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VaultAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: true),
                    LastScannedBlock = table.Column<long>(type: "bigint", nullable: true),
                    LastScanAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColVaultChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RawVaultAmount = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    VaultBalance = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    UserTotal = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColVaultChecks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositAddresses_NetworkId_Address",
                table: "ColDepositAddresses",
                columns: new[] { "NetworkId", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositAddresses_NetworkId_AddressIndex",
                table: "ColDepositAddresses",
                columns: new[] { "NetworkId", "AddressIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositAddresses_UserId_NetworkId",
                table: "ColDepositAddresses",
                columns: new[] { "UserId", "NetworkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_Status_BlockNumber",
                table: "ColDepositOrders",
                columns: new[] { "Status", "BlockNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_TokenId_Status",
                table: "ColDepositOrders",
                columns: new[] { "TokenId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_TxHash",
                table: "ColDepositOrders",
                column: "TxHash");

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_TxHash_LogIndex",
                table: "ColDepositOrders",
                columns: new[] { "TxHash", "LogIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColNetworks_Name",
                table: "ColNetworks",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColSweepTasks_AddressId",
                table: "ColSweepTasks",
                column: "AddressId",
                unique: true,
                filter: "\"Status\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ColSweepTasks_AddressId_TokenId",
                table: "ColSweepTasks",
                columns: new[] { "AddressId", "TokenId" },
                unique: true,
                filter: "\"Status\" IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "IX_ColSweepTasks_Status_StatusChangedAt",
                table: "ColSweepTasks",
                columns: new[] { "Status", "StatusChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ColTokens_NetworkId_ContractAddress",
                table: "ColTokens",
                columns: new[] { "NetworkId", "ContractAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColTokens_NetworkId_Symbol",
                table: "ColTokens",
                columns: new[] { "NetworkId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColVaultChecks_TokenId_CheckedAt",
                table: "ColVaultChecks",
                columns: new[] { "TokenId", "CheckedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColDepositAddresses");

            migrationBuilder.DropTable(
                name: "ColDepositOrders");

            migrationBuilder.DropTable(
                name: "ColNetworks");

            migrationBuilder.DropTable(
                name: "ColSweepTasks");

            migrationBuilder.DropTable(
                name: "ColTokens");

            migrationBuilder.DropTable(
                name: "ColVaultChecks");
        }
    }
}
