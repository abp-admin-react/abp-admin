using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using EasyAbp.Abp.DataDictionary;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.DataDictionaries;

/* T3.4 第 12 步：字典管理面（视图合并 + 列表/创建/删除 + 原子保存）。
 * 模块 HttpApi 依赖已移除，这里是字典唯一的写入口与静态不变量的落点。 */
public abstract class DataDictionaryViewAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IDataDictionaryViewAppService _viewAppService;
    private readonly IDataDictionaryRepository _dictionaryRepository;
    private readonly IDataDictionaryManager _dictionaryManager;
    private readonly IRepository<DataDictionaryItemMeta, Guid> _metaRepository;
    private readonly IGuidGenerator _guidGenerator;

    protected DataDictionaryViewAppServiceTests()
    {
        _viewAppService = GetRequiredService<IDataDictionaryViewAppService>();
        _dictionaryRepository = GetRequiredService<IDataDictionaryRepository>();
        _dictionaryManager = GetRequiredService<IDataDictionaryManager>();
        _metaRepository = GetRequiredService<IRepository<DataDictionaryItemMeta, Guid>>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task GetAsync_Should_Return_Merged_Items_With_IsStatic_And_TagType()
    {
        var view = await _viewAppService.GetAsync("TenantActivationState");

        view.ShouldNotBeNull();
        view!.Code.ShouldBe("TenantActivationState");
        view.IsStatic.ShouldBeTrue();
        view.Items.Count.ShouldBe(3);

        // 模块 DTO 拿不到的 isStatic 与 tagType 都在（验收标准）
        var passive = view.Items.Single(i => i.Code == "2");
        passive.IsStatic.ShouldBeTrue();
        passive.TagType.ShouldBe(DataDictionaryTagTypes.Red);
        passive.DisplayText.ShouldBe("已停用");

        // 按元数据 Order 升序（枚举声明顺序），不是 Code 的字符串序
        view.Items.Select(i => i.Code).ShouldBe(["0", "1", "2"]);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Null_For_Unknown_Code()
    {
        (await _viewAppService.GetAsync("NoSuchDictionary")).ShouldBeNull();
    }

    [Fact]
    public async Task GetListAsync_Should_Return_Paged_List_Sorted_By_Code()
    {
        var page = await _viewAppService.GetListAsync(new PagedResultRequestDto
        {
            SkipCount = 0,
            MaxResultCount = 5
        });

        // 种子默认 12 个字典（6 手写 + 6 枚举同步），全部在 host 上下文
        page.TotalCount.ShouldBe(12);
        page.Items.Count.ShouldBe(5);
        // 列表项带 Id（前端 rowKey）与 isStatic，不带 Items
        page.Items.ShouldAllBe(i => i.Id != Guid.Empty);
        page.Items.Select(i => i.Code).ShouldBe(
            page.Items.Select(i => i.Code).OrderBy(x => x, StringComparer.Ordinal).ToList());

        var secondPage = await _viewAppService.GetListAsync(new PagedResultRequestDto
        {
            SkipCount = 5,
            MaxResultCount = 100
        });
        secondPage.Items.Count.ShouldBe(7);
        secondPage.Items.Select(i => i.Code).ShouldNotContain(page.Items.Select(i => i.Code).First());
    }

    [Fact]
    public async Task CreateAsync_Should_Create_NonStatic_Dictionary()
    {
        // IsStatic 不开放给 API：静态字典的权威来源是代码（种子），
        // 从界面建静态字典会被下次部署的枚举同步结构同步当残留清掉
        var view = await _viewAppService.CreateAsync(new CreateDataDictionaryInput
        {
            Code = "CreatedDict",
            DisplayText = "新建字典",
            Description = "created by test"
        });

        view.IsStatic.ShouldBeFalse();
        view.Code.ShouldBe("CreatedDict");
        view.Items.ShouldBeEmpty();

        (await _viewAppService.GetAsync("CreatedDict")).ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_Duplicate_Code()
    {
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.CreateAsync(new CreateDataDictionaryInput
            {
                Code = AbpAdminDictionaryCodes.Gender,
                DisplayText = "重复编码"
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.DataDictionaries.DictionaryCodeAlreadyExists);
    }

    [Fact]
    public async Task DeleteAsync_Should_Reject_Static_Dictionary()
    {
        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.DeleteAsync(AbpAdminDictionaryCodes.Gender));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.DataDictionaries.StaticStructureLocked);

        // 拒绝即未删
        (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender)).ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_NonStatic_Dictionary_And_Meta_Rows()
    {
        var dict = new DataDictionary(
            _guidGenerator.Create(), null, "DeleteMetaDict", "待删字典", null,
            new List<DataDictionaryItem>(), isStatic: false);
        dict.AddOrUpdateItem("A", "甲", null, isStatic: false);
        await _dictionaryManager.CreateAsync(dict);

        await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
            _guidGenerator.Create(), null, "DeleteMetaDict", "A", DataDictionaryTagTypes.Cyan, 0));

        await _viewAppService.DeleteAsync("DeleteMetaDict");

        (await _viewAppService.GetAsync("DeleteMetaDict")).ShouldBeNull();
        // 旁挂元数据行随字典一起清，不留孤儿
        (await _metaRepository.GetListAsync(x => x.DictionaryCode == "DeleteMetaDict")).ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Save_Texts_And_Meta_In_One_Call()
    {
        var view = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;

        await _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
        {
            DisplayText = view.DisplayText,
            Description = view.Description,
            Items = view.Items.Select(i => new SaveDataDictionaryItemInput
            {
                Code = i.Code,
                DisplayText = i.Code == "Male" ? "男性（原子保存改）" : i.DisplayText,
                Description = i.Description,
                TagType = i.Code == "Male" ? DataDictionaryTagTypes.Blue : i.TagType,
            }).ToList()
        });

        var after = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;
        var male = after.Items.Single(i => i.Code == "Male");
        male.DisplayText.ShouldBe("男性（原子保存改）");
        male.TagType.ShouldBe(DataDictionaryTagTypes.Blue);
        male.IsStatic.ShouldBeTrue(); // 静态标记不被文本更新冲掉
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Reject_Structure_Change_On_Static_Dictionary()
    {
        var view = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;

        // 静态字典漏传一项 = 结构变化，必须拒绝而不是静默删光
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
            {
                DisplayText = view.DisplayText,
                Description = view.Description,
                Items = view.Items.Take(2).Select(i => new SaveDataDictionaryItemInput
                {
                    Code = i.Code,
                    DisplayText = i.DisplayText,
                    Description = i.Description,
                }).ToList()
            }));

        // 拒绝后无部分写入：项集合保持原样（防"先动库后校验"的回归）
        var after = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;
        after.Items.Select(i => i.Code).ShouldBe(view.Items.Select(i => i.Code).ToList());
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Reject_Adding_Or_Renaming_Items_On_Static_Dictionary()
    {
        var view = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;

        static SaveDataDictionaryItemInput Item(string code) => new() { Code = code, DisplayText = code };

        // 多传一项 = 新增：静态字典不允许（结构锁的"增"方向）
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
            {
                DisplayText = view.DisplayText,
                Items = view.Items.Select(i => Item(i.Code)).Append(Item("Extra")).ToList(),
            }));

        // 改编码 = 删一加一：静态项编码被后端强依赖，SetEquals 必须拒绝
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
            {
                DisplayText = view.DisplayText,
                Items = view.Items
                    .Select(i => Item(i.Code == "Male" ? "Male2" : i.Code))
                    .ToList(),
            }));
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Reject_Duplicate_Codes()
    {
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
            {
                DisplayText = "性别",
                Items =
                [
                    new SaveDataDictionaryItemInput { Code = "Male", DisplayText = "男" },
                    new SaveDataDictionaryItemInput { Code = "Male", DisplayText = "男2" },
                ]
            }));
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Reject_Invalid_TagType()
    {
        var view = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;

        // 非法 TagType 必须在动库前整单拒绝
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
            {
                DisplayText = view.DisplayText,
                Description = view.Description,
                Items = view.Items.Select(i => new SaveDataDictionaryItemInput
                {
                    Code = i.Code,
                    DisplayText = i.DisplayText,
                    Description = i.Description,
                    TagType = i.Code == "Male" ? "not-a-color" : i.TagType,
                }).ToList()
            }));

        // 校验失败不落库：显示名保持原值
        var after = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;
        after.DisplayText.ShouldBe(view.DisplayText);
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Reject_Unknown_Dictionary()
    {
        await Should.ThrowAsync<BusinessException>(() =>
            _viewAppService.SaveItemsAsync("NoSuchDictionary", new SaveDataDictionaryItemsInput
            {
                DisplayText = "不存在的字典",
                Items = [new SaveDataDictionaryItemInput { Code = "A", DisplayText = "甲" }],
            }));
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Write_Order_By_Array_Position()
    {
        // 乱序提交：Order 必须跟随数组下标（前端拖拽排序的落库依据），而不是沿用旧值。
        // 回归成"恒 0"时 GetAsync 的稳定排序会让所有测试继续绿——所以直接断言 Order 数值。
        await _viewAppService.SaveItemsAsync(AbpAdminDictionaryCodes.Gender, new SaveDataDictionaryItemsInput
        {
            DisplayText = "性别",
            Items =
            [
                new SaveDataDictionaryItemInput { Code = "Female", DisplayText = "女" },
                new SaveDataDictionaryItemInput { Code = "Male", DisplayText = "男" },
                new SaveDataDictionaryItemInput { Code = "Unknown", DisplayText = "未知" },
            ]
        });

        var view = (await _viewAppService.GetAsync(AbpAdminDictionaryCodes.Gender))!;
        view.Items.Select(i => i.Code).ShouldBe(["Female", "Male", "Unknown"]);
        view.Items.Single(i => i.Code == "Female").Order.ShouldBe(0);
        view.Items.Single(i => i.Code == "Male").Order.ShouldBe(1);
        view.Items.Single(i => i.Code == "Unknown").Order.ShouldBe(2);
    }

    [Fact]
    public async Task SaveItemsAsync_Should_Replace_Items_And_Clean_Orphan_Metas_On_NonStatic_Dictionary()
    {
        var dict = new DataDictionary(
            _guidGenerator.Create(), null, "TestSaveDict", "原子保存测试字典", "desc",
            new List<DataDictionaryItem>(), isStatic: false);
        dict.AddOrUpdateItem("A", "甲", null, isStatic: false);
        dict.AddOrUpdateItem("B", "乙", null, isStatic: false);
        await _dictionaryManager.CreateAsync(dict);

        // 给 A 建一条展示元数据，删项后应作为孤儿一并清理
        await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
            _guidGenerator.Create(), null, "TestSaveDict", "A", DataDictionaryTagTypes.Cyan, 0));

        await _viewAppService.SaveItemsAsync("TestSaveDict", new SaveDataDictionaryItemsInput
        {
            DisplayText = "原子保存测试字典",
            Description = "desc",
            Items = [new SaveDataDictionaryItemInput { Code = "B", DisplayText = "乙（改）" }]
        });

        var after = (await _viewAppService.GetAsync("TestSaveDict"))!;
        after.Items.Select(i => i.Code).ShouldBe(["B"]);
        after.Items.Single().TagType.ShouldBeNull();

        // A 的孤儿元数据已删
        var metas = await _metaRepository.GetListAsync(x => x.DictionaryCode == "TestSaveDict");
        metas.Any(m => m.ItemCode == "A").ShouldBeFalse();
    }
}
