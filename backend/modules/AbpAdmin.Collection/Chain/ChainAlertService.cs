using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.EventBus.Distributed;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 链上告警出口（计划第 5.6 节）：三类固定前缀（Gas 过低/归集卡住/代币暂停）供日志采集按前缀告警；
/// 同时发站内信给配置的 Host 用户名单（Collection:Chain:AlertRecipientUserIds，逗号分隔）。
/// 名单为空 → LogError 且不发（不把通知标成已送达）；站内信用户离线时推送为空操作、记录留收件箱。
/// 同类告警对同一主体的冷却由调用方持 ColNetworks 上的 AlertedAt 列控制。
/// 收件人由模块装配注入（配置解析一次；构造注入保证可测，不在每轮读配置）。
/// </summary>
public class ChainAlertService
{
    private readonly Guid[] _recipients;
    private readonly IDistributedEventBus _eventBus;
    private readonly ILogger<ChainAlertService> _logger;

    public ChainAlertService(Guid[] recipients, IDistributedEventBus eventBus, ILogger<ChainAlertService> logger)
    {
        _recipients = recipients;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <param name="prefix">固定前缀（CollectionConsts.AlertPrefix*）或 null（仅普通 WRN）。</param>
    public virtual async Task RaiseAsync(string? prefix, string title, string detail)
    {
        if (prefix != null)
        {
            // 固定前缀单独成行：日志采集按行首前缀告警（计划第 5.6 节）
            _logger.LogError("{Prefix}{Title}: {Detail}", prefix, title, detail);
        }
        else
        {
            _logger.LogWarning("[Collection] {Title}: {Detail}", title, detail);
        }

        if (_recipients.Length == 0)
        {
            _logger.LogError(
                "链上告警无人可收（Collection:Chain:AlertRecipientUserIds 为空）：{Title}。请配置收件人，否则告警只落日志",
                title);
            return;
        }

        try
        {
            // 走 Domain 侧的 CreateInAppNotificationEventHandler（发布 ETO 即达；离线用户推送为空操作、留收件箱）
            await _eventBus.PublishAsync(new AbpAdmin.Notifications.CreateInAppNotificationEto(
                null, _recipients, title, body: detail));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "站内信告警发布失败（{Title}）；日志侧告警已落", title);
        }
    }

    /// <summary>冷却写回助手：同一类告警对同一主体在上次触发后 cooldown 内不重复（网络行上的 AlertedAt 列）。</summary>
    public static bool ShouldAlert(DateTime? lastAlertedAt, TimeSpan cooldown, DateTime now)
    {
        return lastAlertedAt == null || now - lastAlertedAt.Value >= cooldown;
    }
}
