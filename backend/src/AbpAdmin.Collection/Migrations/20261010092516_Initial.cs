using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,18)", precision: 28, scale: 18, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ChannelResultUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ChannelTradeNo = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ChannelPayload = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColCollections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColCollections_BusinessType_BusinessId",
                table: "ColCollections",
                columns: new[] { "BusinessType", "BusinessId" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ColCollections_CollectionNo",
                table: "ColCollections",
                column: "CollectionNo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColCollections");
        }
    }
}
