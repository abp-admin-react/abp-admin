using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

/* 存量库打戳的迁移 ID 漂移钉死：打戳器把「Initial 迁移 ID」硬编码为常量记账，
 * 若 Migrations/ 下的 Initial 被重命名/重建而常量没跟，存量库会重新撞 42P07——
 * 打戳器要防的原始故障原样回归，且只影响存量库、全新库与单测全绿（最阴险的漂移方向）。
 * 此测试从迁移程序集读取真实迁移清单，把常量钉在事实上。
 */
public class EfCoreLegacySchemaBaselinerDriftTests : AbpAdminApplicationTestBase<AbpAdminEntityFrameworkCoreTestModule>
{
    [Fact]
    public async Task Framework_Initial_Migration_Id_Constant_Matches_Migrations_Assembly()
    {
        var dbContext = GetRequiredService<AbpAdminDbContext>();
        var migrations = dbContext.Database.GetMigrations();

        migrations.ShouldNotBeEmpty();
        // 字符串序最小者 = 时间戳最早的迁移 = Initial（EF 迁移 ID 以时间戳开头，按序应用）
        migrations.Min().ShouldBe(EfCoreLegacySchemaBaseliner.FrameworkInitialMigrationId);
    }
}
