using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 充值地址派生（计划第 5.6 节）：扫链尚未成功跑过一轮（任一代币游标为空）时拒绝新派生——
/// 否则转入会落在首次游标之前永远扫不到；已有地址的用户照常返回（标明充值未开放）。
/// 派生在 Redis 锁内：等待 5 秒，拿不到抛「请重试」；锁内再查一次；插入撞索引（并发派生）
/// 则重读——不把同一索引发给两个用户。私钥不入库。
/// </summary>
public class DepositAddressManager : DomainService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IStringEncryptionService _encryptor;
    private readonly Volo.Abp.DistributedLocking.IAbpDistributedLock _distributedLock;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public DepositAddressManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IStringEncryptionService encryptor,
        Volo.Abp.DistributedLocking.IAbpDistributedLock distributedLock,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _dbContextProvider = dbContextProvider;
        _encryptor = encryptor;
        _distributedLock = distributedLock;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
    }

    private string? MasterKey => _masterKey;
    private string? _masterKey;

    public DepositAddressManager WithMasterKey(string? masterKey)
    {
        _masterKey = masterKey;
        return this;
    }

    /// <summary>解析一期唯一的 BSC 网络行；种子未跑时抛「充值未开放」。</summary>
    public async Task<Guid> ResolveBscNetworkIdAsync()
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks
            .Where(n => n.Name == CollectionConsts.BscNetworkName)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync();
        await uow.CompleteAsync();
        return network ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
    }

    /// <summary>
    /// 取或派生当前用户的充值地址。返回（地址, 充值是否开放, 未开放时的说明）。
    /// </summary>
    public virtual async Task<(string Address, bool Enabled, string? Notice)> GetOrDeriveAsync(Guid networkId, Guid userId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var network = await dbContext.Networks.FirstOrDefaultAsync(n => n.Id == networkId);
        if (network == null)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        }

        var existing = await dbContext.DepositAddresses
            .FirstOrDefaultAsync(a => a.UserId == userId && a.NetworkId == network.Id);

        var scanEverRan = await dbContext.Tokens.AnyAsync(t => t.NetworkId == network.Id && t.LastScannedBlock != null);

        if (existing != null)
        {
            var enabled = scanEverRan && await HasConfiguredVaultAsync(dbContext, network.Id);
            await uow.CompleteAsync();
            return (existing.Address, enabled,
                enabled ? null : "充值尚未开放：扫链成功跑过一轮之前不提供充值服务。");
        }

        // 没有地址：扫链没跑过 → 拒绝新派生（转入会落在首次游标之前）
        if (!scanEverRan)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        }

        if (string.IsNullOrEmpty(network.MnemonicCipher))
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen)
                .WithData("Reason", "助记词未初始化");
        }

        if (MasterKey == null)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen)
                .WithData("Reason", "主密钥未配置");
        }

        // Redis 锁内派生：等待 5 秒，拿不到抛「请重试」；锁内再查一次；撞索引重读
        // 全局锁而非 per-user：要与「助记词重置」的 check-then-write 互斥（评审 H 级）。
        // 派生每用户仅一次，全局串行可接受
        var lockKey = CollectionConsts.MnemonicLockKey;
        await using var handle = await _distributedLock.TryAcquireAsync(lockKey, timeout: TimeSpan.FromSeconds(5));
        if (handle == null)
        {
            throw new BusinessException(CollectionErrorCodes.TryAgain)
                .WithData("Action", "地址派生进行中，请稍后重试");
        }

        // 锁内再查一次（并发请求可能已派生）
        existing = await dbContext.DepositAddresses
            .FirstOrDefaultAsync(a => a.UserId == userId && a.NetworkId == network.Id);
        if (existing != null)
        {
            await uow.CompleteAsync();
            return (existing.Address, true, null);
        }

        // 并发派生撞索引（唯一 (NetworkId, AddressIndex)）：不同用户的并发派生都算出同一
        // 「最大索引 +1」。每次尝试用独立事务——23505 会中止当前事务，同事务内重试必然
        // 连环失败；撞上后释放事务、退避、重开（赢家提交后 READ COMMITTED 才能看到新最大值）
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(150 * attempt, CancellationToken.None);
            }

            using (var attemptUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                try
                {
                    var attemptDbContext = await _dbContextProvider.GetDbContextAsync();
                    var maxIndex = await attemptDbContext.DepositAddresses
                        .Where(a => a.NetworkId == networkId)
                        .MaxAsync(a => (int?)a.AddressIndex);
                    var nextIndex = Math.Max(HdKeyService.FirstUserAddressIndex, (maxIndex ?? 1) + 1);

                    // 锁内列投影重读密文：外层的 network 是锁前快照——重置竞态下密文可能已
                    // 轮换，用快照派生的首地址在重置后永远不可签名（评审 H 级 TOCTOU）
                    var cipherNow = await dbContext.Networks
                        .Where(n => n.Id == networkId)
                        .Select(n => n.MnemonicCipher)
                        .FirstAsync();
                    using var mnemonic = HdKeyService.Unprotect(cipherNow, MasterKey, _encryptor);
                    var address = HdKeyService.DeriveAddress(mnemonic.Words, nextIndex);

                    var entity = new ColDepositAddress(GuidGenerator.Create())
                    {
                        UserId = userId,
                        NetworkId = networkId,
                        Address = address,
                        AddressIndex = nextIndex,
                        CreationTime = _clock.Now,
                    };
                    await attemptDbContext.DepositAddresses.AddAsync(entity);
                    await attemptDbContext.SaveChangesAsync();

                    await attemptUow.CompleteAsync();
                    Logger.LogInformation("用户 {UserId} 派生充值地址（index={Index}）", userId, nextIndex);
                    return (address, true, null);
                }
                catch (DbUpdateException ex) when (
                    ex.InnerException is PostgresException pg && pg.SqlState == "23505")
                {
                    // 撞索引：事务随 using 释放回滚，退避后换下一个索引重来
                    Logger.LogWarning("派生撞索引（attempt={Attempt}），退避后重试", attempt);
                }
            }
        }

        throw new BusinessException(CollectionErrorCodes.TryAgain)
            .WithData("Reason", "地址派生并发冲突未收敛，请稍后重试");
    }

    private static async Task<bool> HasConfiguredVaultAsync(CollectionDbContext dbContext, Guid networkId)
    {
        return await dbContext.Tokens.AnyAsync(t => t.NetworkId == networkId && t.VaultAddress != null);
    }
}
