using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ordering.Stubs;

/// <summary>
/// 集成服务桩（计划第 7 节 M1：「桩放在订单测试工程，用 ReplaceServices 替换集成服务」）。
/// 状态全部保存在单个测试用例内（每用例重启应用 + Respawn 清表，桩也随之重建）；
/// 用例通过属性配置渠道行为后调 OrderPaymentManager/应用服务，断言记录的调用。
/// </summary>
public class StubCollectionIntegrationService : ICollectionIntegrationService, ITransientDependency
{
    private int _seq;

    public int CreateCallCount { get; private set; }

    public int DebitCallCount { get; private set; }

    public int CancelCallCount { get; private set; }

    /// <summary>计数与字典操作的互斥锁：并发支付用例下多线程共享同一个单例桩。</summary>
    private readonly object _gate = new();

    /// <summary>CancelAsync 对某个收款单返回的状态（默认 Closed）。</summary>
    public System.Collections.Generic.Dictionary<Guid, CollectionStatus> CancelOutcomes { get; } = new();

    /// <summary>按业务单号存放的收款单列表（ListByBusinessAsync / 补状态作业用）。</summary>
    public System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CollectionDto>> BusinessCollections { get; } = new();

    /// <summary>DebitBalanceAsync 抛出的异常（余额不足等）；为 null 时成功。</summary>
    public System.Exception? DebitException { get; set; }

    /// <summary>CreateAsync 抛出的异常（渠道拒绝 / 上限 / Stripe 时限）。</summary>
    public System.Exception? CreateException { get; set; }

    public Task<CollectionDto> CreateAsync(CreateCollectionInput input)
    {
        lock (_gate) { CreateCallCount++; }
        if (CreateException != null)
        {
            throw CreateException;
        }

        var dto = new CollectionDto
        {
            Id = Guid.NewGuid(),
            CollectionNo = "STUB" + Interlocked.Increment(ref _seq),
            BusinessType = input.BusinessType,
            BusinessId = input.BusinessId,
            UserId = input.UserId,
            Channel = input.Channel,
            Currency = input.Currency,
            Amount = CollectionAmounts.Format(CollectionAmounts.Parse(input.Amount)),
            Status = CollectionStatus.Pending,
            ExpiresAt = input.ExpiresAt,
            ChannelPayload = "stub://" + input.Channel,
        };

        Track(dto);
        return Task.FromResult(dto);
    }

    public Task<CancelCollectionResult> CancelAsync(Guid collectionId)
    {
        lock (_gate) { CancelCallCount++; }
        var status = CancelOutcomes.TryGetValue(collectionId, out var s) ? s : CollectionStatus.Closed;
        var dto = Find(collectionId);
        if (dto != null)
        {
            dto.Status = status;
        }

        return Task.FromResult(new CancelCollectionResult
        {
            Collection = dto ?? new CollectionDto
            {
                Id = collectionId,
                CollectionNo = "STUB-CANCELLED",
                Status = status,
                Amount = "0",
                Channel = CollectionChannel.WechatNative,
                Currency = CollectionCurrency.Cny,
            },
        });
    }

    public Task<CollectionDto?> GetAsync(Guid collectionId)
    {
        return Task.FromResult(Find(collectionId));
    }

    public Task<System.Collections.Generic.List<CollectionDto>> ListByBusinessAsync(string businessType, Guid businessId)
    {
        return Task.FromResult(BusinessCollections.TryGetValue(Key(businessType, businessId), out var list)
            ? list
            : []);
    }

    public Task<DebitBalanceResult> DebitBalanceAsync(DebitBalanceInput input)
    {
        lock (_gate) { DebitCallCount++; }
        if (DebitException != null)
        {
            throw DebitException;
        }

        return Task.FromResult(new DebitBalanceResult { BalanceTxnId = Guid.NewGuid() });
    }

    /// <summary>按业务单号存放的未回冲扣款（GetDebitAsync；补状态作业 M2 步骤用例配置）。</summary>
    public System.Collections.Generic.Dictionary<string, DebitDto> Debits { get; } = new();

    /// <summary>ReverseDebitAsync 的调用记录（businessId）。</summary>
    public System.Collections.Generic.List<Guid> ReversedBusinessIds { get; } = new();

    /// <summary>配置某个业务单的未回冲扣款（键格式是桩的私有细节，测试不手拼）。</summary>
    public void SetDebit(string businessType, Guid businessId, DebitDto dto)
        => Debits[Key(businessType, businessId)] = dto;

    /// <summary>ListCreditedTxnsAsync 返回的已入账充值流水（按 Seq 排好后塞入）。</summary>
    public System.Collections.Generic.List<CreditedTxnDto> CreditedTxns { get; } = new();

    public Task ReverseDebitAsync(string businessType, Guid businessId)
    {
        lock (_gate) { ReversedBusinessIds.Add(businessId); }
        Debits.Remove(Key(businessType, businessId));
        return Task.CompletedTask;
    }

    public Task<DebitDto?> GetDebitAsync(string businessType, Guid businessId)
        => Task.FromResult(Debits.TryGetValue(Key(businessType, businessId), out var dto) ? dto : null);

    public Task<ListCreditedTxnsResult> ListCreditedTxnsAsync(long afterSeq, int maxCount)
    {
        // 对齐契约（与真实现同一语义）：正向序号段 + 最近 7 天重扫段（游标之前、窗口之内）。
        // 没有重扫腿的话，作业级幂等重跑测试拿不到任何行，断言空转
        var cutoff = DateTime.Now.AddDays(-7);
        var forward = CreditedTxns.Where(t => t.Seq > afterSeq).OrderBy(t => t.Seq).Take(maxCount).ToList();
        var rescan = CreditedTxns
            .Where(t => t.Seq <= afterSeq && t.CreditedAt >= cutoff)
            .OrderBy(t => t.Seq).Take(maxCount).ToList();
        var items = forward.Concat(rescan).DistinctBy(t => t.Seq).OrderBy(t => t.Seq).ToList();
        return Task.FromResult(new ListCreditedTxnsResult
        {
            Items = items,
            Cursor = items.Count > 0 ? Math.Max(afterSeq, items.Max(t => t.Seq)) : afterSeq,
        });
    }

    public Task<DepositAddressDto> GetDepositAddressAsync(Guid userId)
        => throw new BusinessException(CollectionErrorCodes.DepositNotOpen);

    // ── 内部：把桩里的收款单按业务单号归档，供 ListByBusinessAsync 返回 ─────────────────

    public void Track(CollectionDto dto)
    {
        var key = Key(dto.BusinessType, dto.BusinessId);
        if (!BusinessCollections.TryGetValue(key, out var list))
        {
            list = [];
            BusinessCollections[key] = list;
        }

        list.RemoveAll(x => x.Id == dto.Id);
        list.Add(dto);
    }

    private CollectionDto? Find(Guid collectionId)
    {
        foreach (var list in BusinessCollections.Values)
        {
            var hit = list.FirstOrDefault(x => x.Id == collectionId);
            if (hit != null)
            {
                return hit;
            }
        }

        return null;
    }

    private static string Key(string businessType, Guid businessId) => businessType + ":" + businessId.ToString("N");
}
