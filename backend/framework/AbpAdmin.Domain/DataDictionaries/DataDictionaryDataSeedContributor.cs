using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.Abp.DataDictionary;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 手写字典种子（T3.4 第 5 步）：没有对应 C# 枚举的字典在这里定义。
/// 对应枚举的字典（TenantActivationState/FileThumbnailState/DataScope）不在这里写——
/// 交给 EnumDataDictionarySyncDataSeedContributor 自动物化，否则枚举加成员时要同时改两处、必然漂移。
///
/// 全部以 IsStatic = true 写入：这些字典的项由代码定义（后端可能用 switch/常量引用），
/// 管理界面只允许改显示名，不允许改编码、不允许删除。
/// DisplayText 是「locale key 缺失时的中文兜底文案」（国际化走前端语言包），不是正规文案出处；
/// 英文环境兜底生效时显示中文属预期行为（与 MenuTemplateDefinition 的 Title 约定一致）。
/// 幂等且是「首次物化」：字典/字典项/元数据行已存在就只跳过（只补缺失项，绝不动内容），
/// 用户在管理页改过的显示名/TagType/Order 不会被重跑种子重置；
/// DbMigrator 与新建租户事件都会触发本种子。
/// </summary>
public class DataDictionaryDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IDataDictionaryRepository _dataDictionaryRepository;
    private readonly IDataDictionaryManager _dataDictionaryManager;
    private readonly DataDictionaryItemMetaManager _metaManager;
    private readonly IGuidGenerator _guidGenerator;

    public DataDictionaryDataSeedContributor(
        IDataDictionaryRepository dataDictionaryRepository,
        IDataDictionaryManager dataDictionaryManager,
        DataDictionaryItemMetaManager metaManager,
        IGuidGenerator guidGenerator)
    {
        _dataDictionaryRepository = dataDictionaryRepository;
        _dataDictionaryManager = dataDictionaryManager;
        _metaManager = metaManager;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        foreach (var def in Definitions)
        {
            // FindAsync 默认 includeDetails: true，模块的 EF 仓储 WithDetails 会带上 Items
            var dict = await _dataDictionaryRepository.FindAsync(d => d.Code == def.Code);
            var existingItemCodes = dict?.Items.Select(x => x.Code).ToHashSet() ?? new HashSet<string>();

            if (dict == null)
            {
                dict = new DataDictionary(
                    _guidGenerator.Create(),
                    context.TenantId,
                    def.Code,
                    def.DisplayText,
                    def.Description,
                    new List<DataDictionaryItem>(),
                    isStatic: true);
                foreach (var item in def.Items)
                {
                    dict.AddOrUpdateItem(item.Code, item.DisplayText, item.Description, isStatic: true);
                }
                await _dataDictionaryManager.CreateAsync(dict);
            }
            else
            {
                // 已存在的字典只补缺失项，不动内容：种子是"首次物化"，用户在管理页改过的
                // 显示名/TagType/Order 不能在重新部署时被定义表重置
                // （代价：代码侧改定义不再自动传播，需删行重种或界面改）。
                foreach (var item in def.Items)
                {
                    if (dict.Items.All(x => x.Code != item.Code))
                    {
                        dict.AddOrUpdateItem(item.Code, item.DisplayText, item.Description, isStatic: true);
                    }
                }

                if (dict.Items.Count != existingItemCodes.Count)
                {
                    await _dataDictionaryManager.UpdateAsync(dict);
                }
            }

            // 元数据（TagType/Order）只补缺失行：管理页保存走 SaveForDictionaryAsync（用户改动必须落库），
            // 种子侧用 UpsertIfMissingAsync，避免重跑种子把用户自定义颜色/顺序打回定义表默认值
            var order = 0;
            foreach (var item in def.Items)
            {
                await _metaManager.UpsertIfMissingAsync(def.Code, item.Code, item.TagType, order++);
            }
        }
    }

    private sealed record ItemDef(string Code, string DisplayText, string? Description = null, string? TagType = null);

    private sealed record DictionaryDef(string Code, string DisplayText, string? Description, ItemDef[] Items);

    /// <summary>
    /// 手写字典定义表。每条都标明属于哪一类：
    /// 「纯配置」= 没有对应 C# 枚举、值集合可能随业务调整；
    /// 「翻译表」= 值集合被后端代码强依赖，字典只是统一前端的渲染方式，不要往里面加值。
    /// </summary>
    private static readonly DictionaryDef[] Definitions =
    [
        // 纯配置
        new(AbpAdminDictionaryCodes.Gender, "性别", "纯配置字典（T3.4 手写种子）",
        [
            new("Unknown", "未知"),
            new("Male", "男"),
            new("Female", "女")
        ]),

        // 纯配置
        new(AbpAdminDictionaryCodes.EnabledStatus, "启用状态", "纯配置字典（T3.4 手写种子）",
        [
            new(AbpAdminDictionaryCodes.EnabledStatusItems.Enabled, "启用", TagType: DataDictionaryTagTypes.Success),
            new(AbpAdminDictionaryCodes.EnabledStatusItems.Disabled, "停用", TagType: DataDictionaryTagTypes.Error)
        ]),

        // 翻译表：对齐 T3.5 通知渠道标识（EasyAbp NotificationService 的 NotificationMethod 值：
        // Mailing = 模块 Mailing provider，Sms = 模块 Sms provider，InApp = 自研站内信）
        new(AbpAdminDictionaryCodes.NotificationMethod, "通知渠道", "翻译表（对齐 T3.5 渠道标识，值集合由后端渠道实现决定）",
        [
            new("Mailing", "邮件"),
            new("Sms", "短信"),
            new("InApp", "站内信")
        ]),

        // 翻译表：审计日志筛选下拉
        new(AbpAdminDictionaryCodes.AuditLogHttpMethod, "审计日志 HTTP 方法", "翻译表（审计日志筛选）",
        [
            new("GET", "GET"),
            new("POST", "POST"),
            new("PUT", "PUT"),
            new("DELETE", "DELETE"),
            new("PATCH", "PATCH")
        ]),

        // 翻译表：对齐 T3.3 ScheduledJobExecution.Success（bool）的 JSON 表示
        new(AbpAdminDictionaryCodes.ScheduledJobResult, "定时作业执行结果", "翻译表（对齐 T3.3 执行结果）",
        [
            new("true", "成功", TagType: DataDictionaryTagTypes.Success),
            new("false", "失败", TagType: DataDictionaryTagTypes.Error)
        ]),

        // 翻译表：T2.4 的状态由 ReadyTime 推导（无独立状态字段），Pending = 准备中，Ready = 已就绪
        new(AbpAdminDictionaryCodes.GdprRequestState, "GDPR 请求状态", "翻译表（T2.4 由 ReadyTime 推导）",
        [
            new("Pending", "准备中", TagType: DataDictionaryTagTypes.Processing),
            new("Ready", "已就绪", TagType: DataDictionaryTagTypes.Success)
        ])
    ];
}
