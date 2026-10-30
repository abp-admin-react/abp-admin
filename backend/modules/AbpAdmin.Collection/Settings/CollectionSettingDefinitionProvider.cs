using AbpAdmin.Collection.Localization;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Collection.Settings;

/// <summary>
/// 收款模块运行期参数定义（同 Biz.Template 的第③层模式：定义随模块自持，
/// 存储复用框架 SettingManagement；WithProviders("G","D") 的理由见该样板的注释）。
/// </summary>
public class CollectionSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                CollectionSettings.MaxAmount.WechatNativeCny,
                defaultValue: "",
                L("DisplayName:Collection.MaxAmount.WechatNativeCny"),
                L("Description:Collection.MaxAmount.WechatNativeCny"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(CollectionSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Text),
            new SettingDefinition(
                CollectionSettings.MaxAmount.AlipayPrecreateCny,
                defaultValue: "",
                L("DisplayName:Collection.MaxAmount.AlipayPrecreateCny"),
                L("Description:Collection.MaxAmount.AlipayPrecreateCny"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(CollectionSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Text),
            new SettingDefinition(
                CollectionSettings.MaxAmount.StripeCheckoutUsd,
                defaultValue: "",
                L("DisplayName:Collection.MaxAmount.StripeCheckoutUsd"),
                L("Description:Collection.MaxAmount.StripeCheckoutUsd"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(CollectionSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Text),
            new SettingDefinition(
                CollectionSettings.Balance.DebitCommandTimeoutSeconds,
                defaultValue: "15",
                L("DisplayName:Collection.Balance.DebitCommandTimeoutSeconds"),
                L("Description:Collection.Balance.DebitCommandTimeoutSeconds"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(CollectionSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Text),

            // 提现 M2/M3 运行期参数：默认值单源 = WithdrawDefaults（不变文化字符串；Cursor 计划
            // settings-single-source）。Halt 无常量对应，保持 "false" 字面量。
            W(CollectionSettings.Withdraw.MinAmount, DefMinAmount),
            W(CollectionSettings.Withdraw.FeeRate, DefFeeRate),
            W(CollectionSettings.Withdraw.MaxDailyAmount, DefMaxDailyAmount),
            W(CollectionSettings.Withdraw.MaxDailyCount, DefMaxDailyCount),
            W(CollectionSettings.Withdraw.AutoApproveMaxAmount, DefAutoApproveMaxAmount),
            S(CollectionSettings.Withdraw.Halt),
            W(CollectionSettings.Withdraw.BroadcastTimeoutMinutes, DefBroadcastTimeoutMinutes),
            W(CollectionSettings.Withdraw.MaxRetries, DefMaxRetries),
            W(CollectionSettings.Withdraw.HotWalletUsdtFloor, DefHotWalletUsdtFloor),
            W(CollectionSettings.Withdraw.MinHotWalletBnb, DefMinHotWalletBnb),

            // 链上 worker 参数热改（宿主配置迁移）：数值/阈值参数在设置页管理（热改免重启）；
            // 资金面开关族（扫链/归集/热钱包监控/提现总开关/提现总闸 Halt）不进设置页——
            // 高危杠杆改由域权限端点把守（Halt 与 Enabled 同拦一条付币管线，同权限位治理；
            // 布尔闸门渲染成自由文本框还有敲 "True" 静默不拉闸的 fail-open 面）
            //（SetChainSwitchesAsync=Network.Admin / SetWithdrawEnabledAsync=WithdrawAdmin，
            // （通用 AbpAdmin.SettingUi.Update 挡不住烧 Gas/付币开关），worker 照旧每轮热读。
            W(CollectionSettings.Withdraw.BroadcastIntervalSeconds, DefBroadcastIntervalSeconds),
            W(CollectionSettings.Withdraw.BroadcastBatchSize, DefBroadcastBatchSize),
            W(CollectionSettings.Withdraw.AutoApproveBatchSize, DefAutoApproveBatchSize),
            S(CollectionSettings.Withdraw.Enabled),
            S(CollectionSettings.Chain.ScanEnabled),
            S(CollectionSettings.Chain.SweepEnabled),
            S(CollectionSettings.Chain.HotWalletWatchEnabled),
            W(CollectionSettings.Chain.ScanIntervalSeconds, DefScanIntervalSeconds),
            W(CollectionSettings.Chain.SweepIntervalSeconds, DefSweepIntervalSeconds),
            W(CollectionSettings.Chain.HotWalletWatchIntervalSeconds, DefHotWalletWatchIntervalSeconds),
            W(CollectionSettings.Chain.Confirmations, DefConfirmations),
            W(CollectionSettings.Chain.BlockRange, DefBlockRange),
            W(CollectionSettings.Chain.ScanBehindBlocks, DefScanBehindBlocks),
            W(CollectionSettings.Chain.SweepRoundLimit, ""),
            W(CollectionSettings.Chain.FuelBnbLimit, DefFuelBnbLimit),
            W(CollectionSettings.Chain.GasBnbAlertThreshold, DefGasBnbAlertThreshold),
            W(CollectionSettings.Chain.RetryCooldownMinutes, DefRetryCooldownMinutes),
            W(CollectionSettings.Chain.RebroadcastGraceMinutes, DefRebroadcastGraceMinutes),
            W(CollectionSettings.Chain.SweepStuckMinutes, DefSweepStuckMinutes),
            W(CollectionSettings.Chain.SkipRatioWindowMinutes, DefSkipRatioWindowMinutes),
            W(CollectionSettings.Chain.SkipRatioThreshold, DefSkipRatioThreshold));
    }

    // 常量 → 设置默认值字符串（不变文化）。静态字段而非方法调用——csc 10.0.401 对本文件
    // 参数位置内嵌 Inv(...) 方法调用持续误报 CS1026（无隐藏字符、括号平衡，字面量则通过），
    // 用字段初始化绕过（语义等价，均为编译期常量的运行时格式化）。
    private static readonly string DefMinAmount = Inv(WithdrawDefaults.MinAmount);
    private static readonly string DefFeeRate = Inv(WithdrawDefaults.FeeRate);
    private static readonly string DefMaxDailyAmount = Inv(WithdrawDefaults.MaxDailyAmount);
    private static readonly string DefMaxDailyCount = Inv(WithdrawDefaults.MaxDailyCount);
    private static readonly string DefAutoApproveMaxAmount = Inv(WithdrawDefaults.AutoApproveMaxAmount);
    private static readonly string DefBroadcastTimeoutMinutes = Inv(WithdrawDefaults.BroadcastTimeoutMinutes);
    private static readonly string DefMaxRetries = Inv(WithdrawDefaults.MaxRetries);
    private static readonly string DefHotWalletUsdtFloor = Inv(WithdrawDefaults.HotWalletUsdtFloor);
    private static readonly string DefMinHotWalletBnb = Inv(WithdrawDefaults.MinHotWalletBnb);
    private static readonly string DefBroadcastIntervalSeconds = Inv(WithdrawDefaults.BroadcastIntervalSeconds);
    private static readonly string DefBroadcastBatchSize = Inv(WithdrawDefaults.BroadcastBatchSize);
    private static readonly string DefAutoApproveBatchSize = Inv(WithdrawDefaults.AutoApproveBatchSize);
    private static readonly string DefScanIntervalSeconds = Inv(ChainDefaults.ScanIntervalSeconds);
    private static readonly string DefSweepIntervalSeconds = Inv(ChainDefaults.SweepIntervalSeconds);
    private static readonly string DefHotWalletWatchIntervalSeconds = Inv(ChainDefaults.HotWalletWatchIntervalSeconds);
    private static readonly string DefConfirmations = Inv(ChainDefaults.Confirmations);
    private static readonly string DefBlockRange = Inv(ChainDefaults.BlockRange);
    private static readonly string DefScanBehindBlocks = Inv(ChainDefaults.ScanBehindBlocks);
    private static readonly string DefFuelBnbLimit = Inv(ChainDefaults.FuelBnbLimit);
    private static readonly string DefGasBnbAlertThreshold = Inv(ChainDefaults.GasBnbAlertThreshold);
    private static readonly string DefRetryCooldownMinutes = Inv(ChainDefaults.RetryCooldownMinutes);
    private static readonly string DefRebroadcastGraceMinutes = Inv(ChainDefaults.RebroadcastGraceMinutes);
    private static readonly string DefSweepStuckMinutes = Inv(ChainDefaults.SweepStuckMinutes);
    private static readonly string DefSkipRatioWindowMinutes = Inv(ChainDefaults.SkipRatioWindowMinutes);
    private static readonly string DefSkipRatioThreshold = Inv(ChainDefaults.SkipRatioThreshold);

    private static string Inv(decimal value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Inv(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>数值参数定义样板：显示名/描述键 = DisplayName:{键} / Description:{键}。</summary>
    private static SettingDefinition W(string key, string defaultValue)
    {
        return new SettingDefinition(
                key,
                defaultValue,
                L("DisplayName:" + key),
                L("Description:" + key))
            .WithProviders(
                GlobalSettingValueProvider.ProviderName,
                DefaultValueSettingValueProvider.ProviderName)
            .WithUi(CollectionSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Text);
    }

    /// <summary>
    /// 布尔资金闸门定义（无 UI——不进设置页）：默认 "false"（fail-closed，垃圾值解析为关），
    /// 读侧 worker/提交闸每轮热读；写侧走域权限管理端点（SetChainSwitchesAsync=Network.Admin；
    /// SetWithdrawEnabledAsync/SetWithdrawHaltAsync=WithdrawAdmin——资金面高危杠杆
    /// 不交给通用 AbpAdmin.SettingUi.Update 权限）。
    /// </summary>
    private static SettingDefinition S(string key)
    {
        return new SettingDefinition(
                key,
                "false",
                L("DisplayName:" + key),
                L("Description:" + key))
            .WithProviders(
                GlobalSettingValueProvider.ProviderName,
                DefaultValueSettingValueProvider.ProviderName);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CollectionResource>(name);
    }
}
