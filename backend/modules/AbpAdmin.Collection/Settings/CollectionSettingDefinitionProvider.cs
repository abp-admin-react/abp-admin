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

            // 提现 M2/M3 运行期参数（点键，SettingUi 可改；代码默认值见 WithdrawDefaults）
            W(CollectionSettings.Withdraw.MinAmount, "10"),
            W(CollectionSettings.Withdraw.FeeRate, "0.004"),
            W(CollectionSettings.Withdraw.MaxDailyAmount, "50000"),
            W(CollectionSettings.Withdraw.MaxDailyCount, "3"),
            W(CollectionSettings.Withdraw.AutoApproveMaxAmount, "100"),
            W(CollectionSettings.Withdraw.Halt, "false"),
            W(CollectionSettings.Withdraw.BroadcastTimeoutMinutes, "30"),
            W(CollectionSettings.Withdraw.MaxRetries, "3"),
            W(CollectionSettings.Withdraw.HotWalletUsdtFloor, "1000"),
            W(CollectionSettings.Withdraw.MinHotWalletBnb, "0.005"));
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
