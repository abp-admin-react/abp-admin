using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 共享确认回执 Gas 实耗台账落库器（<see cref="SweepManager"/> 与提现广播共用，避免重复台账逻辑）。
/// 按 (NetworkId, TxHash) 幂等写入；回执未返回 GasUsed 时跳过（兼容不模拟 gasUsed 的离线测试）。
/// </summary>
internal static class ChainGasFeeRecorder
{
    internal static async Task TryRecordAsync(
        IUnitOfWorkManager uowManager,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IClock clock,
        Guid networkId,
        ChainTxPurpose purpose,
        Guid? refId,
        string txHash,
        TransactionReceipt receipt,
        string? fallbackGasPriceWei,
        string? fallbackSenderAddress,
        CancellationToken ct)
    {
        if (receipt.GasUsed is not { } gasUsedBig || gasUsedBig <= 0 || gasUsedBig > long.MaxValue)
        {
            return;
        }

        var normalizedHash = txHash.ToLowerInvariant();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var db = await dbContextProvider.GetDbContextAsync();

        if (await db.GasFeeLogs.AsNoTracking().AnyAsync(g => g.NetworkId == networkId && g.TxHash == normalizedHash, ct))
        {
            await uow.CompleteAsync(ct);
            return;
        }

        var attempt = await db.ChainTxAttempts.AsNoTracking()
            .Where(a => a.NetworkId == networkId && a.TxHash == normalizedHash)
            .Select(a => new { a.SenderAddress, a.GasPriceWei })
            .FirstOrDefaultAsync(ct);

        var priceWei = receipt.EffectiveGasPriceWei is { } effWei && effWei > 0
            ? effWei
            : BscGasPricePolicy.ParseWeiOrZero(fallbackGasPriceWei ?? attempt?.GasPriceWei);
        if (priceWei <= 0)
        {
            await uow.CompleteAsync(ct);
            return;
        }

        var sender = (fallbackSenderAddress ?? attempt?.SenderAddress ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrEmpty(sender))
        {
            sender = "0x0000000000000000000000000000000000000000";
        }

        var gasUsed = (long)gasUsedBig;
        var feeBnb = BscGasPricePolicy.WeiToEther(gasUsedBig * priceWei);
        db.GasFeeLogs.Add(new ColGasFeeLog(Guid.NewGuid())
        {
            NetworkId = networkId,
            Purpose = purpose,
            RefId = refId,
            TxHash = normalizedHash,
            SenderAddress = sender,
            BlockNumber = receipt.BlockNumber,
            GasUsed = gasUsed,
            EffectiveGasPriceWei = priceWei.ToString(CultureInfo.InvariantCulture),
            FeeBnb = feeBnb,
            CreationTime = clock.Now,
        });
        await db.SaveChangesAsync(ct);
        await uow.CompleteAsync(ct);
    }
}
