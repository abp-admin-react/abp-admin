using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstPrinciplesChainHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreflightDeficitCount",
                table: "ColSweepTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SponsorWatchGaveUp",
                table: "ColNetworks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SponsorWatchGaveUpAt",
                table: "ColNetworks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SponsorWatchHeadNonce",
                table: "ColNetworks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SponsorWatchLastReplaceAt",
                table: "ColNetworks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SponsorWatchLastSeenPendingAt",
                table: "ColNetworks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SponsorWatchReplaceAttempts",
                table: "ColNetworks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SponsorWatchStuckSince",
                table: "ColNetworks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCheckpointWalletAddress",
                table: "ColHotWalletWatchStates",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreflightDeficitCount",
                table: "ColSweepTasks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchGaveUp",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchGaveUpAt",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchHeadNonce",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchLastReplaceAt",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchLastSeenPendingAt",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchReplaceAttempts",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "SponsorWatchStuckSince",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "LastCheckpointWalletAddress",
                table: "ColHotWalletWatchStates");
        }
    }
}
