using System;
using System.Linq;
using System.Reflection;
using AbpAdmin.Biz.Template.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Xunit;

namespace AbpAdmin.Biz.Template;

/* 模块 Initial 迁移 ID 漂移钉死：BizTemplateConsts.InitialMigrationId 供存量库打戳
 * （EfCoreLegacySchemaBaseliner）记账用——若 Migrations/ 下 Initial 被重命名/重建而常量
 * 没跟，派生项目的存量库会重新撞 42P07 且单测全绿。反射读迁移程序集的真实清单钉住常量。
 * 样板复制改名后本测试随工程一起复制，继续守卫改名后的 Initial。
 */
public class BizTemplateInitialMigrationIdDriftTests
{
    [Fact]
    public void Initial_Migration_Id_Constant_Matches_Migrations_Assembly()
    {
        var migrationIds = typeof(BizTemplateDbSchemaMigrator).Assembly
            .GetTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>())
            .Where(m => m != null)
            .Select(m => m!.Id)
            .ToList();

        migrationIds.ShouldNotBeEmpty();
        // 字符串序最小者 = 时间戳最早的迁移 = Initial（EF 迁移 ID 以时间戳开头，按序应用）
        migrationIds.Min().ShouldBe(BizTemplateConsts.InitialMigrationId);
    }
}
