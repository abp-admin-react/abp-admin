using System.Collections.Generic;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.ClientErrors;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Biz.Template.ClientErrors;

/// <summary>
/// 模块公开错误声明。模块移除即从目录消失；无异常原文参数。
/// </summary>
[ExposeServices(typeof(IPublicErrorDefinitionProvider))]
public class BizTemplatePublicErrorDefinitions : IPublicErrorDefinitionProvider, ISingletonDependency
{
    public IEnumerable<PublicErrorDefinition> GetDefinitions()
    {
        yield return new PublicErrorDefinition
        {
            InternalCode = BizTemplateClientErrorCodes.ProjectUnavailable,
            PublicCode = BizTemplateClientErrorCodes.ProjectUnavailablePublic,
            ResourceType = typeof(BizTemplateClientResource),
            MessageKey = "ProjectUnavailable",
            Kind = PublicErrorKind.Business,
            PublicHttpStatuses = [403, 404],
        };
    }
}
