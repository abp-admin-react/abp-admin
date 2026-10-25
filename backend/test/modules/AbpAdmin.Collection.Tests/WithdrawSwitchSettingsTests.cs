using System.Threading.Tasks;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using Shouldly;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 提现总开关（设置 Collection.Withdraw.Enabled）fail-closed + 热改锁：
/// 垃圾值解析为关；SetGlobal 后下一次读取立即生效（与 Halt 同款热改路径——worker 每轮现读）。
/// 取代旧 WithdrawEnabledPredicateTests（开关迁设置页后无独立解析纯函数，锁点移到真实读链）。
/// </summary>
public class WithdrawSwitchSettingsTests : CollectionTestBase
{
    [Fact]
    public async Task Switch_Is_On_For_Test_Db_By_Module_Seed()
    {
        var manager = GetRequiredService<WithdrawManager>();
        (await manager.IsEnabledAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Garbage_Value_Fails_Closed()
    {
        var settings = GetRequiredService<ISettingManager>();
        var manager = GetRequiredService<WithdrawManager>();
        try
        {
            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "garbage");
            (await manager.IsEnabledAsync()).ShouldBeFalse();

            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "FALSE");
            (await manager.IsEnabledAsync()).ShouldBeFalse();
        }
        finally
        {
            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "true");
        }
    }

    [Fact]
    public async Task Hot_Change_Takes_Effect_On_Next_Read()
    {
        var settings = GetRequiredService<ISettingManager>();
        var manager = GetRequiredService<WithdrawManager>();
        try
        {
            (await manager.IsEnabledAsync()).ShouldBeTrue();
            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "false");
            (await manager.IsEnabledAsync()).ShouldBeFalse();
        }
        finally
        {
            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "true");
        }
    }
}
