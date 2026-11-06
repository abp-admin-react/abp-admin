using System.Linq;
using AbpAdmin.Biz.Template.Client.Controllers;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AbpAdmin.Biz.Template.Client;

/// <summary>
/// 样例关闭时从 MVC 应用模型移除客户控制器，生产默认不发现该入口。
/// </summary>
public sealed class BizProjectClientControllerConvention : IApplicationModelConvention
{
    private readonly bool _enabled;

    public BizProjectClientControllerConvention(bool enabled)
    {
        _enabled = enabled;
    }

    public bool IsSampleEnabled => _enabled;

    public void Apply(ApplicationModel application)
    {
        if (_enabled)
        {
            return;
        }

        var controllers = application.Controllers
            .Where(controller => controller.ControllerType == typeof(BizProjectClientController))
            .ToList();

        foreach (var controller in controllers)
        {
            application.Controllers.Remove(controller);
        }
    }
}
