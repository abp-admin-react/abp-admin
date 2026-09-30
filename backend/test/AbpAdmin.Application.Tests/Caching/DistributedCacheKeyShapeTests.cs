using System;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Caching;

/* ABP 缓存键形状的机器钉住（此前只是 CacheMonitorAppService 注释里的"rel-10.6 反编译核实"
 * 散文断言）：监控的键空间守卫（IsAllowedKey 的 ",k:{KeyPrefix}" 判定）与扫描锚定
 * （BuildScanPattern 的隔离前缀必含段）完全依赖这些形状——ABP 升级若改变规范化输出
 * （比如把 KeyPrefix 挪到键首），监控的判定将静默失配（漏键或越界），本测试先红。
 *
 * 直连构造规范化器而不启测试模块（本工程的 AbpAdminApplicationTestModule 需要 EFCore 层
 * 仓储才能启动——DI 型测试都在 EntityFrameworkCore.Tests 落地；规范化器只依赖
 * ICurrentTenant + IOptions，用本地替身即可），保持本工程「具体类皆纯单测」的惯例。
 */
public class DistributedCacheKeyShapeTests
{
    /// <summary>
    /// 最小租户上下文替身：Change 的嵌套还原语义与真实 CurrentTenant 一致（本测试只用
    /// Id 的当前值与单层 Change）。
    /// </summary>
    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid? Id { get; private set; }
        public string? Name { get; private set; }
        public bool IsAvailable => Id.HasValue;

        public IDisposable Change(Guid? id, string? name = null)
        {
            var previous = (Id, Name);
            Id = id;
            Name = name;
            return new Revert(this, previous);
        }

        private sealed class Revert(StubCurrentTenant owner, (Guid? Id, string? Name) previous) : IDisposable
        {
            public void Dispose()
            {
                owner.Id = previous.Id;
                owner.Name = previous.Name;
            }
        }
    }

    private readonly StubCurrentTenant _currentTenant = new();
    private readonly IDistributedCacheKeyNormalizer _normalizer;

    public DistributedCacheKeyShapeTests()
    {
        _normalizer = new DistributedCacheKeyNormalizer(
            _currentTenant,
            Options.Create(new AbpDistributedCacheOptions()));
    }

    /// <summary>带隔离前缀的规范化器。</summary>
    private IDistributedCacheKeyNormalizer NormalizerWithPrefix(string prefix) =>
        new DistributedCacheKeyNormalizer(
            _currentTenant,
            Options.Create(new AbpDistributedCacheOptions { KeyPrefix = prefix }));

    private string Normalize(IDistributedCacheKeyNormalizer normalizer) =>
        normalizer.NormalizeKey(new DistributedCacheKeyNormalizeArgs("k1", "MyCache", ignoreMultiTenancy: false));

    [Fact]
    public void Host_Keys_Use_The_Structural_c_Prefix()
    {
        Normalize(_normalizer).ShouldBe("c:MyCache,k:k1");
    }

    [Fact]
    public void Tenant_Keys_Nest_The_Host_Shape_Under_t_Prefix()
    {
        var tenantId = Guid.Parse("3a23d8e4-1122-3344-5566-778899aabbcc");
        using (_currentTenant.Change(tenantId))
        {
            Normalize(_normalizer).ShouldBe($"t:{tenantId},c:MyCache,k:k1");
        }
    }

    [Fact]
    public void Isolation_Prefix_Sits_Inside_The_k_Segment_Not_At_The_Key_Head()
    {
        // ",k:{KeyPrefix}" 是 IsAllowedKey 的判定锚：前缀若被挪到键首（如 "{Prefix}c:..."），
        // 监控对配置了隔离前缀的部署会漏掉自己的全部键
        Normalize(NormalizerWithPrefix("AbpAdmin:")).ShouldBe("c:MyCache,k:AbpAdmin:k1");
    }

    [Fact]
    public void Tenant_Prefixed_Keys_Keep_Both_Invariants()
    {
        var tenantId = Guid.Parse("3a23d8e4-1122-3344-5566-778899aabbcc");
        using (_currentTenant.Change(tenantId))
        {
            Normalize(NormalizerWithPrefix("AbpAdmin:")).ShouldBe($"t:{tenantId},c:MyCache,k:AbpAdmin:k1");
        }
    }
}
