using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using EasyAbp.Abp.DataDictionary;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities.Events.Distributed;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.DataDictionaries;

/* T3.4 数据字典种子与枚举同步的集成测试。
 * 覆盖验收标准：
 * - 12 个初始字典（6 手写 + 6 枚举同步：TenantActivationState / FileThumbnailState /
 *   DataScopeType / MenuType / PostStatus / MaskKind），
 *   全部 IsStatic = true，项的 Code 是数值字符串、DisplayText 是 [Description] 中文、Description 是成员名；
 * - 幂等：再跑一遍行数与内容完全不变；
 * - 清理：枚举字典里多余的项被删掉、无对应枚举的静态字典被删掉、孤儿元数据被清掉；
 * - 多租户：租户上下文下同步会各写一份，host 数据不受影响。
 *
 * 测试模块初始化时已跑过一遍 IDataSeeder（host 上下文），这里直接断言即可。
 */
public abstract class DataDictionarySeedTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IDataDictionaryRepository _dictionaryRepository;
    private readonly IRepository<DataDictionaryItemMeta, Guid> _metaRepository;
    private readonly EnumDataDictionarySyncDataSeedContributor _syncContributor;
    private readonly DataDictionaryDataSeedContributor _handWrittenContributor;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedEventBus _distributedEventBus;

    protected DataDictionarySeedTests()
    {
        _dictionaryRepository = GetRequiredService<IDataDictionaryRepository>();
        _metaRepository = GetRequiredService<IRepository<DataDictionaryItemMeta, Guid>>();
        _syncContributor = GetRequiredService<EnumDataDictionarySyncDataSeedContributor>();
        _handWrittenContributor = GetRequiredService<DataDictionaryDataSeedContributor>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _distributedEventBus = GetRequiredService<IDistributedEventBus>();
    }

    [Fact]
    public async Task Should_Seed_Initial_Dictionaries_As_Static()
    {
        var dicts = await WithUnitOfWorkAsync(() => _dictionaryRepository.GetListAsync(includeDetails: true));

        // 6 个手写 + 6 个枚举同步（TenantActivationState / FileThumbnailState / DataScopeType /
        // MenuType / PostStatus / MaskKind）
        dicts.Count.ShouldBe(12);
        dicts.ShouldAllBe(d => d.IsStatic);

        foreach (var code in AbpAdminDictionaryCodes.All)
        {
            dicts.ShouldContain(d => d.Code == code, $"缺少手写字典 {code}");
        }

        // 枚举字典：项数与成员数一致，Code 是数值字符串、DisplayText 是中文、Description 是成员名
        var activation = dicts.Single(d => d.Code == "TenantActivationState");
        activation.Items.Count.ShouldBe(3);
        activation.Items.ShouldAllBe(i => i.IsStatic);
        activation.Items.Single(i => i.Code == "0").DisplayText.ShouldBe("激活");
        activation.Items.Single(i => i.Code == "1").DisplayText.ShouldBe("限时激活");
        activation.Items.Single(i => i.Code == "2").DisplayText.ShouldBe("已停用");
        activation.Items.Single(i => i.Code == "2").Description.ShouldBe("Passive");

        var thumbnail = dicts.Single(d => d.Code == "FileThumbnailState");
        thumbnail.Items.Count.ShouldBe(4);
        thumbnail.Items.Single(i => i.Code == "2").DisplayText.ShouldBe("不支持");

        var gender = dicts.Single(d => d.Code == AbpAdminDictionaryCodes.Gender);
        gender.Items.Count.ShouldBe(3);
        gender.Items.Single(i => i.Code == "Male").DisplayText.ShouldBe("男");
    }

    [Fact]
    public async Task Should_Seed_Item_Metas_With_TagType_And_Order()
    {
        var metas = await WithUnitOfWorkAsync(() => _metaRepository.GetListAsync());

        // 枚举成员的声明顺序写进 Order；[DictionaryTag] 写进 TagType
        var passive = metas.Single(m => m.DictionaryCode == "TenantActivationState" && m.ItemCode == "2");
        passive.TagType.ShouldBe(DataDictionaryTagTypes.Red);
        passive.Order.ShouldBe(2);

        var pending = metas.Single(m => m.DictionaryCode == "TenantActivationState" && m.ItemCode == "0");
        pending.TagType.ShouldBe(DataDictionaryTagTypes.Green);
        pending.Order.ShouldBe(0);

        // 手写字典的元数据
        var jobSuccess = metas.Single(m => m.DictionaryCode == AbpAdminDictionaryCodes.ScheduledJobResult && m.ItemCode == "true");
        jobSuccess.TagType.ShouldBe(DataDictionaryTagTypes.Success);

        // DataScopeType 枚举（字典编码 = 类型名去 Enum 后缀 = DataScopeType）没有标 [DictionaryTag]，TagType 为 null
        var keys = metas.Select(m => $"{m.DictionaryCode}/{m.ItemCode}/{m.TagType}").ToList();
        keys.ShouldContain("DataScopeType/0/");
        var scopeMeta = metas.Single(m => m.DictionaryCode == "DataScopeType" && m.ItemCode == "0");
        scopeMeta.TagType.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Be_Idempotent_When_Seeded_Twice()
    {
        var before = await SnapshotAsync();

        await RunSeedContributorsAsync(null);

        var after = await SnapshotAsync();
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Should_Not_Reset_User_Customizations_On_Reseed()
    {
        // 用户在管理页改过静态项的显示名与 TagType/Order 后重新跑种子：
        // 自定义必须保留（种子只负责首次物化，结构同步除外）。
        // 字典本体显示名也在内：两个贡献者本次删掉的正是字典级 SetContent，回归时这里必须变红
        await WithUnitOfWorkAsync(async () =>
        {
            var gender = await _dictionaryRepository.FindAsync(d => d.Code == AbpAdminDictionaryCodes.Gender);
            gender!.SetContent("性别（自定义）", null);
            gender.Items.Single(i => i.Code == "Male").SetContent("男性自定义", null);
            await _dictionaryRepository.UpdateAsync(gender);

            var meta = await _metaRepository.FindAsync(m =>
                m.DictionaryCode == AbpAdminDictionaryCodes.Gender && m.ItemCode == "Male");
            meta!.Update(DataDictionaryTagTypes.Purple, 42);
            await _metaRepository.UpdateAsync(meta);
        });

        await RunSeedContributorsAsync(null);

        await WithUnitOfWorkAsync(async () =>
        {
            var gender = await _dictionaryRepository.FindAsync(d => d.Code == AbpAdminDictionaryCodes.Gender);
            gender!.DisplayText.ShouldBe("性别（自定义）");
            gender.Items.Single(i => i.Code == "Male").DisplayText.ShouldBe("男性自定义");

            var meta = await _metaRepository.FindAsync(m =>
                m.DictionaryCode == AbpAdminDictionaryCodes.Gender && m.ItemCode == "Male");
            meta!.TagType.ShouldBe(DataDictionaryTagTypes.Purple);
            meta.Order.ShouldBe(42);
        });
    }

    [Fact]
    public async Task Should_Not_Reset_User_Customizations_On_Enum_Dictionaries()
    {
        // 枚举侧与手写侧同口径：字典显示名、项文本、元数据（TagType/Order）都不能被重跑种子重置——
        // ApplyPlansAsync 若回归为 SetContent + 无条件 AddOrUpdateItem + UpsertAsync，这里必须变红
        await WithUnitOfWorkAsync(async () =>
        {
            var activation = await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState");
            activation!.SetContent("租户状态（自定义）", null);
            activation.Items.Single(i => i.Code == "1").SetContent("限时激活（自定义）", null);
            await _dictionaryRepository.UpdateAsync(activation);

            var meta = await _metaRepository.FindAsync(m =>
                m.DictionaryCode == "TenantActivationState" && m.ItemCode == "1");
            meta!.Update(DataDictionaryTagTypes.Purple, 42);
            await _metaRepository.UpdateAsync(meta);
        });

        await RunSeedContributorsAsync(null);

        await WithUnitOfWorkAsync(async () =>
        {
            var activation = await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState");
            activation!.DisplayText.ShouldBe("租户状态（自定义）");
            activation.Items.Single(i => i.Code == "1").DisplayText.ShouldBe("限时激活（自定义）");

            var meta = await _metaRepository.FindAsync(m =>
                m.DictionaryCode == "TenantActivationState" && m.ItemCode == "1");
            meta!.TagType.ShouldBe(DataDictionaryTagTypes.Purple);
            meta.Order.ShouldBe(42);
        });
    }

    [Fact]
    public async Task Should_Backfill_Missing_Meta_Rows_For_HandWritten_Dictionaries_On_Reseed()
    {
        // 手写种子的"补"半边：已存在手写字典缺 meta 行（部分恢复/手工误删）时，重跑种子按定义表回填
        await WithUnitOfWorkAsync(async () =>
        {
            var metas = await _metaRepository.GetListAsync(
                m => m.DictionaryCode == AbpAdminDictionaryCodes.Gender);
            foreach (var meta in metas)
            {
                await _metaRepository.DeleteAsync(meta);
            }
        });

        await RunSeedContributorsAsync(null);

        await WithUnitOfWorkAsync(async () =>
        {
            var gender = await _dictionaryRepository.FindAsync(d => d.Code == AbpAdminDictionaryCodes.Gender);
            var metas = await _metaRepository.GetListAsync(
                m => m.DictionaryCode == AbpAdminDictionaryCodes.Gender);
            metas.Select(m => m.ItemCode).OrderBy(x => x, StringComparer.Ordinal).ToList()
                .ShouldBe(gender!.Items.Select(i => i.Code)
                    .OrderBy(x => x, StringComparer.Ordinal).ToList());
        });
    }

    [Fact]
    public async Task Should_Backfill_Missing_Meta_Rows_For_Existing_Enum_Items_On_Reseed()
    {
        // 已存在枚举项缺 meta 行（部分恢复/手工误删/旧版本种子失败）时，重跑种子应自愈——
        // UpsertIfMissingAsync 对已存在项也执行，与手写字典种子同口径
        await WithUnitOfWorkAsync(async () =>
        {
            var metas = await _metaRepository.GetListAsync(
                m => m.DictionaryCode == "TenantActivationState");
            foreach (var meta in metas)
            {
                await _metaRepository.DeleteAsync(meta);
            }
        });

        await RunSeedContributorsAsync(null);

        await WithUnitOfWorkAsync(async () =>
        {
            var activation = await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState");
            activation!.Items.Count.ShouldBeGreaterThan(0);

            var metas = await _metaRepository.GetListAsync(
                m => m.DictionaryCode == "TenantActivationState");
            metas.Select(m => m.ItemCode).OrderBy(x => x, StringComparer.Ordinal).ToList()
                .ShouldBe(activation.Items.Select(i => i.Code)
                    .OrderBy(x => x, StringComparer.Ordinal).ToList());
        });
    }

    [Fact]
    public async Task Should_Clean_Removed_Items_Orphan_Dictionaries_And_Orphan_Metas()
    {
        // 造三类残留：① 枚举字典里多出来的项（模拟从代码删掉的枚举成员）
        // ② 无对应枚举的静态字典（模拟整个枚举类型被删掉）
        // ③ 孤儿元数据（对应字典项不存在）
        await WithUnitOfWorkAsync(async () =>
        {
            var activation = await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState");
            activation!.AddOrUpdateItem("99", "已删除成员", "RemovedMember", isStatic: true);
            await _dictionaryRepository.UpdateAsync(activation);

            await _dictionaryRepository.InsertAsync(new DataDictionary(
                Guid.NewGuid(), null, "RemovedEnum", "已删除枚举", "AbpAdmin.Gone.RemovedEnum",
                [], isStatic: true));

            await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
                Guid.NewGuid(), null, "TenantActivationState", "99", DataDictionaryTagTypes.Default, 99));
            await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
                Guid.NewGuid(), null, "NoSuchDictionary", "0", DataDictionaryTagTypes.Default, 0));
            // 非静态字典（管理员通过 API 建的）及其项：同步绝不能碰
            var manual = new DataDictionary(
                Guid.NewGuid(), null, "ManualDict", "手工字典", null,
                [], isStatic: false);
            manual.AddOrUpdateItem("A", "手工项", null, isStatic: false);
            await _dictionaryRepository.InsertAsync(manual);
            await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
                Guid.NewGuid(), null, "ManualDict", "A", DataDictionaryTagTypes.Blue, 0));
        });

        await RunSeedContributorsAsync(null);

        await WithUnitOfWorkAsync(async () =>
        {
            var activation = await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState");
            activation!.Items.Count.ShouldBe(3);
            activation.Items.ShouldNotContain(i => i.Code == "99");

            (await _dictionaryRepository.FindAsync(d => d.Code == "RemovedEnum")).ShouldBeNull();

            var metas = await _metaRepository.GetListAsync();
            metas.ShouldNotContain(m => m.ItemCode == "99" && m.DictionaryCode == "TenantActivationState");
            metas.ShouldNotContain(m => m.DictionaryCode == "NoSuchDictionary");
            // 非静态字典与其元数据原样保留
            metas.ShouldContain(m => m.DictionaryCode == "ManualDict" && m.ItemCode == "A");

            var manual = await _dictionaryRepository.FindAsync(d => d.Code == "ManualDict");
            manual!.Items.Count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Should_Seed_Per_Tenant_Without_Touching_Host()    {
        var tenantId = Guid.NewGuid();

        var hostCount = await WithUnitOfWorkAsync(() => _dictionaryRepository.GetCountAsync());

        using (_currentTenant.Change(tenantId))
        {
            await RunSeedContributorsAsync(tenantId);

            var tenantDicts = await WithUnitOfWorkAsync(() => _dictionaryRepository.GetListAsync(includeDetails: true));
            // 租户拿到全套 12 个字典（模块无租户回落，必须逐租户种）
            tenantDicts.Count.ShouldBe(12);
            tenantDicts.ShouldAllBe(d => d.TenantId == tenantId);
        }

        var hostCountAfter = await WithUnitOfWorkAsync(() => _dictionaryRepository.GetCountAsync());
        hostCountAfter.ShouldBe(hostCount);

        // 同一枚举字典在 host 与租户下的行 Id 不同（哈希含租户，否则撞主键）
        Guid hostActivationId, tenantActivationId;
        hostActivationId = await WithUnitOfWorkAsync(async () =>
            (await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState"))!.Id);
        using (_currentTenant.Change(tenantId))
        {
            tenantActivationId = await WithUnitOfWorkAsync(async () =>
                (await _dictionaryRepository.FindAsync(d => d.Code == "TenantActivationState"))!.Id);
        }
        tenantActivationId.ShouldNotBe(hostActivationId);
    }

    [Fact]
    public async Task Should_Seed_When_Tenant_Created_Event_Published()
    {
        // 第 10 步：新建租户 → EntityCreatedEto<TenantEto> → AbpAdminDatabaseMigrationEventHandler 补跑种子。
        // 测试环境 IDistributedEventBus 是进程内 LocalDistributedEventBus，发布即同步处理。
        var tenantId = Guid.NewGuid();

        // 不在环境 UoW 里发布：与真实请求路径一致（处理器方法上禁用了环境 UoW，
        // 两个字典贡献者各自的 [UnitOfWork] 独立提交）。
        await _distributedEventBus.PublishAsync(
            new EntityCreatedEto<TenantEto>(new TenantEto { Id = tenantId, Name = "event-tenant" }));

        using (_currentTenant.Change(tenantId))
        {
            var dicts = await WithUnitOfWorkAsync(() => _dictionaryRepository.GetListAsync());
            dicts.Count.ShouldBe(12); // 6 手写 + 6 枚举同步，口径同上
            dicts.ShouldAllBe(d => d.TenantId == tenantId);
        }
    }

    private async Task RunSeedContributorsAsync(Guid? tenantId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _handWrittenContributor.SeedAsync(new DataSeedContext(tenantId));
            await _syncContributor.SeedAsync(new DataSeedContext(tenantId));
        });
    }

    /// <summary>行数 + 全内容快照（字典、字典项、元数据），用于幂等比对。</summary>
    private async Task<string> SnapshotAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var dicts = (await _dictionaryRepository.GetListAsync(includeDetails: true))
                .OrderBy(d => d.Code)
                .Select(d => $"{d.Id}|{d.Code}|{d.DisplayText}|{d.Description}|{d.IsStatic}|" +
                             string.Join(",", d.Items.OrderBy(i => i.Code).Select(i => $"{i.Code}:{i.DisplayText}:{i.Description}:{i.IsStatic}")))
                .ToList();
            var metas = (await _metaRepository.GetListAsync())
                .OrderBy(m => m.DictionaryCode).ThenBy(m => m.ItemCode)
                .Select(m => $"{m.DictionaryCode}|{m.ItemCode}|{m.TagType}|{m.Order}")
                .ToList();
            return string.Join("\n", dicts) + "\n==META==\n" + string.Join("\n", metas);
        });
    }
}
