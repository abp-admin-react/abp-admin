using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class PreserveLegacyDepositWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColDepositAddresses_UserId_NetworkId",
                table: "ColDepositAddresses");

            migrationBuilder.AddColumn<int>(
                name: "LegacyAddressIndexHighWaterMark",
                table: "ColNetworks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRetired",
                table: "ColDepositAddresses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositAddresses_UserId_NetworkId",
                table: "ColDepositAddresses",
                columns: new[] { "UserId", "NetworkId" },
                unique: true,
                filter: "NOT \"IsRetired\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColDepositAddresses_UserId_NetworkId",
                table: "ColDepositAddresses");

            migrationBuilder.DropColumn(
                name: "LegacyAddressIndexHighWaterMark",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "IsRetired",
                table: "ColDepositAddresses");

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositAddresses_UserId_NetworkId",
                table: "ColDepositAddresses",
                columns: new[] { "UserId", "NetworkId" },
                unique: true);
        }
    }
}
