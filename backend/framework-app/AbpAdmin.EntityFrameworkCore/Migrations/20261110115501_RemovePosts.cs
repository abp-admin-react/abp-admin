using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbpAdmin.Migrations
{
    /// <inheritdoc />
    public partial class RemovePosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 模板删掉节点不会删除存量租户的菜单快照。仅退役岗位路径或明确绑定
            // 原五项岗位权限的节点；保留其自定义后代并挂回最近的存活祖先。
            // 此迁移会删除岗位表和历史数据，Down 只能恢复表结构，不能恢复被清理的数据。
            migrationBuilder.Sql("""
                CREATE TEMPORARY TABLE "__RetiredPostMenus" ON COMMIT DROP AS
                SELECT "Id", "ParentId"
                FROM "AppMenus"
                WHERE "Path" = '/administration/identity/posts'
                   OR "PermissionName" IN (
                       'AbpAdmin.Posts', 'AbpAdmin.Posts.Create', 'AbpAdmin.Posts.Update',
                       'AbpAdmin.Posts.Delete', 'AbpAdmin.Posts.ManageMembers');

                WITH RECURSIVE parent_chain AS (
                    SELECT "Id" AS "RetiredId", "ParentId", ARRAY["Id"] AS "Seen"
                    FROM "__RetiredPostMenus"
                    UNION ALL
                    SELECT chain."RetiredId", parent."ParentId", chain."Seen" || parent."Id"
                    FROM parent_chain chain
                    JOIN "__RetiredPostMenus" parent ON parent."Id" = chain."ParentId"
                    WHERE NOT parent."Id" = ANY(chain."Seen")
                ), surviving_parents AS (
                    SELECT "RetiredId", "ParentId"
                    FROM parent_chain chain
                    WHERE chain."ParentId" IS NULL
                       OR NOT EXISTS (
                           SELECT 1 FROM "__RetiredPostMenus" parent
                           WHERE parent."Id" = chain."ParentId")
                )
                UPDATE "AppMenus" child
                SET "ParentId" = (
                    SELECT parent."ParentId" FROM surviving_parents parent
                    WHERE parent."RetiredId" = child."ParentId")
                WHERE child."ParentId" IN (SELECT "Id" FROM "__RetiredPostMenus")
                  AND child."Id" NOT IN (SELECT "Id" FROM "__RetiredPostMenus");

                DELETE FROM "AppMenuGrants"
                WHERE "MenuId" IN (SELECT "Id" FROM "__RetiredPostMenus");
                DELETE FROM "AppTenantPackageMenus"
                WHERE "TemplateMenuId" IN (SELECT "Id" FROM "__RetiredPostMenus");
                DELETE FROM "AppMenus"
                WHERE "Id" IN (SELECT "Id" FROM "__RetiredPostMenus");

                DELETE FROM "AbpPermissionGrants"
                WHERE "Name" IN (
                    'AbpAdmin.Posts', 'AbpAdmin.Posts.Create', 'AbpAdmin.Posts.Update',
                    'AbpAdmin.Posts.Delete', 'AbpAdmin.Posts.ManageMembers');
                DELETE FROM "AbpPermissions"
                WHERE "Name" IN (
                    'AbpAdmin.Posts', 'AbpAdmin.Posts.Create', 'AbpAdmin.Posts.Update',
                    'AbpAdmin.Posts.Delete', 'AbpAdmin.Posts.ManageMembers');
                """);

            migrationBuilder.DropTable(
                name: "AppIdentityUserPosts");

            migrationBuilder.DropTable(
                name: "AppPosts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 仅恢复原表结构；已退役的岗位、关联、菜单和授权数据不能由回滚重建。
            migrationBuilder.CreateTable(
                name: "AppIdentityUserPosts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppIdentityUserPosts", x => new { x.UserId, x.PostId });
                });

            migrationBuilder.CreateTable(
                name: "AppPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Remark = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppIdentityUserPosts_TenantId_PostId",
                table: "AppIdentityUserPosts",
                columns: new[] { "TenantId", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPosts_TenantId_Code",
                table: "AppPosts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPosts_TenantId_Name",
                table: "AppPosts",
                columns: new[] { "TenantId", "Name" },
                unique: true);
        }
    }
}
