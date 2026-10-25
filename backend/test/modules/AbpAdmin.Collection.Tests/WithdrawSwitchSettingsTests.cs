using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using Shouldly;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
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

    [Fact]
    public async Task Withdraw_Admin_Endpoint_Flips_Switch_Hotly()
    {
        // 资金面开关的写入口在 WithdrawAdmin 端点（非 SettingUi 通用权限）——锁：写入即热生效
        var admin = GetRequiredService<CollectionWithdrawAdminAppService>();
        var manager = GetRequiredService<WithdrawManager>();
        try
        {
            await admin.SetWithdrawEnabledAsync(false);
            (await manager.IsEnabledAsync()).ShouldBeFalse();
            await admin.SetWithdrawEnabledAsync(true);
            (await manager.IsEnabledAsync()).ShouldBeTrue();
        }
        finally
        {
            await GetRequiredService<ISettingManager>()
                .SetGlobalAsync(CollectionSettings.Withdraw.Enabled, "true");
        }
    }

    [Fact]
    public async Task Chain_Admin_Endpoint_Flips_Switches_And_Status_Reflects()
    {
        var admin = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        var networkAdmin = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        var (network, _) = await SeedNetworkOnlyAsync();
        try
        {
            // 先播种 true：断言「未提供的参数不动」才区分得了 null 跳过 vs 覆盖成默认 false
            await admin.SetChainSwitchesAsync(sweepEnabled: true);
            await admin.SetChainSwitchesAsync(scanEnabled: true, hotWalletWatchEnabled: true);
            var status = await networkAdmin.GetStatusAsync(network.Id);
            status.ScanEnabled.ShouldBeTrue();
            status.SweepEnabled.ShouldBeTrue(); // 未提供的参数不动（原 true 不被 null 翻回）
            status.HotWalletWatchEnabled.ShouldBeTrue();
        }
        finally
        {
            await admin.SetChainSwitchesAsync(
                scanEnabled: false, sweepEnabled: false, hotWalletWatchEnabled: false);
        }
    }

    [Fact]
    public async Task Halt_Endpoint_Flips_Hotly_And_Gates_Submission()
    {
        // Halt 与 Enabled 同族治理（域权限端点而非通用设置页）——锁：写后热生效 + 提交被拒
        var admin = GetRequiredService<CollectionWithdrawAdminAppService>();
        var manager = GetRequiredService<WithdrawManager>();
        var settings = GetRequiredService<ISettingManager>();
        try
        {
            await admin.SetWithdrawHaltAsync(true);
            (await GetRequiredService<ISettingProvider>().IsTrueAsync(CollectionSettings.Withdraw.Halt))
                .ShouldBeTrue();
            await Should.ThrowAsync<Volo.Abp.BusinessException>(async () =>
            {
                using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
                await manager.SubmitAsync(Guid.NewGuid(), CollectionCurrency.Usdt,
                    "0x4444444444444444444444444444444444444444", 100m);
                await uow.CompleteAsync();
            });
        }
        finally
        {
            await settings.SetGlobalAsync(CollectionSettings.Withdraw.Halt, "false");
        }
    }

    [Fact]
    public async Task HotWallet_Status_Degrades_To_Unconfigured_When_Gateway_Cannot_Assemble()
    {
        // 全新环境（无 RPC/key 池）：状态卡是只读展示面，必须 Configured=false 而不是 500
        //（用户实测踩中：未配置环境打开提现页弹服务器错误）。资金路径不经此降级。
        await SeedNetworkOnlyAsync(); // 种网络行但不配 RPC/key
        var admin = GetRequiredService<CollectionWithdrawAdminAppService>();
        var status = await admin.GetHotWalletAsync();
        status.Configured.ShouldBeFalse();
    }

    private async Task<(AbpAdmin.Collection.Entities.ColNetwork Network, Guid UsdtId)> SeedNetworkOnlyAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<
            Volo.Abp.EntityFrameworkCore.IDbContextProvider<AbpAdmin.Collection.Data.CollectionDbContext>>()
            .GetDbContextAsync();
        var network = new AbpAdmin.Collection.Entities.ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-SW-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Networks.AddAsync(network);
        var usdt = new AbpAdmin.Collection.Entities.ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id,
            Symbol = "USDT",
            ContractAddress = "0xusdt",
            Decimals = 18,
            MinDeposit = "10",
            IsEnabled = true,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Tokens.AddAsync(usdt);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, usdt.Id);
    }
}
