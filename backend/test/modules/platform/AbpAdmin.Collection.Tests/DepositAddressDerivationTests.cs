using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 充值地址派生（计划第 5.6 节验收）：并发派生不撞索引（真 PG，唯一 (NetworkId, AddressIndex)
/// 兜底 + 锁内重读）；扫链未成功跑过一轮时拒绝新派生、已有地址照常返回并标明未开放。
/// </summary>
public class DepositAddressDerivationTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid User2 = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private async Task<Guid> SeedNetworkAsync(bool scanRan)
    {
        var encryptor = GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>();
        var cipher = HdKeyService.Protect(
            "test test test test test test test test test test test test", "test-master-key", encryptor);

        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-" + Guid.NewGuid().ToString("N")[..8], // 共享库跨用例：名字唯一防撞
            ChainId = 97,
            MnemonicCipher = cipher,
            CreationTime = DateTime.Now,
        };
        await dbContext.Networks.AddAsync(network);
        await dbContext.Tokens.AddAsync(new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id, Symbol = "USDT", ContractAddress = "0xusdt",
            Decimals = 18, MinDeposit = "10", IsEnabled = true,
            LastScannedBlock = scanRan ? 100 : null,
            CreationTime = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return network.Id;
    }

    private DepositAddressManager NewManager()
    {
        return GetRequiredService<DepositAddressManager>().WithMasterKey("test-master-key");
    }

    [Fact]
    public async Task Concurrent_Derivation_Assigns_Distinct_Indexes()
    {
        var networkId = await SeedNetworkAsync(scanRan: true);

        // 两个独立 scope 并发派生（不同用户）：部分唯一索引 (NetworkId, AddressIndex) 兜底 + 锁内重读
        var addresses = await Task.WhenAll(
            DeriveInNewScopeAsync(networkId, User1),
            DeriveInNewScopeAsync(networkId, User2));

        addresses[0].ShouldNotBe(addresses[1]); // 不把同一地址发给两个用户

        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var rows = await dbContext.DepositAddresses
            .Where(a => a.NetworkId == networkId)
            .OrderBy(a => a.AddressIndex)
            .ToListAsync();
        await uow.CompleteAsync();

        rows.Count.ShouldBe(2);
        rows[0].AddressIndex.ShouldNotBe(rows[1].AddressIndex);
        rows[0].AddressIndex.ShouldBeGreaterThanOrEqualTo(HdKeyService.FirstUserAddressIndex);
    }

    [Fact]
    public async Task Derivation_Refused_Until_Scan_Has_Run()
    {
        var networkId = await SeedNetworkAsync(scanRan: false);
        var manager = NewManager();

        // 扫链没跑过：拒绝新派生（转入会落在首次游标之前）
        await Should.ThrowAsync<BusinessException>(() => manager.GetOrDeriveAsync(networkId, User1));

        // 已有地址的用户照常返回，标明充值未开放
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.DepositAddresses.AddAsync(new ColDepositAddress(Guid.NewGuid())
            {
                UserId = User2, NetworkId = networkId,
                Address = "0x" + (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..40],
                AddressIndex = 2, CreationTime = DateTime.Now,
            });
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        var (address, enabled, notice) = await manager.GetOrDeriveAsync(networkId, User2);
        address.ShouldNotBeNullOrEmpty();
        enabled.ShouldBeFalse();
        notice.ShouldNotBeNull();
    }

    private async Task<string> DeriveInNewScopeAsync(Guid networkId, Guid userId)
    {
        using var scope = ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<DepositAddressManager>()
            .WithMasterKey("test-master-key");
        return (await manager.GetOrDeriveAsync(networkId, userId)).Address;
    }
}
