using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 归集手动重试端点锁：仅 Failed 可拉回 Queued（跳过冷却；仅人工失败也允许——管理员显式决策）；
/// 非 Failed 抛 SweepTaskNotRetryable、不存在抛 SweepTaskNotFound；
/// GetStatus 回显充值政策两字段（前端政策表单预填口径，AutoCreditMaxAmount 走十进制字符串口径）。
/// </summary>
public class SweepRetryAdminTests : CollectionTestBase
{
    [Fact]
    public async Task Retry_Pulls_Failed_Task_Back_To_Queued_Clears_FailReason()
    {
        var (network, usdt) = await SeedMinimalChainAsync();
        // 仅人工失败样例（打油超上限）：自动拉回永远不碰它，手动重试必须允许
        var taskId = await InsertSweepTaskAsync(network.Id, usdt.Id, SweepTaskStatus.Failed,
            failReason: "打油金额 0.02 BNB 超过上限 0.005（gas 价格异常？）");

        var admin = GetRequiredService<CollectionNetworkAdminAppService>();
        await admin.RetrySweepTaskAsync(taskId);

        Entities.ColSweepTask? task = null;
        await WithUnitOfWorkAsync(async () =>
        {
            task = await (await DbAsync()).SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        });
        task!.Status.ShouldBe(SweepTaskStatus.Queued);
        task.Retryable.ShouldBeFalse(); // 与自动拉回同款：再失败由 FailAsync 重新判定
        task.FailReason.ShouldBeNull();

        // 已 Queued 再点重试：状态门拒绝（0 行 CAS 的前置检查口径）
        await Should.ThrowAsync<BusinessException>(() => admin.RetrySweepTaskAsync(taskId));
    }

    [Fact]
    public async Task Retry_Rejects_NonFailed_And_Missing()
    {
        var (network, usdt) = await SeedMinimalChainAsync();
        var sweepingId = await InsertSweepTaskAsync(network.Id, usdt.Id, SweepTaskStatus.Sweeping);

        var admin = GetRequiredService<CollectionNetworkAdminAppService>();
        (await Should.ThrowAsync<BusinessException>(() => admin.RetrySweepTaskAsync(sweepingId)))
            .Code.ShouldContain("SweepTaskNotRetryable");
        (await Should.ThrowAsync<BusinessException>(() => admin.RetrySweepTaskAsync(Guid.NewGuid())))
            .Code.ShouldContain("SweepTaskNotFound");
    }

    [Fact]
    public async Task GetStatus_Returns_Deposit_Policy_Fields()
    {
        await SeedMinimalChainAsync(); // 端点写「最早网络行」：种子 BSC 行先于本测试网络，读默认网络
        var admin = GetRequiredService<CollectionNetworkAdminAppService>();
        await admin.SetDepositPolicyAsync(0.001m, 12345m);

        var status = await admin.GetStatusAsync();
        status.DepositFeeRate.ShouldBe(0.001m);
        status.AutoCreditMaxAmount.ShouldBe("12345"); // CollectionAmounts.Format 口径
    }

    private async Task<(ColNetwork Network, ColToken Usdt)> SeedMinimalChainAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-RETRY-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            DepositFeeRate = 0m,
            AutoCreditMaxAmount = 5000m,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Networks.AddAsync(network);
        var usdt = new ColToken(Guid.NewGuid())
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
        return (network, usdt);
    }

    private async Task<Guid> InsertSweepTaskAsync(
        Guid networkId, Guid tokenId, SweepTaskStatus status, string? failReason = null)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        var task = new ColSweepTask(Guid.NewGuid())
        {
            NetworkId = networkId,
            TokenId = tokenId,
            UserId = Guid.NewGuid(),
            AddressId = Guid.NewGuid(), // 随机地址：绕开同址同币活跃任务唯一索引
            Address = "0x" + Guid.NewGuid().ToString("N").PadRight(40, '0'),
            AddressIndex = 2,
            Amount = 25m,
            Status = status,
            Retryable = false,
            FailReason = failReason,
            StatusChangedAt = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.SweepTasks.AddAsync(task);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return task.Id;
    }

    private async Task<CollectionDbContext> DbAsync()
    {
        return await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
    }
}
