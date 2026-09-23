using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using EasyAbp.Abp.DataDictionary;
using Microsoft.AspNetCore.Http;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin;

/* Inherit your application services from this class.
 */
public abstract class AbpAdminAppService : ApplicationService
{
    protected AbpAdminAppService()
    {
        LocalizationResource = typeof(AbpAdminResource);
    }

    /// <summary>
    /// 当前 HTTP 请求的中止令牌（审查轮从三处私有片段收拢）：下游耗时调用（如 /connect/token
    /// 换票）可据此提前终止。非 Web 宿主/测试环境没有 HttpContext，回落 CancellationToken.None。
    /// </summary>
    protected CancellationToken GetRequestAbortedOrNone()
    {
        return LazyServiceProvider.LazyGetService<IHttpContextAccessor>()?.HttpContext?.RequestAborted
               ?? CancellationToken.None;
    }

    /// <summary>
    /// Host 专属资源的双保险守卫：权限定义虽已是 MultiTenancySides.Host（租户侧拿不到授权），
    /// 仍显式拒绝租户上下文的直接调用（防权限误授/内部误用）。Editions、TenantPackages 共用，
    /// 消除各服务私有拷贝的漂移。
    /// </summary>
    protected virtual void EnsureHostSide()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.HostSideOnly);
        }
    }

    /// <summary>
    /// 字典渲染器（T3.4 第 9 步）。模块没有自动拦截的 MVC Filter，必须显式调用。
    /// </summary>
    protected IDataDictionaryRenderer DataDictionaryRenderer
        => LazyServiceProvider.LazyGetRequiredService<IDataDictionaryRenderer>();

    /// <summary>
    /// 渲染 DTO 上标注了 [DictionaryRenderField] 的字段，返回替换后的分页结果（必须接返回值）。
    /// 取舍：普通列表查询接口不要调（白增数据量，前端用 dictionaryValueEnum 自己渲染）；
    /// 只在导出、邮件/短信模板、报表这类「没有前端参与」的路径上调。
    /// 注意：渲染值走模块的分布式缓存且无失效逻辑，管理页改了显示名后渲染路径要等缓存 TTL 过期。
    /// </summary>
    protected virtual async Task<PagedResultDto<TDto>> RenderDictionaryAsync<TDto>(
        PagedResultDto<TDto> result)
    {
        result.Items = await DataDictionaryRenderer.RenderListAsync(result.Items);
        return result;
    }

    /// <summary>
    /// 渲染单个 DTO 上标注了 [DictionaryRenderField] 的字段（必须接返回值）。
    /// </summary>
    protected virtual Task<TDto> RenderDictionaryAsync<TDto>(TDto dto)
    {
        return DataDictionaryRenderer.RenderAsync(dto);
    }
}
