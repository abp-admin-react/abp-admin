using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AbpAdmin.TokenGateway.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TgApiKeys",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    KeyCipher = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgApiKeys", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "TgLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Ref = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TgOrders",
                columns: table => new
                {
                    OrderId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AmountCny = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tokens = table.Column<long>(type: "bigint", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    QrVersion = table.Column<int>(type: "integer", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgOrders", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "TgPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PriceCny = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tokens = table.Column<long>(type: "bigint", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TgReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Charged = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgReservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TgSessions",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgSessions", x => x.TokenHash);
                });

            migrationBuilder.CreateTable(
                name: "TgUsage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Bot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptTokens = table.Column<long>(type: "bigint", nullable: false),
                    CompletionTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<long>(type: "bigint", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgUsage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TgWallets",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalCredited = table.Column<long>(type: "bigint", nullable: false),
                    TotalSettled = table.Column<long>(type: "bigint", nullable: false),
                    Frozen = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TgWallets", x => x.UserId);
                });

            migrationBuilder.InsertData(
                table: "TgPlans",
                columns: new[] { "Id", "IsActive", "Name", "PriceCny", "SortOrder", "Tokens", "UpdatedAt" },
                values: new object[,]
                {
                    { "p198", true, "进阶包", 198m, 3, 19800000L, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "p30", true, "体验包", 30m, 1, 3000000L, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "p498", true, "专业包", 498m, 4, 49800000L, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "p98", true, "标准包", 98m, 2, 9800000L, new DateTime(2026, 10, 14, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TgApiKeys_KeyHash",
                table: "TgApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TgLedgerEntries_Kind_Ref",
                table: "TgLedgerEntries",
                columns: new[] { "Kind", "Ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TgLedgerEntries_UserId_CreatedAt",
                table: "TgLedgerEntries",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TgOrders_UserId_CreatedAt",
                table: "TgOrders",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TgReservations_UserId_Status",
                table: "TgReservations",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TgSessions_ExpiresAt",
                table: "TgSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TgUsage_UserId_Time",
                table: "TgUsage",
                columns: new[] { "UserId", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TgApiKeys");

            migrationBuilder.DropTable(
                name: "TgLedgerEntries");

            migrationBuilder.DropTable(
                name: "TgOrders");

            migrationBuilder.DropTable(
                name: "TgPlans");

            migrationBuilder.DropTable(
                name: "TgReservations");

            migrationBuilder.DropTable(
                name: "TgSessions");

            migrationBuilder.DropTable(
                name: "TgUsage");

            migrationBuilder.DropTable(
                name: "TgWallets");
        }
    }
}
