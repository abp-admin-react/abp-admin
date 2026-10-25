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
            W(CollectionSettings.Withdraw.Halt, "false"),
            W(CollectionSettings.Withdraw.BroadcastTimeoutMinutes, DefBroadcastTimeoutMinutes),
            W(CollectionSettings.Withdraw.MaxRetries, DefMaxRetries),
            W(CollectionSettings.Withdraw.HotWalletUsdtFloor, DefHotWalletUsdtFloor),
            W(CollectionSettings.Withdraw.MinHotWalletBnb, DefMinHotWalletBnb));
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

    private static string Inv(decimal value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>提现参数定义样板：显示名/描述键 = DisplayName:{键} / Description:{键}。</summary>
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

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<CollectionResource>(name);
    }
}
