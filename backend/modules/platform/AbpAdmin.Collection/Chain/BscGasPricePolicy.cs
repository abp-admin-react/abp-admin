using System;
using System.Globalization;
using System.Numerics;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// BSC legacy <c>gasPrice</c> 策略与单位换算工具。
/// 首笔按节点当前价；每次重签或同 nonce 替换时按档位抬价，并在低 gasPrice 下追加 +bumpSteps wei 打破同哈希死锁。
/// </summary>
public static class BscGasPricePolicy
{
    /// <summary>乘性抬价最大档数（每档 ×1.25）。超过后仅叠加 wei，避免打油金额失控。</summary>
    public const int MaxBumpSteps = 8;

    /// <summary>单档乘数分子（5/4 = 1.25）。</summary>
    public const int BumpFactorNum = 5;

    /// <summary>单档乘数分母。</summary>
    public const int BumpFactorDen = 4;

    /// <summary>geth/BSC txpool 同 nonce 替换最低加价百分比（+10%）。</summary>
    public const int ReplacePriceBumpPercent = 10;

    /// <summary>原生 BNB 转账 gas 上限。</summary>
    public const int NativeTransferGasLimit = 21_000;

    /// <summary>ERC20 transfer 调用 gas 上限。</summary>
    public const int Erc20TransferGasLimit = 100_000;

    private static readonly BigInteger WeiPerEther = BigInteger.Parse("1000000000000000000", CultureInfo.InvariantCulture);

    /// <summary>
    /// 按重签档位抬高 gasPrice。≤0 档原样返回；1..MaxBumpSteps 乘性 ×1.25 并加 bumpSteps wei，
    /// 保证在低 gasPrice（如 BSC 0.05 Gwei）下仍严格递增并产出新交易哈希。
    /// </summary>
    public static BigInteger ApplyBump(BigInteger networkWei, int bumpSteps)
    {
        if (networkWei < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(networkWei), "networkWei cannot be negative.");
        }

        if (bumpSteps <= 0)
        {
            return networkWei;
        }

        var multSteps = Math.Min(bumpSteps, MaxBumpSteps);
        var wei = networkWei;
        for (var i = 0; i < multSteps; i++)
        {
            wei = wei * BumpFactorNum / BumpFactorDen;
        }

        wei += bumpSteps;
        if (wei <= networkWei)
        {
            wei = networkWei + bumpSteps;
        }

        return wei;
    }

    /// <summary>
    /// 同 nonce 加速替换使用的 gasPrice：至少旧价 × 110% + 1 wei，且不低于 <paramref name="desiredWei"/>。
    /// </summary>
    public static BigInteger ReplacementGasWei(BigInteger desiredWei, BigInteger previousGasWei)
    {
        if (previousGasWei <= 0)
        {
            return desiredWei;
        }

        var minReplace = previousGasWei * (100 + ReplacePriceBumpPercent) / 100 + 1;
        return desiredWei >= minReplace ? desiredWei : minReplace;
    }

    /// <summary>
    /// 打油预留使用的 gasPrice（wei）：按 <see cref="MaxBumpSteps"/> 封顶档计算，确保后续归集重签抬档仍可负担。
    /// </summary>
    public static BigInteger FuelBudgetGasPriceWei(BigInteger networkWei) => ApplyBump(networkWei, MaxBumpSteps);

    /// <summary>
    /// 计算归集任务下一笔待广播归集交易所需的目标 gasPrice（wei）：
    /// - 首笔归集（!isReplacement）：ApplyBump(networkGasWei, committedBumpSteps)
    /// - 同 nonce 替换（isReplacement）：ReplacementGasWei(ApplyBump(networkGasWei, committedBumpSteps + 1), previousSweepGasWei)
    /// DriveFuelingAsync 准入检查、打油差额预算与 DriveSweepingAsync 实际签名三处严格共用本函数，保证 P_enter == P_stay。
    /// </summary>
    public static BigInteger RequiredSweepGasWei(
        BigInteger networkGasWei, int committedBumpSteps, BigInteger previousSweepGasWei, bool isReplacement)
    {
        var targetStep = isReplacement ? committedBumpSteps + 1 : committedBumpSteps;
        var desired = ApplyBump(networkGasWei, targetStep);
        return isReplacement ? ReplacementGasWei(desired, previousSweepGasWei) : desired;
    }

    /// <summary>
    /// 计算给充值地址补足归集 gas 所需的打油差额（wei）：
    /// 目标余额 = max(FuelBudgetGasPriceWei(networkGasWei), requiredSweepGasWei) * Erc20TransferGasLimit * 3 / 2，
    /// 实际打油额 = max(目标余额 - currentAddressBnbWei, minimumFuelWei)。
    /// </summary>
    public static BigInteger ComputeDeltaFuelWei(
        BigInteger networkGasWei, BigInteger requiredSweepGasWei, BigInteger currentAddressBnbWei, BigInteger minimumFuelWei)
    {
        var baseBudgetGasWei = FuelBudgetGasPriceWei(networkGasWei);
        var effectiveBudgetGasWei = requiredSweepGasWei > baseBudgetGasWei ? requiredSweepGasWei : baseBudgetGasWei;
        var targetBalanceWei = effectiveBudgetGasWei * Erc20TransferGasLimit * 3 / 2;
        var deficitWei = targetBalanceWei > currentAddressBnbWei ? targetBalanceWei - currentAddressBnbWei : BigInteger.Zero;
        return deficitWei > minimumFuelWei ? deficitWei : minimumFuelWei;
    }

    /// <summary>
    /// 计算地址在给定 <paramref name="gasLimit"/> 下可负担的最大 gasPrice，并与期望值取小。
    /// 调用方发现返回 0 或低于期望值时不得静默压价广播（否则会重算同哈希或卡池）。
    /// </summary>
    public static BigInteger CapToAffordable(BigInteger desiredGasPriceWei, BigInteger balanceWei, int gasLimit)
    {
        if (gasLimit <= 0 || balanceWei <= 0)
        {
            return BigInteger.Zero;
        }

        var maxAffordable = balanceWei / gasLimit;
        if (maxAffordable <= 0)
        {
            return BigInteger.Zero;
        }

        return desiredGasPriceWei > maxAffordable ? maxAffordable : desiredGasPriceWei;
    }

    public static BigInteger EtherToWei(decimal ether)
    {
        if (ether <= 0m)
        {
            return BigInteger.Zero;
        }

        var scaled = decimal.Truncate(ether * 1_000_000_000_000_000_000m);
        return new BigInteger(scaled);
    }

    public static decimal WeiToEther(BigInteger wei)
    {
        if (wei <= 0)
        {
            return 0m;
        }

        var whole = BigInteger.DivRem(wei, WeiPerEther, out var rem);
        return (decimal)whole + (decimal)rem / 1_000_000_000_000_000_000m;
    }

    public static BigInteger GweiToWei(decimal gwei)
    {
        if (gwei <= 0m)
        {
            return BigInteger.Zero;
        }

        var scaled = decimal.Truncate(gwei * 1_000_000_000m);
        return new BigInteger(scaled);
    }

    public static BigInteger ParseWeiOrZero(string? decimalWei)
    {
        return BigInteger.TryParse(decimalWei, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : BigInteger.Zero;
    }

    /// <summary>
    /// 检测 eth_getCode 结果是否包含合法非空字节码（合约或 EIP-7702 0xef0100 委托）。
    /// 必须为 0x 前缀 + 至少 1 字节的偶数长度合法十六进制串且至少含一个非零位；畸形字符串视为无效响应返回 false。
    /// </summary>
    public static bool HasDeployedCode(string? codeHex)
    {
        if (string.IsNullOrWhiteSpace(codeHex))
        {
            return false;
        }

        var trimmed = codeHex.Trim();
        if (!trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var span = trimmed.AsSpan(2);
        if (span.Length < 2 || (span.Length & 1) != 0)
        {
            return false;
        }

        var hasNonZero = false;
        foreach (var ch in span)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }

            if (ch != '0')
            {
                hasNonZero = true;
            }
        }

        return hasNonZero;
    }
}
