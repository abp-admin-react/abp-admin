using System.Globalization;
using Volo.Abp.Modularity;

namespace AbpAdmin.Biz.Template;

/// <summary>
/// 模块测试基类：AbpIntegratedTest 每个测试实例独立启动一次应用（独立内存 SQLite），
/// 测试之间零共享。启动链：BizTemplateTestModule（建表）→ AbpAdminBizTemplateModule（被测）
/// → AbpAdminTestBaseModule（AlwaysAllow 授权 + IDataSeeder 启动种子）。
/// </summary>
public abstract class BizTemplateTestBase : AbpAdminTestBase<BizTemplateTestModule>
{
    protected BizTemplateTestBase()
    {
        // 文化在实例构造器里固定（xUnit 每个用例可能在不同线程执行，模块初始化里设置不跟随）：
        // 测试进程 UI 文化随开发机系统语言（如 zh-CN），模块资源只注册 en/zh-Hans，
        // zh-CN 查不到会退化成裸键（L["X"] 返回 "X"），断言随机器漂移。固定 en 保持稳定。
        CultureInfo.CurrentCulture = new CultureInfo("en");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
    }
}
