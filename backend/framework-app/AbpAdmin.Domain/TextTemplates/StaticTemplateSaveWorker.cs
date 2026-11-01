using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TextTemplating;
using Volo.Abp.Uow;

namespace AbpAdmin.TextTemplates;

/// <summary>
/// 将静态模板定义同步到数据库作为基线。
/// 只插入缺失的记录，不覆盖已有的用户修改。
/// 在 HttpApi.Host 启动时执行一次，DbMigrator 不执行。
/// </summary>
public class StaticTemplateSaveWorker : ITransientDependency
{
    private readonly ITemplateDefinitionManager _templateDefinitionManager;
    private readonly ITemplateContentProvider _templateContentProvider;
    private readonly IRepository<TextTemplateContent, Guid> _contentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IOptions<AbpAdminTextTemplateOptions> _options;
    private readonly ILogger<StaticTemplateSaveWorker> _logger;

    public StaticTemplateSaveWorker(
        ITemplateDefinitionManager templateDefinitionManager,
        ITemplateContentProvider templateContentProvider,
        IRepository<TextTemplateContent, Guid> contentRepository,
        ICurrentTenant currentTenant,
        IOptions<AbpAdminTextTemplateOptions> options,
        ILogger<StaticTemplateSaveWorker> logger)
    {
        _templateDefinitionManager = templateDefinitionManager;
        _templateContentProvider = templateContentProvider;
        _contentRepository = contentRepository;
        _currentTenant = currentTenant;
        _options = options;
        _logger = logger;
    }

    [UnitOfWork]
    public virtual async Task SaveStaticTemplatesToDatabaseAsync()
    {
        if (!_options.Value.SaveStaticTemplatesToDatabase)
        {
            _logger.LogDebug("SaveStaticTemplatesToDatabase is disabled, skipping static template save.");
            return;
        }

        _logger.LogInformation("Saving static template contents to database...");

        var definitions = await _templateDefinitionManager.GetAllAsync();
        var savedCount = 0;

        foreach (var definition in definitions)
        {
            // 跳过布局模板，布局模板不存储内容
            if (definition.IsLayout)
            {
                continue;
            }

            // 检查是否已存在（host 级别，TenantId 为 null）
            var exists = await _contentRepository.AnyAsync(x =>
                x.TenantId == null &&
                x.Name == definition.Name &&
                x.CultureName == null);

            if (exists)
            {
                continue;
            }

            // 获取静态内容
            string? content;
            try
            {
                content = await _templateContentProvider.GetContentOrNullAsync(definition);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get content for template {TemplateName}, skipping.", definition.Name);
                continue;
            }

            if (content == null)
            {
                _logger.LogDebug("Template {TemplateName} has no static content, skipping.", definition.Name);
                continue;
            }

            var entity = new TextTemplateContent(
                Guid.NewGuid(),
                tenantId: null, // host 级别基线
                name: definition.Name,
                content: content,
                cultureName: null);

            await _contentRepository.InsertAsync(entity);
            savedCount++;
        }

        _logger.LogInformation("Saved {Count} static template contents to database.", savedCount);
    }
}
