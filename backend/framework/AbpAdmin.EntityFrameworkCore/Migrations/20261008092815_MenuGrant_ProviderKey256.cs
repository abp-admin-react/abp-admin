using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Migrations
{
    /// <summary>
    /// AppMenuGrants 键宽对齐 ABP 角色名上限：ProviderKey 64→256——
    /// 此前 64 时，角色名 65+（ABP IdentityRole.Name 允许 256）的角色一旦被授予菜单，
    /// 插入即撞列宽 500（宽度校验在 ABP 侧，本服务无法前置拦截）。
    /// ProviderName 收窄 64→8（代码常量 "R"/"U"，随键共用常量虚胖，单列）。
    /// 加宽方向无数据风险；Down 收窄前需确认存量值不超过 64。
    /// </summary>
    public partial class MenuGrant_ProviderKey256 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProviderName",
                table: "AppMenuGrants",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AppMenuGrants",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ProviderName",
                table: "AppMenuGrants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderKey",
                table: "AppMenuGrants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
