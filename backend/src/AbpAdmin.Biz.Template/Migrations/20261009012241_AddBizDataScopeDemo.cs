using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Biz.Template.Migrations
{
    /// <inheritdoc />
    public partial class AddBizDataScopeDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BizDataScopeDemos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BizDataScopeDemos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BizDataScopeDemos_TenantId_OrganizationUnitId",
                table: "BizDataScopeDemos",
                columns: new[] { "TenantId", "OrganizationUnitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BizDataScopeDemos");
        }
    }
}
