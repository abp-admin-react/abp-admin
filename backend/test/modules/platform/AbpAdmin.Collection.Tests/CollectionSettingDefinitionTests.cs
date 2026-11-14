using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.Collection.Settings;
using EasyAbp.Abp.SettingUi;
using Shouldly;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionSettingDefinitionTests
{
    [Theory]
    [InlineData(CollectionSettings.MaxAmount.WechatNativeCny, CollectionSettings.SettingUiGroups.WechatPay, "")]
    [InlineData(CollectionSettings.MaxAmount.AlipayPrecreateCny, CollectionSettings.SettingUiGroups.Alipay, "")]
    [InlineData(CollectionSettings.MaxAmount.StripeCheckoutUsd, CollectionSettings.SettingUiGroups.Stripe, "")]
    [InlineData(CollectionSettings.Balance.DebitCommandTimeoutSeconds, CollectionSettings.SettingUiGroup, "15")]
    public void Payment_Settings_Belong_To_Their_Own_Channel(string name, string group, string defaultValue)
    {
        var setting = GetDefinitions()[name];

        setting.Properties[SettingUiConst.Group1].ShouldBe(group);
        setting.Properties.ContainsKey(SettingUiConst.Group2).ShouldBeFalse();
        setting.DefaultValue.ShouldBe(defaultValue);
        setting.Providers.ShouldBe(new[] { GlobalSettingValueProvider.ProviderName, DefaultValueSettingValueProvider.ProviderName });
    }

    [Fact]
    public void Chain_And_Withdrawal_Settings_Are_Grouped_Under_Web3()
    {
        var definitions = GetDefinitions().Values;
        var settings = definitions.Where(setting =>
            setting.Properties.ContainsKey(SettingUiConst.Group1) &&
            (setting.Name.StartsWith("Collection.Chain.", StringComparison.Ordinal) ||
             setting.Name.StartsWith("Collection.Withdraw.", StringComparison.Ordinal))).ToList();

        settings.ShouldNotBeEmpty();
        foreach (var setting in settings)
        {
            setting.Properties[SettingUiConst.Group1].ShouldBe(CollectionSettings.SettingUiGroups.Web3);
            setting.Properties[SettingUiConst.Group2].ShouldBe(
                setting.Name.StartsWith("Collection.Chain.", StringComparison.Ordinal) ? "Chain" : "Withdraw");
        }

        definitions.Where(setting => setting.Properties.TryGetValue(SettingUiConst.Group1, out var group) &&
                Equals(group, CollectionSettings.SettingUiGroup))
            .Select(setting => setting.Name)
            .ShouldBe(new[] { CollectionSettings.Balance.DebitCommandTimeoutSeconds });
    }

    [Theory]
    [InlineData(CollectionSettings.Withdraw.Enabled)]
    [InlineData(CollectionSettings.Withdraw.Halt)]
    [InlineData(CollectionSettings.Chain.ScanEnabled)]
    [InlineData(CollectionSettings.Chain.SweepEnabled)]
    [InlineData(CollectionSettings.Chain.HotWalletWatchEnabled)]
    public void Funds_Switches_Keep_Their_Domain_Management_Endpoints(string name)
    {
        var setting = GetDefinitions()[name];

        setting.Properties.ContainsKey(SettingUiConst.Group1).ShouldBeFalse();
        setting.DefaultValue.ShouldBe("false");
    }

    private static Dictionary<string, SettingDefinition> GetDefinitions()
    {
        var definitions = new Dictionary<string, SettingDefinition>();
        new CollectionSettingDefinitionProvider().Define(new SettingDefinitionContext(definitions));
        return definitions;
    }
}
