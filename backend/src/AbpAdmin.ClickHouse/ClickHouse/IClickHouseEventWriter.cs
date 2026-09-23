using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.ClickHouse;

public interface IClickHouseEventWriter
{
    /// <summary>
    /// 入队采集事件:零阻塞、不抛异常。缓冲满(DropOldest)时静默挤掉最旧事件。
    /// 刻意命名 Enqueue 而非 TryEnqueue:DropOldest 下"尝试失败"不存在(恒入队),
    /// 也无返回值可查——丢弃语义由缓冲容量预算承载,不反压业务线程。
    /// </summary>
    void Enqueue(CollectedEvent collectedEvent);

    /// <summary>
    /// 把当前缓冲批量写入 CH,返回成功写入行数;失败时整批退回缓冲等待下一轮。
    /// 供测试/运维手动冲刷与停机兜底调用,业务侧只需 <see cref="Enqueue"/>——后台循环才是常规冲刷方。
    /// 与后台循环并发调用是安全的(门闩串行化,拿不到锁直接返回 0)。
    /// 注意交付语义是 at-least-once:结果未知的失败(超时但服务端已提交)重放可能产生重复行,顺序不保证;
    /// 调用方令牌取消(含写入中途取消)会向本方法抛 OCE,在途批已先退回缓冲不丢数据。
    /// </summary>
    Task<int> FlushAsync(CancellationToken cancellationToken = default);
}
