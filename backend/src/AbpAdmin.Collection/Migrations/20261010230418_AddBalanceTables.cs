using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Balance = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColBalances", x => x.Id);
                    table.CheckConstraint("CK_ColBalances_NonNegative", "\"Balance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ColBalanceTxns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    RefTxnId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColBalanceTxns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColBalances_UserId_Currency",
                table: "ColBalances",
                columns: new[] { "UserId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceTxns_Action_CreationTime",
                table: "ColBalanceTxns",
                columns: new[] { "Action", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceTxns_Action_Seq",
                table: "ColBalanceTxns",
                columns: new[] { "Action", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceTxns_BusinessType_BusinessId",
                table: "ColBalanceTxns",
                columns: new[] { "BusinessType", "BusinessId" },
                unique: true,
                filter: "\"Action\" = 'BusinessDebit' AND \"ReversedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceTxns_IdempotencyKey",
                table: "ColBalanceTxns",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ColBalanceTxns_UserId_Currency",
                table: "ColBalanceTxns",
                columns: new[] { "UserId", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColBalances");

            migrationBuilder.DropTable(
                name: "ColBalanceTxns");
        }
    }
}
