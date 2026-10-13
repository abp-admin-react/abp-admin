using System;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 充值入账幂等键的读写双方唯一定义处：DepositCredit 流水的 IdempotencyKey =
/// 进款单 Id 的 "D" 格式。写入（归集确认入账 SweepManager）与读取（用户流水回填
/// 链上凭证 CollectionBalanceAppService）都从这里取口径——格式变更只改这一个文件，
/// 不会静默退化成"用户看不见凭证"。
/// </summary>
public static class BalanceIdempotencyKeys
{
    public static string ForDepositOrder(Guid depositOrderId) => depositOrderId.ToString("D");

    public static bool TryParseDepositOrder(string? idempotencyKey, out Guid depositOrderId)
        => Guid.TryParseExact(idempotencyKey, "D", out depositOrderId);
}
