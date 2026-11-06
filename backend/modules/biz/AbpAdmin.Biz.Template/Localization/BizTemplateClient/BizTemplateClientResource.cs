using AbpAdmin.ClientErrors;
using Volo.Abp.Localization;

namespace AbpAdmin.Biz.Template.Localization;

/// <summary>
/// 客户静态文案资源。实现 <see cref="IClientStaticResource"/>，不接受数据库动态覆盖。
/// </summary>
[LocalizationResourceName("BizTemplateClient")]
public class BizTemplateClientResource : IClientStaticResource
{
}
