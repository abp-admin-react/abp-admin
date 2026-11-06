using Volo.Abp.Localization;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 客户静态错误资源。不接受数据库动态覆盖。
/// </summary>
[LocalizationResourceName("AbpAdminClientErrors")]
public class ClientErrorResource : IClientStaticResource
{
}
