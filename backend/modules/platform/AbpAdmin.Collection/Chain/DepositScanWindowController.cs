using System;
using System.Collections.Concurrent;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 充值扫链 AIMD 自适应块窗控制器：遇到节点区间限制（RangeIncomplete）时乘性减半，
/// 整窗完整读取后加性缓慢回升，上限受限于配置的 blockRange。
/// </summary>
public sealed class DepositScanWindowController
{
    private readonly ConcurrentDictionary<(Guid TokenId, Guid ProviderId), int> _windows = new();

    public int GetWindow(Guid tokenId, Guid providerId, int configuredMax)
    {
        var max = Math.Max(1, configuredMax);
        var current = _windows.GetOrAdd((tokenId, providerId), max);
        return Math.Clamp(current, 1, max);
    }

    public void RecordIncomplete(Guid tokenId, Guid providerId, int attemptedWindow, int configuredMax)
    {
        var max = Math.Max(1, configuredMax);
        var halved = Math.Clamp(Math.Max(1, attemptedWindow / 2), 1, max);
        _windows[(tokenId, providerId)] = halved;
    }

    public void RecordComplete(Guid tokenId, Guid providerId, int completedWindow, int requestedWindow, int configuredMax)
    {
        var max = Math.Max(1, configuredMax);
        if (completedWindow < requestedWindow || completedWindow <= 0)
        {
            return;
        }

        var step = Math.Max(1, Math.Min(completedWindow / 2, 50));
        var next = Math.Min(max, completedWindow + step);
        _windows[(tokenId, providerId)] = next;
    }

    public void Reset(Guid tokenId, Guid providerId)
    {
        _windows.TryRemove((tokenId, providerId), out _);
    }
}
