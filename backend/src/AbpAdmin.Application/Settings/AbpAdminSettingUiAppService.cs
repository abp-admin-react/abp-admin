using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using EasyAbp.Abp.SettingUi;
using EasyAbp.Abp.SettingUi.Authorization;
using EasyAbp.Abp.SettingUi.Dto;
using EasyAbp.Abp.SettingUi.Extensions;
using EasyAbp.Abp.SettingUi.Localization;
using EasyAbp.Abp.SettingUi.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Json;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.Settings;

/// <summary>
/// SettingUi 应用服务替换（T3.5）：加密设置项脱敏 + 写权限收口。
/// 2.10.0 反编译核实的基类行为：host 侧 GetSettingValueAsync 经 SettingProvider 返回
/// 解密后的明文——SMTP 密码、短信 SecretKey 会随 GET /api/setting-ui 泄露给任何有
/// 设置页权限的人。这里覆写取值，加密项永不下发值（前端显示空）；
/// 写入侧基类只在租户侧跳过"空值加密项"，host 侧会用空串覆盖密码——覆写 SetSettingAsync
/// 统一跳过，实现"提交空值 = 不修改"。
/// "重置为默认值"显式绕过该守卫（ResetSettingValuesAsync 走的就是 SetAsync(name, null)）：
/// 重置是用户明确意图，加密项也应当能被清回默认值。
/// [RemoteService(false)]：Host 对整个 Application 程序集建常规控制器，
/// 不关掉的话本类会被额外暴露一份 /api/app/setting-ui（EasyAbp 的 /api/setting-ui 控制器不受影响）。
/// [Authorize(ShowSettingPage)]：上游 EasyAbp.SettingUi（含 2.10.0）的 SettingUiAppService 与
/// SettingUiController 都没有 [Authorize]，/api/setting-ui/* 实际匿名可达——此前只有前端
/// access.ts 的 canManageSettings 在拦（UI 不是防线）。授权拦截器作用于本服务实例，
/// EasyAbp 控制器注入的正是本类代理，类级特性即覆盖 GET/写值/重置全部入口。
/// 读写分离（模块五收口）：类级 ShowSettingPage 是读门禁；SetSettingValuesAsync /
/// ResetSettingValuesAsync 叠加方法级 AbpAdmin.SettingUi.Update（ABP 拦截器会把类级
/// 与方法级 Authorize 组合成"两者都须满足"，故 Update 隐含要求读权限）。
/// 审计：写值入参 Dictionary&lt;string,string&gt; 含明文新密钥，在宿主 AbpAuditingOptions.IgnoredTypes
/// 里整体不序列化（控制器层和应用服务层共用该开关）；"谁改了什么"的语义轨迹由
/// _operationLogWriter 手写（上游 SettingUiController 不是自动 API 控制器，
/// [OperationLog] 特性挂不上它的 MethodInfo，只能服务内手写）。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ISettingUiAppService), typeof(SettingUiAppService))]
[Volo.Abp.RemoteService(false)]
[Authorize(SettingUiPermissions.ShowSettingPage)]
public class AbpAdminSettingUiAppService : SettingUiAppService
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingManager _settingManager;
    private readonly IOperationLogWriter _operationLogWriter;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AbpAdminSettingUiAppService(
        IOptions<AbpSettingUiOptions> options,
        IStringLocalizer<SettingUiResource> localizer,
        IStringLocalizerFactory factory,
        IVirtualFileProvider fileProvider,
        IJsonSerializer jsonSerializer,
        ISettingDefinitionManager settingDefinitionManager,
        ISettingManager settingManager,
        ITimezoneProvider timezoneProvider,
        ICurrentTimezoneProvider currentTimezoneProvider,
        IPermissionDefinitionManager permissionDefinitionManager,
        IOperationLogWriter operationLogWriter,
        ICorrelationIdProvider correlationIdProvider,
        IHttpContextAccessor httpContextAccessor)
        : base(options, localizer, factory, fileProvider, jsonSerializer, settingDefinitionManager,
            settingManager, timezoneProvider, currentTimezoneProvider, permissionDefinitionManager)
    {
        // 基类里这两个依赖是 private，重置流程需要直接用
        _settingDefinitionManager = settingDefinitionManager;
        _settingManager = settingManager;
        _operationLogWriter = operationLogWriter;
        // 操作日志的 CorrelationId 与 ABP AuditLog/SecurityLog 同源（ICorrelationIdProvider），
        // 是手动日志与审计日志的 join 键——审计参数已被 IgnoredTypes 关掉，join 不到就成了孤儿轨迹
        _correlationIdProvider = correlationIdProvider;
        // IP/UA 与既有 OperationLogActionFilter 写出的日志字段对齐（Chrome 实测发现旧记录有、
        // 手写记录缺，语义轨迹质量不该因入口不同而缩水）
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<SettingInfo> CreateSettingInfoAsync(SettingDefinition settingDefinition)
    {
        var info = await base.CreateSettingInfoAsync(settingDefinition);

        if (settingDefinition.IsEncrypted)
        {
            info.Value = null;
            // 加密项一律用密码框（基类/模块元数据对 SMTP 密码给的是明文 text）——
            // 与"不下发值、留空不改"的约定配套，在这里统一做比在定义侧改元数据更可靠
            // （定义侧改动取决于 provider 执行顺序，不可控）。
            info.Properties[SettingUiConst.Type] = AbpAdminSettingUiConsts.PasswordType;
        }

        return info;
    }

    /// <summary>
    /// 写值入口：读门禁（类级 ShowSettingPage）之外再要求 SettingUi.Update。
    /// 上游基类实现已知缺陷（2026-09 main 分支核实，2.10.0 同）：
    /// SetSettingValuesAsync 的 dateTime 分支在时区转换成功路径上误写 return（应为 continue），
    /// 会中断整个循环、静默丢弃后续所有设置项。当前系统没有任何 dateTime 类型的设置项
    /// （由 SettingUiDateTimePinTests 钉住），故不整方法复制修正——一旦需要 dateTime 项，
    /// 该测试会红，届时必须覆写整个方法并顺手修掉上游 bug。
    /// 操作日志走 OnCompleted（提交后）写：OperationLogWriter 用 requiresNew UoW 开第二个连接，
    /// SQLite 单写锁下在业务事务还开着时插日志会锁死 30 秒后静默丢弃——与仓库既有的
    /// OnCompleted 后置写入同款修法（Language/LanguageText/TextTemplate/ScheduledJob 等）。
    /// 失败路径不写操作日志——
    /// ABP 审计对失败调用本身有记录（方法+用户+异常），语义轨迹只在成功提交后落。
    /// </summary>
    [Authorize(AbpAdminPermissions.SettingUi.Update)]
    public override async Task SetSettingValuesAsync(Dictionary<string, string> settingValues)
    {
        await base.SetSettingValuesAsync(settingValues);

        // 参数含明文新密钥，ABP 审计参数序列化已被 IgnoredTypes 关掉；
        // 语义轨迹在这里手写，只记设置名不记值。
        // 只保留真实提交的项名（表单键还原成 Abp.X.Y 设置名）；前端会把整个表单原样回传
        // （未改的项值为空串），空值项没有信息量。密钥值永不落库。
        var changedKeys = settingValues
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .Select(kv => ResolveSettingName(kv.Key))
            .Where(name => name != null)
            .Select(name => name!)
            .ToList();

        WriteOperationLogOnCommit(new OperationLogEntry
        {
            Type = "设置管理",
            SubType = "保存设置值",
            Action = "更新了系统设置",
            Extra = string.Join(", ", changedKeys),
            Success = true,
            RequestMethod = "PUT",
            RequestUrl = "/api/setting-ui/set-setting-values",
            CorrelationId = _correlationIdProvider.Get(),
            ClientIpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(),
        });
    }

    /// <summary>
    /// 显式重置：逐层清除当前上下文能写到的全部覆盖（U → T → G，按上下文裁剪），
    /// 让设置回到默认值。基类实现只对 ShouldManageAsGlobal 路由出的"单一条链"调
    /// SetAsync(name, null)——当值实际存放在其他层时清不掉（典型：host 上下文
    /// ManageGlobalSettingsOnHostSide=false 时保存写 T 层，而运维经 SetGlobalAsync
    /// 写的 G 层值永远清不到；反之亦然）。
    /// </summary>
    [Authorize(AbpAdminPermissions.SettingUi.Update)]
    public override async Task ResetSettingValuesAsync(List<string> settingNames)
    {
        var processed = new List<string>();

        foreach (var settingName in settingNames)
        {
            var definition = await _settingDefinitionManager.GetOrNullAsync(settingName);
            if (definition == null)
            {
                continue;
            }

            processed.Add(definition.Name);

            // SetAsync(name, null, provider, key) 在 SettingManager 内部走 ClearAsync 分支：
            // 加密项（Encrypt(null)=null）同样删除覆盖行，不需要特殊处理。
            if (CurrentUser.IsAuthenticated)
            {
                await _settingManager.SetAsync(
                    definition.Name, null, UserSettingValueProvider.ProviderName, CurrentUser.Id!.ToString());
            }

            if (CurrentTenant.IsAvailable)
            {
                await _settingManager.SetAsync(
                    definition.Name, null, TenantSettingValueProvider.ProviderName, CurrentTenant.Id!.ToString());
            }

            // 全局层只允许 host 上下文清：租户管理员的重置不应抹掉全局默认
            if (!CurrentTenant.IsAvailable)
            {
                await _settingManager.SetAsync(
                    definition.Name, null, GlobalSettingValueProvider.ProviderName, null);
            }
        }

        WriteOperationLogOnCommit(new OperationLogEntry
        {
            Type = "设置管理",
            SubType = "重置设置值",
            Action = "重置了系统设置",
            // 只记真实处理过的项名（静默跳过的未定义项不算"已重置"）
            Extra = string.Join(", ", processed),
            Success = true,
            RequestMethod = "PUT",
            RequestUrl = "/api/setting-ui/reset-setting-values",
            CorrelationId = _correlationIdProvider.Get(),
            ClientIpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(),
        });
    }

    /// <summary>
    /// 表单键还原设置名，与上游 SetSettingValuesAsync 的键名变换同构：
    /// Setting_AbpAdmin_Sms_SecretKey → AbpAdmin.Sms.SecretKey；
    /// 非表单键（无 Setting_ 前缀）返回 null。
    /// </summary>
    private static string? ResolveSettingName(string formKey)
    {
        var pascalCase = formKey.ToPascalCase();
        if (!pascalCase.StartsWith(SettingUiConst.FormNamePrefix))
        {
            return null;
        }

        return pascalCase.RemovePreFix(SettingUiConst.FormNamePrefix).UnderscoreToDot();
    }

    /// <summary>
    /// 操作日志统一走 UoW 提交后（OnCompleted）写入，与仓库既有的后置写入同款修法
    /// （Language/TextTemplate/ScheduledJob 等）：
    /// requiresNew 写日志在业务事务内执行会与 SQLite 单写锁死锁。
    /// 无环境 UoW 时（理论上不会有——PUT 入口必有 UoW 拦截器）退化为 fire-and-forget
    /// 直接写（`_ =` 丢弃任务：日志链路 fail-open，异常不反噬调用方）。
    /// </summary>
    private void WriteOperationLogOnCommit(OperationLogEntry entry)
    {
        var uow = UnitOfWorkManager.Current;
        if (uow == null)
        {
            _ = _operationLogWriter.WriteAsync(entry);
            return;
        }

        uow.OnCompleted(async () =>
        {
            try
            {
                await _operationLogWriter.WriteAsync(entry);
            }
            catch (Exception ex)
            {
                // 日志链路 fail-open：落不了操作日志不能反过来影响已提交的设置变更
                Logger.LogWarning(ex, "SettingUi 操作日志写入失败（Type={Type}, SubType={SubType}）",
                    entry.Type, entry.SubType);            }
        });
    }

    protected override Task SetSettingAsync(SettingDefinition setting, string value)
    {
        if (setting.IsEncrypted && string.IsNullOrEmpty(value))
        {
            return Task.CompletedTask;
        }

        // 租户上下文永不写 G 层：上游按定义的 Providers 路由，若未来某模块的设置定义
        // 声明了 Global provider，租户管理员经此入口会改到宿主全局值（Setting 实体本身
        // 不分租户）。钳到租户层，宿主全局只能由 host 上下文写——与 Reset 的
        // !CurrentTenant.IsAvailable 才清 G 层同一口径。
        if (CurrentTenant.IsAvailable && setting.Providers.Contains(GlobalSettingValueProvider.ProviderName))
        {
            return _settingManager.SetForCurrentTenantAsync(setting.Name, value);
        }

        return base.SetSettingAsync(setting, value);
    }
}
