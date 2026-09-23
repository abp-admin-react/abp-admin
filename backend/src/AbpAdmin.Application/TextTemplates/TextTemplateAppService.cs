using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Localization;
using Volo.Abp.TextTemplating;

namespace AbpAdmin.TextTemplates;

[Authorize(AbpAdminPermissions.TextTemplates.Default)]
public class TextTemplateAppService : AbpAdminAppService, ITextTemplateAppService
{
    private readonly ITemplateDefinitionManager _templateDefinitionManager;
    private readonly ITemplateContentProvider _templateContentProvider;
    private readonly IRepository<TextTemplateContent, Guid> _contentRepository;
    private readonly TemplateContentCacheCleaner _cacheCleaner;

    public TextTemplateAppService(
        ITemplateDefinitionManager templateDefinitionManager,
        ITemplateContentProvider templateContentProvider,
        IRepository<TextTemplateContent, Guid> contentRepository,
        TemplateContentCacheCleaner cacheCleaner)
    {
        _templateDefinitionManager = templateDefinitionManager;
        _templateContentProvider = templateContentProvider;
        _contentRepository = contentRepository;
        _cacheCleaner = cacheCleaner;
    }

    public virtual async Task<List<TextTemplateDto>> GetListAsync(string? filter = null)
    {
        var definitions = await _templateDefinitionManager.GetAllAsync();
        var result = new List<TextTemplateDto>();
        foreach (var definition in definitions)
        {
            result.Add(new TextTemplateDto
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName?.Localize(StringLocalizerFactory),
                IsLayout = definition.IsLayout,
                Layout = definition.Layout,
                IsSandboxed = IsSandboxedEngine(definition.RenderEngine)
            });
        }

        var query = result.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (x.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.OrderBy(x => x.Name).ToList();
    }

    public virtual async Task<TextTemplateDto> GetContentAsync(string name, string? cultureName = null)
    {
        // 未知模板名直接报错：静默返回空 DTO 会让前端把它渲染成可编辑态，
        // 保存后凭空创建孤儿覆盖记录
        var definition = await GetRequiredDefinitionAsync(name);

        // 现在数据库内容已经在 provider 的管线里了，直接调用即可
        var content = await _templateContentProvider.GetContentOrNullAsync(definition, cultureName);

        return new TextTemplateDto
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName?.Localize(StringLocalizerFactory) ?? definition.Name,
            IsLayout = definition.IsLayout,
            Layout = definition.Layout,
            CultureName = cultureName,
            Content = content,
            IsSandboxed = IsSandboxedEngine(definition.RenderEngine)
        };
    }

    [Authorize(AbpAdminPermissions.TextTemplates.Update)]
    public virtual async Task UpdateAsync(UpdateTextTemplateDto input)
    {
        // 未知模板名拒绝落库（防止凭空创建孤儿覆盖记录）
        var definition = await GetRequiredDefinitionAsync(input.Name);

        // 检查沙箱权限
        if (!IsSandboxedEngine(definition.RenderEngine))
        {
            // 非沙箱引擎需要额外权限
            if (!await AuthorizationService.IsGrantedAsync(AbpAdminPermissions.TextTemplates.EditNonSandboxedContents))
            {
                Logger.LogWarning("[UpdateAsync] User does not have EditNonSandboxedContents permission for non-sandboxed template: {Name}", input.Name);
                throw new AbpAuthorizationException($"Template '{input.Name}' uses a non-sandboxed rendering engine. You need the '{AbpAdminPermissions.TextTemplates.EditNonSandboxedContents}' permission to edit it.");
            }
        }

        var query = await _contentRepository.GetQueryableAsync();
        var stored = await _contentRepository.AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x => x.TenantId == CurrentTenant.Id && x.Name == input.Name && x.CultureName == input.CultureName));

        if (stored == null)
        {
            stored = new TextTemplateContent(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                input.Name,
                input.Content,
                input.CultureName);
            await _contentRepository.InsertAsync(stored);
        }
        else
        {
            stored.SetContent(input.Content);
            await _contentRepository.UpdateAsync(stored);
        }

        // 作废缓存：版本号替换一次作废该模板当前租户下所有文化的条目（提交后执行，见 InvalidateCacheOnCompleted）
        InvalidateCacheOnCompleted(input.Name);
    }

    [Authorize(AbpAdminPermissions.TextTemplates.Update)]
    public virtual async Task RestoreToDefaultAsync(RestoreTextTemplateToDefaultInput input)
    {
        var query = await _contentRepository.GetQueryableAsync();

        // 如果是恢复文化无关的默认内容，需要删除所有文化的覆盖记录
        if (input.CultureName == null)
        {
            // 删除当前租户下该模板的所有覆盖记录（包括所有文化），批量一次删除
            var allOverrides = await _contentRepository.AsyncExecuter.ToListAsync(
                query.Where(x => x.TenantId == CurrentTenant.Id && x.Name == input.Name));

            if (allOverrides.Count == 0)
            {
                Logger.LogWarning("[RestoreToDefaultAsync] No override record found for template: {Name}", input.Name);
            }

            await _contentRepository.DeleteManyAsync(allOverrides);
        }
        else
        {
            // 只删除指定文化的覆盖记录
            var stored = await _contentRepository.AsyncExecuter.FirstOrDefaultAsync(
                query.Where(x => x.TenantId == CurrentTenant.Id && x.Name == input.Name && x.CultureName == input.CultureName));

            if (stored != null)
            {
                await _contentRepository.DeleteAsync(stored);
            }
            else
            {
                Logger.LogWarning("[RestoreToDefaultAsync] No override record found for template: {Name}, culture: {Culture}",
                    input.Name, input.CultureName ?? "null");
            }
        }

        // 作废缓存（提交后执行，同 UpdateAsync）：版本号替换对该模板所有文化一次性生效
        InvalidateCacheOnCompleted(input.Name);
    }

    /// <summary>
    /// 注册"事务提交后作废该模板当前租户所有文化缓存"的回调（UpdateAsync 与 RestoreToDefaultAsync 共用）。
    /// 作废方式：TemplateContentCacheCleaner.ClearCacheAsync 版本号替换，一次覆盖该模板所有文化条目。
    /// 必须挂在 UoW 提交后执行：AppService 方法结束时才提交事务，
    /// 提交前换版本会在"版本已换、事务未提交"窗口里被并发读按新版本 key 回填旧值。
    /// 回调执行时事务已提交：失效失败只造成最长一个 TTL 的脏读，降级记 Warning，不应让客户端收到 500。
    /// </summary>
    private void InvalidateCacheOnCompleted(string templateName)
    {
        UnitOfWorkManager.Current.OnCompleted(async () =>
        {
            try
            {
                await _cacheCleaner.ClearCacheAsync(templateName);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "文本模板缓存失效失败，Name={Name}", templateName);
            }
        });
    }

    /// <summary>
    /// 获取模板定义，不存在时抛 TemplateNotFound（GetContentAsync 与 UpdateAsync 共用）。
    /// </summary>
    private async Task<TemplateDefinition> GetRequiredDefinitionAsync(string name)
    {
        return await _templateDefinitionManager.GetOrNullAsync(name)
            ?? throw new BusinessException(AbpAdminDomainErrorCodes.TextTemplates.TemplateNotFound)
                .WithData("Name", name);
    }

    /// <summary>
    /// 判断渲染引擎是否为沙箱引擎（名单匹配不区分大小写）。
    /// 已知沙箱引擎：Scriban；未指定引擎时也视为沙箱（与 ABP 默认渲染引擎 Scriban 一致）。
    /// 未知引擎按非沙箱处理（安全起见，需 EditNonSandboxedContents 权限）。
    /// </summary>
    private static bool IsSandboxedEngine(string? renderEngine)
    {
        // 未指定引擎时，ABP 默认使用 Scriban（沙箱）
        if (string.IsNullOrEmpty(renderEngine))
        {
            return true;
        }

        // 已知沙箱引擎名单（OrdinalIgnoreCase 匹配，无需枚举大小写变体）
        var sandboxedEngines = new[] { "Scriban" };

        return sandboxedEngines.Contains(renderEngine, StringComparer.OrdinalIgnoreCase);
    }
}
