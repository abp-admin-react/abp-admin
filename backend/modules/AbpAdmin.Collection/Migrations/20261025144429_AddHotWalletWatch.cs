using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddHotWalletWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColHotWalletTxns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TxHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    LogIndex = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    To = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    RawAmount = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: true),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MatchedWithdrawId = table.Column<Guid>(type: "uuid", nullable: true),
                    AckedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AckNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColHotWalletTxns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColHotWalletWatchStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LastScannedBlock = table.Column<long>(type: "bigint", nullable: true),
                    LastScanAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColHotWalletWatchStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColHotWalletTxns_CreationTime",
                table: "ColHotWalletTxns",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_ColHotWalletTxns_Status_CreationTime",
                table: "ColHotWalletTxns",
                columns: new[] { "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColHotWalletTxns_TxHash_LogIndex",
                table: "ColHotWalletTxns",
                columns: new[] { "TxHash", "LogIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColHotWalletWatchStates_TokenId",
                table: "ColHotWalletWatchStates",
                column: "TokenId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColHotWalletTxns");

            migrationBuilder.DropTable(
                name: "ColHotWalletWatchStates");
        }
    }
}
