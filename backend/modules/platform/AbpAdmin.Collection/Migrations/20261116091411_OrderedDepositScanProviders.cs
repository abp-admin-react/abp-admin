using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class OrderedDepositScanProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastScanFailure",
                table: "ColTokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastScanProvider",
                table: "ColTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScanProviderAt",
                table: "ColTokens",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanReplayFromBlock",
                table: "ColTokens",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ScanReplayToBlock",
                table: "ColTokens",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyDepositRpcCipher",
                table: "ColNetworks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ScanBillingClaimPending",
                table: "ColNetworks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ColScanProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyCipher = table.Column<string>(type: "text", nullable: false),
                    PlatformAuthKeyCipher = table.Column<string>(type: "text", nullable: true),
                    Group = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColScanProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColScanProviderNetworks",
                columns: table => new
                {
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogResultCap = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColScanProviderNetworks", x => new { x.ProviderId, x.NetworkId });
                    table.ForeignKey(
                        name: "FK_ColScanProviderNetworks_ColNetworks_NetworkId",
                        column: x => x.NetworkId,
                        principalTable: "ColNetworks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ColScanProviderNetworks_ColScanProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "ColScanProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColScanProviderNetworks_NetworkId",
                table: "ColScanProviderNetworks",
                column: "NetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_ColScanProviders_Provider_KeyHash",
                table: "ColScanProviders",
                columns: new[] { "Provider", "KeyHash" });

            // Copy encrypted credentials only. The existing transaction pool stays independent.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT key.*, network."Name" AS "NetworkName",
                           row_number() OVER (
                               PARTITION BY key."NetworkId", key."Provider"
                               ORDER BY key."IsEnabled" DESC, key."CreationTime", key."Id") AS "ProviderRank"
                    FROM "ColChainApiKeys" key
                    JOIN "ColNetworks" network ON network."Id" = key."NetworkId"
                    WHERE key."Provider" IN (1, 2)
                )
                INSERT INTO "ColScanProviders" ("Id", "Provider", "Label", "KeyHash", "KeyCipher", "IsEnabled", "SortOrder")
                SELECT "Id", "Provider", "Label", "KeyHash", "KeyCipher", "IsEnabled",
                       (row_number() OVER (ORDER BY
                           CASE WHEN "NetworkName" = 'BSC' AND "IsEnabled" AND "ProviderRank" = 1
                                THEN "Provider" ELSE 3 END,
                           "CreationTime", "Id") - 1)::integer
                FROM ranked;

                INSERT INTO "ColScanProviderNetworks" ("ProviderId", "NetworkId", "LogResultCap")
                SELECT provider."Id", key."NetworkId",
                       CASE WHEN key."Provider" = 1 THEN network."NodeLogResultCap" END
                FROM "ColScanProviders" provider
                JOIN "ColChainApiKeys" key ON key."Id" = provider."Id"
                JOIN "ColNetworks" network ON network."Id" = key."NetworkId";

                WITH billing AS (
                    SELECT network."Id" AS "NetworkId", network."AnkrPlatformAuthKeyCipher", network."AnkrGroup",
                           (array_agg(link."ProviderId") FILTER (WHERE provider."Provider" = 1))[1] AS "ProviderId",
                           count(provider."Id") FILTER (WHERE provider."Provider" = 1) AS "Candidates"
                    FROM "ColNetworks" network
                    LEFT JOIN "ColScanProviderNetworks" link ON link."NetworkId" = network."Id"
                    LEFT JOIN "ColScanProviders" provider ON provider."Id" = link."ProviderId"
                    WHERE network."Name" = 'BSC' AND network."AnkrPlatformAuthKeyCipher" IS NOT NULL
                    GROUP BY network."Id"
                ), assigned AS (
                    UPDATE "ColScanProviders" provider
                    SET "PlatformAuthKeyCipher" = billing."AnkrPlatformAuthKeyCipher", "Group" = billing."AnkrGroup"
                    FROM billing WHERE billing."Candidates" = 1 AND provider."Id" = billing."ProviderId"
                    RETURNING provider."Id"
                )
                UPDATE "ColNetworks" network SET "ScanBillingClaimPending" = true
                FROM billing WHERE billing."Candidates" <> 1 AND network."Id" = billing."NetworkId";

                UPDATE "ColNetworks" network SET "LegacyDepositRpcCipher" = network."RpcCipher"
                WHERE network."Name" = 'BSC' AND network."RpcCipher" IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM "ColChainApiKeys" key
                        WHERE key."NetworkId" = network."Id" AND key."Provider" = 1 AND key."IsEnabled"
                    );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older scanner has no replay marker. Let it rescan all unresolved blocks using existing deduplication.
            migrationBuilder.Sql("""
                UPDATE "ColTokens"
                SET "LastScannedBlock" = LEAST("LastScannedBlock", GREATEST("ScanReplayFromBlock" - 1, 0))
                WHERE "ScanReplayFromBlock" IS NOT NULL AND "LastScannedBlock" IS NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "ColScanProviderNetworks");

            migrationBuilder.DropTable(
                name: "ColScanProviders");

            migrationBuilder.DropColumn(
                name: "LastScanFailure",
                table: "ColTokens");

            migrationBuilder.DropColumn(
                name: "LastScanProvider",
                table: "ColTokens");

            migrationBuilder.DropColumn(
                name: "LastScanProviderAt",
                table: "ColTokens");

            migrationBuilder.DropColumn(
                name: "ScanReplayFromBlock",
                table: "ColTokens");

            migrationBuilder.DropColumn(
                name: "ScanReplayToBlock",
                table: "ColTokens");

            migrationBuilder.DropColumn(
                name: "LegacyDepositRpcCipher",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "ScanBillingClaimPending",
                table: "ColNetworks");
        }
    }
}
