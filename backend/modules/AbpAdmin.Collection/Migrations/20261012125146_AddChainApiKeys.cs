using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddChainApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColChainApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyCipher = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColChainApiKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_TokenId_CreationTime",
                table: "ColDepositOrders",
                columns: new[] { "TokenId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColDepositOrders_UserId_CreationTime",
                table: "ColDepositOrders",
                columns: new[] { "UserId", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColChainApiKeys_NetworkId_Provider_IsEnabled",
                table: "ColChainApiKeys",
                columns: new[] { "NetworkId", "Provider", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ColChainApiKeys_NetworkId_Provider_KeyHash",
                table: "ColChainApiKeys",
                columns: new[] { "NetworkId", "Provider", "KeyHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColChainApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ColDepositOrders_TokenId_CreationTime",
                table: "ColDepositOrders");

            migrationBuilder.DropIndex(
                name: "IX_ColDepositOrders_UserId_CreationTime",
                table: "ColDepositOrders");
        }
    }
}
