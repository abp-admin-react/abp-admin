using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.EventBus.Distributed;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 写入链上告警并向配置的宿主用户发送站内信。网页尚未配置收件人时采用部署默认值，
/// 收件人为空时只写日志。冷却时间由调用方按告警对象维护。
/// </summary>
public class ChainAlertService
{
    private readonly Guid[] _recipients;
    private readonly IDistributedEventBus _eventBus;
    private readonly ILogger<ChainAlertService> _logger;
    private readonly IServiceScopeFactory? _scopeFactory;

    public ChainAlertService(Guid[] recipients, IDistributedEventBus eventBus, ILogger<ChainAlertService> logger,
        IServiceScopeFactory? scopeFactory = null)
    {
        _recipients = recipients;
        _eventBus = eventBus;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <param name="prefix">固定前缀（CollectionConsts.AlertPrefix*）或 null（仅普通 WRN）。</param>
    public virtual async Task RaiseAsync(string? prefix, string title, string detail)
    {
        if (prefix != null)
        {
            // 将固定前缀置于消息开头，便于按前缀采集。
            _logger.LogError("{Prefix}{Title}: {Detail}", prefix, title, detail);
        }
        else
        {
            _logger.LogWarning("[Collection] {Title}: {Detail}", title, detail);
        }

        try
        {
            using var scope = _scopeFactory?.CreateScope();
            var runtime = scope == null ? null
                : await scope.ServiceProvider.GetRequiredService<ChainRuntimeSettings>().ReadAsync();
            var recipients = runtime?.AlertRecipientUserIds ?? _recipients;
            if (recipients.Length == 0)
            {
                _logger.LogWarning("链上告警未设置站内信收件人：{Title}", title);
                return;
            }

            await _eventBus.PublishAsync(new AbpAdmin.Notifications.CreateInAppNotificationEto(
                null, recipients, title, body: detail));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "站内信告警发布失败（{Title}）；日志侧告警已落", title);
        }
    }

    /// <summary>尚未发送过告警或已过冷却期时允许告警。</summary>
    public static bool ShouldAlert(DateTime? lastAlertedAt, TimeSpan cooldown, DateTime now)
    {
        return lastAlertedAt == null || now - lastAlertedAt.Value >= cooldown;
    }
}
