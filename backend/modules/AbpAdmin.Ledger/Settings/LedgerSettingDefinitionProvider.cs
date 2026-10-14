using AbpAdmin.Ledger.Localization;
using AbpAdmin.Settings;
using EasyAbp.Abp.SettingUi;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Ledger.Settings;

public class LedgerSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                    LedgerSettings.Enabled,
                    "true",
                    L("Setting:Finance.Ledger.Enabled"),
                    L("Setting:Finance.Ledger.Enabled.Description"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(LedgerSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Checkbox),
            new SettingDefinition(
                    LedgerSettings.PrepaymentFreezeWrites,
                    "false",
                    L("Setting:Finance.Prepayment.FreezeWrites"),
                    L("Setting:Finance.Prepayment.FreezeWrites.Description"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(LedgerSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Checkbox),
            new SettingDefinition(
                    LedgerSettings.WalletUseLedgerProjection,
                    "false",
                    L("Setting:Finance.Wallet.UseLedgerProjection"),
                    L("Setting:Finance.Wallet.UseLedgerProjection.Description"))
                .WithProviders(
                    GlobalSettingValueProvider.ProviderName,
                    DefaultValueSettingValueProvider.ProviderName)
                .WithUi(LedgerSettings.SettingUiGroup, group2: null, SettingUiConst.Components.Checkbox)
        );
    }

    private static LocalizableString L(string name) =>
        LocalizableString.Create<LedgerResource>(name);
}
