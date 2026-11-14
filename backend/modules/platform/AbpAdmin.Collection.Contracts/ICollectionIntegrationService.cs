using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 供订单等模块在进程内调用的收款契约。金额使用十进制字符串，经 <see cref="CollectionAmounts"/> 转换。
/// </summary>
[IntegrationService]
public interface ICollectionIntegrationService
{
    /// <summary>
    /// 创建一笔外部收款。同一业务单只允许一笔 Pending：已有 Pending 且渠道、金额、币种都相同
    /// 则返回原单。渠道字段为空且未在提交或关单时，可用原收款号重试渠道调用。
    /// 渠道、金额或币种不同则冲突。Stripe 剩余时间不足 30 分钟时拒绝。
    /// onPrepared 在收款单准备事务内运行，可绑定业务快照。事务提交之后再调用渠道。
    /// 渠道明确失败改为 ChannelFailed；超时或结果未知保持 Pending，使用原单号补查。
    /// </summary>
    Task<CollectionDto> CreateAsync(CreateCollectionInput input, Func<CollectionDto, Task>? onPrepared = null);

    /// <summary>
    /// 取消仍在等待的收款。关单确证后转 Closed，渠道已支付则核对实付事实。
    /// 关单结果未知时返回 Pending，调用方不能开启新的支付尝试。
    /// </summary>
    Task<CancelCollectionResult> CancelAsync(Guid collectionId);

    /// <summary>按收款号查询一笔。不按业务单号查询。</summary>
    Task<CollectionDto?> GetAsync(Guid collectionId);

    /// <summary>按业务单号返回该单的全部收款。补状态作业和换渠道前的检查用它。</summary>
    Task<List<CollectionDto>> ListByBusinessAsync(string businessType, Guid businessId);

    /// <summary>
    /// 扣 USDT 或 USDC（与订单标支付放在同一个工作单元里）。成功返回流水号。
    /// 余额不足抛业务异常。同一业务单已有未回冲扣款时：金额和币种相同返回原流水，否则冲突。
    /// </summary>
    Task<DebitBalanceResult> DebitBalanceAsync(DebitBalanceInput input);

    /// <summary>
    /// 回冲该业务单未回冲的扣款。同一单重复回冲只生效一次；没有未回冲扣款时直接成功。
    /// 调用方只能在订单仍不是 Paid、Fulfilled 时调用。
    /// </summary>
    Task ReverseDebitAsync(string businessType, Guid businessId);

    /// <summary>按业务单号读取未回冲扣款。没有则返回 null。</summary>
    Task<DebitDto?> GetDebitAsync(string businessType, Guid businessId);

    /// <summary>
    /// 分别读取游标后的新增充值和最近 7 天且序号不超过游标的充值，每段至多 maxCount 条。
    /// 游标只前进。消费失败的流水由调用方在重扫窗口内重试。
    /// </summary>
    Task<ListCreditedTxnsResult> ListCreditedTxnsAsync(long afterSeq, int maxCount);

    /// <summary>
    /// 返回该用户的 BSC 地址。扫链尚未成功跑过一轮时拒绝新派生（有地址可以返回，
    /// 同时标明充值未开放）。有副作用，不是纯查询。
    /// </summary>
    Task<DepositAddressDto> GetDepositAddressAsync(Guid userId);
}
