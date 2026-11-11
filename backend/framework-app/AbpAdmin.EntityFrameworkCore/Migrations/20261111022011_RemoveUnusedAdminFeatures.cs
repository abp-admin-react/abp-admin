using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedAdminFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 模板变化不会清理存量租户菜单。只删除已退役页面的确切路径，保留
            // 其他菜单及其套餐配置，并将受影响的自定义子项挂回最近的存活祖先。
            migrationBuilder.Sql("""
                CREATE TEMPORARY TABLE "__RetiredAdminMenus" ON COMMIT DROP AS
                SELECT "Id", "ParentId"
                FROM "AppMenus"
                WHERE "Path" IN ('/current-session', '/data-scope-demo', '/administration/permission-definitions');

                WITH RECURSIVE parent_chain AS (
                    SELECT "Id" AS "RetiredId", "ParentId", ARRAY["Id"] AS "Seen"
                    FROM "__RetiredAdminMenus"
                    UNION ALL
                    SELECT chain."RetiredId", parent."ParentId", chain."Seen" || parent."Id"
                    FROM parent_chain chain
                    JOIN "__RetiredAdminMenus" parent ON parent."Id" = chain."ParentId"
                    WHERE NOT parent."Id" = ANY(chain."Seen")
                ), surviving_parents AS (
                    SELECT "RetiredId", "ParentId"
                    FROM parent_chain chain
                    WHERE chain."ParentId" IS NULL
                       OR NOT EXISTS (
                           SELECT 1 FROM "__RetiredAdminMenus" parent
                           WHERE parent."Id" = chain."ParentId")
                )
                UPDATE "AppMenus" child
                SET "ParentId" = (
                    SELECT parent."ParentId" FROM surviving_parents parent
                    WHERE parent."RetiredId" = child."ParentId")
                WHERE child."ParentId" IN (SELECT "Id" FROM "__RetiredAdminMenus")
                  AND child."Id" NOT IN (SELECT "Id" FROM "__RetiredAdminMenus");

                DELETE FROM "AppTenantPackageMenus"
                WHERE "TemplateMenuId" IN (SELECT "Id" FROM "__RetiredAdminMenus");
                DELETE FROM "AppMenus"
                WHERE "Id" IN (SELECT "Id" FROM "__RetiredAdminMenus");

                -- 旧用户菜单只看当前租户的 R 授权。仅靠角色勾选限制的节点在删表后
                -- 默认会公开，先隐藏以免扩大可见范围。隐藏节点的有效子项仍可向上挂接。
                UPDATE "AppMenus" menu
                SET "IsHide" = TRUE
                WHERE menu."PermissionName" IS NULL
                  AND NOT menu."IsHide"
                  AND EXISTS (
                      SELECT 1 FROM "AppMenuGrants" legacy_grant
                      WHERE legacy_grant."MenuId" = menu."Id"
                        AND legacy_grant."ProviderName" = 'R'
                        AND legacy_grant."TenantId" IS NOT DISTINCT FROM menu."TenantId");

                CREATE TEMPORARY TABLE "__RetiredAdminPermissionNames" ON COMMIT DROP AS
                SELECT "Name" FROM (VALUES
                    ('AbpAdmin.Menus.AssignRoles'),
                    ('AbpAdmin.DataScopes.Demo'),
                    ('AbpAdmin.PermissionDefinitions'),
                    ('AbpAdmin.PermissionDefinitions.Create'),
                    ('AbpAdmin.PermissionDefinitions.Update'),
                    ('AbpAdmin.PermissionDefinitions.Delete'),
                    ('AbpAdmin.BackgroundJobs.Enqueue')
                ) AS names("Name");

                -- ABP 从 ParentName 为空的记录开始构建动态权限树。自定义子权限
                -- 若挂在已退役权限下，移到同组根级，保留其授权与下级关系。
                UPDATE "AbpPermissions"
                SET "ParentName" = NULL
                WHERE "ParentName" IN (SELECT "Name" FROM "__RetiredAdminPermissionNames")
                  AND "Name" NOT IN (SELECT "Name" FROM "__RetiredAdminPermissionNames");

                DELETE FROM "AbpPermissionGrants"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredAdminPermissionNames");
                DELETE FROM "AbpPermissions"
                WHERE "Name" IN (SELECT "Name" FROM "__RetiredAdminPermissionNames");
                """);

            migrationBuilder.DropTable(
                name: "AppDataScopeDemos");

            migrationBuilder.DropTable(
                name: "AppMenuGrants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 仅恢复旧表结构。回滚不会恢复已退役的数据或修改过的菜单隐藏设置。
            migrationBuilder.CreateTable(
                name: "AppDataScopeDemos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrganizationUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDataScopeDemos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppMenuGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMenuGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppMenuGrants_AppMenus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "AppMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDataScopeDemos_TenantId_OrganizationUnitId",
                table: "AppDataScopeDemos",
                columns: new[] { "TenantId", "OrganizationUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppMenuGrants_MenuId_ProviderName_ProviderKey",
                table: "AppMenuGrants",
                columns: new[] { "MenuId", "ProviderName", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMenuGrants_TenantId_ProviderName_ProviderKey",
                table: "AppMenuGrants",
                columns: new[] { "TenantId", "ProviderName", "ProviderKey" });
        }
    }
}
