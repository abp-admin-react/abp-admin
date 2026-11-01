using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Collection.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessQueryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ColCollections_BusinessType_BusinessId_Query",
                table: "ColCollections",
                columns: new[] { "BusinessType", "BusinessId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ColCollections_BusinessType_BusinessId_Query",
                table: "ColCollections");
        }
    }
}
