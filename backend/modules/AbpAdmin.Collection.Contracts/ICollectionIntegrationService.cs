using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 收款集成服务（计划第 3.1 节）。订单模块只依赖本契约程序集，收款实现模块提供实现；
/// 同进程调用仍是本地 DI。金额一律十进制字符串，经 <see cref="CollectionAmounts"/> 解析。
/// 用户和管理端看余额、流水，走收款模块自己的应用服务，不放进这里。
/// </summary>
[IntegrationService]
public interface ICollectionIntegrationService
{
    /// <summary>
    /// 创建一笔外部收款。同一业务单只允许一笔 Pending：已有 Pending 且渠道、金额、币种都相同
    /// 则返回原单（渠道字段为空时用同一收款号重试渠道调用）；渠道不同则冲突（调用方先取消）；
    /// 金额或币种不同则冲突。Stripe 剩余时间不足 30 分钟时直接拒绝不插行。
    /// 本地插入与渠道回写都用 requiresNew 事务并在渠道调用前提交；渠道 HTTP 在事务之外。
    /// 渠道明确失败改为 ChannelFailed（释放 Pending 唯一索引）；超时或结果未知保持 Pending 并标未知。
    /// </summary>
    Task<CollectionDto> CreateAsync(CreateCollectionInput input);

    /// <summary>
    /// 取消仍在等待的收款。本地先改 Closed 并提交（requiresNew），渠道关单在事务外；
    /// 渠道返回「订单不存在」、超时或网络失败：本地仍为 Closed 并告警（「订单不存在」视为关单成功）。
    /// 收款单不是 Pending 时不请求渠道关单，返回当前状态，不抛异常——调用方收到已完成时，
    /// 按金额币种走支付或异常，不再创建新收款。
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
    /// 按单调序号列出已入账的充值流水，并重扫最近 7 天（计划第 3.1 节的补漏规则）。
    /// 单行失败跳过并告警，游标推进到本批最大序号（含跳过的行）。
    /// </summary>
    Task<ListCreditedTxnsResult> ListCreditedTxnsAsync(long afterSeq, int maxCount);

    /// <summary>
    /// 返回该用户的 BSC 地址。扫链尚未成功跑过一轮时拒绝新派生（有地址可以返回，
    /// 同时标明充值未开放）。有副作用，不是纯查询。
    /// </summary>
    Task<DepositAddressDto> GetDepositAddressAsync(Guid userId);
}
