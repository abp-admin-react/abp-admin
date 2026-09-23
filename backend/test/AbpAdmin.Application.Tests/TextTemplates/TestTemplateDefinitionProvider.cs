using Volo.Abp.TextTemplating;

namespace AbpAdmin.TextTemplates;

/// <summary>
/// 测试专用模板定义（T2.3 StoredTemplateContentContributorTests）。
/// 每个用例一个独立模板名，避免 contributor 缓存 key 在用例间串扰。
/// 这些模板没有虚拟文件基线：恢复默认后 ITemplateContentProvider 应返回 null。
/// </summary>
public class TestTemplateDefinitionProvider : TemplateDefinitionProvider
{
    public const string StoreOverride = "AbpAdmin.Test.StoreOverride";
    public const string CultureFallback = "AbpAdmin.Test.CultureFallback";
    public const string TenantIsolation = "AbpAdmin.Test.TenantIsolation";
    public const string RestoreDefault = "AbpAdmin.Test.RestoreDefault";
    public const string CacheInvalidation = "AbpAdmin.Test.CacheInvalidation";

    public override void Define(ITemplateDefinitionContext context)
    {
        context.Add(new TemplateDefinition(StoreOverride));
        context.Add(new TemplateDefinition(CultureFallback));
        context.Add(new TemplateDefinition(TenantIsolation));
        context.Add(new TemplateDefinition(RestoreDefault));
        context.Add(new TemplateDefinition(CacheInvalidation));
    }
}
