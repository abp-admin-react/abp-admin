using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddWithdrawHotWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WithdrawVaultAddress",
                table: "ColNetworks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawVaultKeyCipher",
                table: "ColNetworks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WithdrawVaultAddress",
                table: "ColNetworks");

            migrationBuilder.DropColumn(
                name: "WithdrawVaultKeyCipher",
                table: "ColNetworks");
        }
    }
}
