using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.IpRegions;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AuditLogging;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using Volo.Abp.Validation;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 审计日志查询与「已处理」错误排障工作流（对标 ruoyi ApiErrorLog 标记已处理）。
/// 处理状态存储在 AuditLog 行内扩展属性映射列（无侧表）：写经 SetProperty、
/// 筛选/读经 <see cref="IAuditLogHandledColumnQueries"/>（EF 特有表达式收在 EF 层）；
/// 错误口径唯一出处 <see cref="AuditLogQueryExtensions.IsErrorPredicate"/>，
/// 列表/导出/错误率共用，防止口径分叉。
/// </summary>
[Authorize(AbpAdminPermissions.AuditLogs.Default)]
public class AuditLogAppService : AbpAdminAppService, IAuditLogAppService
{
    /// <summary>Sorting 白名单：客户端自由文本进 Dynamic LINQ 前先校验（对齐 OperationLogAppService 先例）。</summary>
    private static readonly string[] SortableFields =
    [
        nameof(AuditLog.ExecutionTime),
        nameof(AuditLog.ExecutionDuration),
        nameof(AuditLog.UserName),
        nameof(AuditLog.HttpStatusCode),
    ];

    /// <summary>实体变更历史的排序白名单：GetEntityChangeListAsync 内部同样是 Dynamic LINQ OrderBy。</summary>
    private static readonly string[] EntityChangeSortableFields =
    [
        nameof(EntityChange.ChangeTime),
    ];

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogHandledColumnQueries _handledColumnQueries;
    private readonly IAuditLogEntityChangeQueries _entityChangeQueries;
    private readonly IIpLocationResolver _ipLocationResolver;
    private readonly AuditLogExcelBuilder _auditLogExcelBuilder;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public AuditLogAppService(
        IAuditLogRepository auditLogRepository,
        IAuditLogHandledColumnQueries handledColumnQueries,
        IAuditLogEntityChangeQueries entityChangeQueries,
        IIpLocationResolver ipLocationResolver,
        AuditLogExcelBuilder auditLogExcelBuilder,
        IBackgroundJobManager backgroundJobManager)
    {
        _auditLogRepository = auditLogRepository;
        _handledColumnQueries = handledColumnQueries;
        _entityChangeQueries = entityChangeQueries;
        _ipLocationResolver = ipLocationResolver;
        _auditLogExcelBuilder = auditLogExcelBuilder;
        _backgroundJobManager = backgroundJobManager;
    }

    public virtual async Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input)
    {
        // Sorting 白名单对两个分支一体生效：普通分支的 input.Sorting 经 GetListByInputAsync
        // 同样进 ABP 仓储的 Dynamic LINQ OrderBy，只校验「未处理错误」分支等于留了一半旁路。
        ValidateSorting(input.Sorting);

        List<AuditLog> items;
        long count;
        if (input.UnhandledErrorOnly == true)
        {
            // 「仅未处理错误」工作流视图：错误口径与导出/错误率统计共用 IsErrorPredicate()，
            // 未处理 = HandledAt 映射列 IS NULL（单表条件，经 IAuditLogHandledColumnQueries 注入）。
            var repository = LazyServiceProvider.LazyGetRequiredService<IRepository<AuditLog, Guid>>();
            var queryable = (await repository.GetQueryableAsync())
                .ApplyUnhandledErrorFilters(input, _handledColumnQueries);

            count = await AsyncExecuter.CountAsync(queryable);
            items = await AsyncExecuter.ToListAsync(queryable
                .OrderBy(string.IsNullOrWhiteSpace(input.Sorting) ? "ExecutionTime desc" : input.Sorting)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));
        }
        else
        {
            count = await _auditLogRepository.GetCountByInputAsync(input);
            items = await _auditLogRepository.GetListByInputAsync(
                input, input.MaxResultCount, input.SkipCount);
        }

        var dtos = items.Select(x => Map(x)).ToList();
        await AttachHandledStatesAsync(dtos);

        // IP 归属地按当页去重解析（内部走缓存），不在查询里 join
        var locationMap = await _ipLocationResolver.ResolveManyAsync(dtos.Select(x => x.ClientIpAddress));
        foreach (var dto in dtos)
        {
            if (dto.ClientIpAddress != null && locationMap.TryGetValue(dto.ClientIpAddress, out var location))
            {
                dto.IpLocation = location;
            }
        }

        return new PagedResultDto<AuditLogDto>(count, dtos);
    }

    /// <summary>
    /// 单条日志详情：含动作/实体变更明细、处理状态（列投影）与 IP 归属地。
    /// </summary>
    public virtual async Task<AuditLogDto> GetAsync(Guid id)
    {
        var log = await _auditLogRepository.GetAsync(id, includeDetails: true);
        var dto = Map(log, true);
        await AttachHandledStatesAsync([dto]);
        if (dto.ClientIpAddress != null
            && (await _ipLocationResolver.ResolveManyAsync([dto.ClientIpAddress])).TryGetValue(dto.ClientIpAddress, out var location))
        {
            dto.IpLocation = location;
        }

        return dto;
    }

    /// <summary>
    /// 标记审计日志为已处理（对标 ruoyi ApiErrorLog 的标记已处理）。幂等 upsert：
    /// 重复标记刷新处理人/时间/备注（同值覆盖，last-write-wins）。普通日志也允许标记。
    /// 状态写在 AuditLog 行内的 HandledAt/HandledByUserId/HandledByName/HandledNote
    /// 扩展属性映射列上（AbpAdminEfCoreEntityExtensionMappings）。
    /// 并发语义：AggregateRoot 自带 ConcurrencyStamp（乐观并发），多管理员真正同时标记
    /// 时后写者会撞 409（AbpDbConcurrencyException）——本操作是纯覆盖、无丢失更新风险，
    /// 故就地重试取最新值再写（见 RetryOnConcurrency），把 last-write-wins 真正落到实处，
    /// 不必再像侧表时代那样靠分布式锁串行化。
    /// </summary>
    [Authorize(AbpAdminPermissions.AuditLogs.HandleErrors)]
    [OperationLog("审计日志", "标记已处理", BizNo = "{{id}}", Success = "标记审计日志 {{id}} 为已处理")]
    public virtual async Task MarkHandledAsync(Guid id, MarkAuditLogHandledInput input)
    {
        // 直接经服务调用的入参不走 DTO 注解校验，与原实体 Check.Length 同强度兜底
        Check.Length(input?.Note, nameof(input.Note), AuditLogHandleConsts.MaxNoteLength);
        Check.Length(CurrentUser.UserName, nameof(CurrentUser.UserName), AuditLogHandleConsts.MaxHandledByNameLength);

        await RetryOnConcurrency(async () =>
        {
            var log = await _auditLogRepository.GetAsync(id);
            log.SetProperty(AuditLogHandleConsts.HandledAtPropertyName, Clock.Now);
            log.SetProperty(AuditLogHandleConsts.HandledByUserIdPropertyName, CurrentUser.GetId());
            log.SetProperty(AuditLogHandleConsts.HandledByNamePropertyName, CurrentUser.UserName);
            log.SetProperty(AuditLogHandleConsts.HandledNotePropertyName, input?.Note);
            await _auditLogRepository.UpdateAsync(log, autoSave: true);
        });
    }

    /// <summary>取消已处理标记；未标记或日志不存在时静默成功（前端按钮幂等）。并发重试语义同标记。</summary>
    [Authorize(AbpAdminPermissions.AuditLogs.HandleErrors)]
    [OperationLog("审计日志", "取消已处理", BizNo = "{{id}}", Success = "取消了审计日志 {{id}} 的已处理标记")]
    public virtual async Task UnmarkHandledAsync(Guid id)
    {
        // 未标记：静默成功（前端按钮幂等），不发无谓 UPDATE
        var log = await _auditLogRepository.FindAsync(id);
        if (log == null
            || log.GetProperty<DateTime?>(AuditLogHandleConsts.HandledAtPropertyName) == null)
        {
            return;
        }

        await RetryOnConcurrency(async () =>
        {
            var current = await _auditLogRepository.GetAsync(id);
            current.SetProperty(AuditLogHandleConsts.HandledAtPropertyName, null);
            current.SetProperty(AuditLogHandleConsts.HandledByUserIdPropertyName, null);
            current.SetProperty(AuditLogHandleConsts.HandledByNamePropertyName, null);
            current.SetProperty(AuditLogHandleConsts.HandledNotePropertyName, null);
            await _auditLogRepository.UpdateAsync(current, autoSave: true);
        });
    }

    /// <summary>
    /// 乐观并发重试：撞 ConcurrencyStamp（409 来源）就开**独立 UoW** 重取最新实体再执行，
    /// 最多 3 次。必须 requiresNew——并发冲突后当前 UoW 的 DbContext 已被失败的
    /// 变更跟踪器污染（SaveChanges 会重放同一条过期更新，重试永远再撞），
    /// 只有新 UoW 的新 DbContext 才能拿到真正最新值。
    /// 仅适用于「整行覆盖、无读改写逻辑依赖」的幂等写（标记/取消正是）；
    /// 重试耗尽仍冲突则上抛，走 ABP 统一异常管道（409），不掩盖真实并发热点。
    /// </summary>
    private async Task RetryOnConcurrency(Func<Task> action)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using (var uow = UnitOfWorkManager.Begin(
                           new Volo.Abp.Uow.AbpUnitOfWorkOptions(),
                           requiresNew: true))
                {
                    await action();
                    await uow.CompleteAsync();
                }

                return;
            }
            catch (AbpDbConcurrencyException) when (attempt < maxAttempts)
            {
                // action 内部已重取实体；这里只吞掉可重试的并发冲突
            }
        }
    }

    /// <summary>
    /// 按当页 DTO 批量附加处理状态（防 N+1：一次列投影查询）。
    /// 读走 <see cref="IAuditLogHandledColumnQueries.GetStatesAsync"/> 的影子列投影——
    /// 不能用 GetProperty：模块仓储 GetListAsync 是 AsNoTracking，映射列的 track 回填
    /// 不发生（详情 GetAsync 虽是 tracking，统一走同一读路径避免两套口径）。
    /// </summary>
    private async Task AttachHandledStatesAsync(List<AuditLogDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var states = await _handledColumnQueries.GetStatesAsync(dtos.Select(x => x.Id).ToList());
        var stateMap = states.ToDictionary(x => x.AuditLogId);
        foreach (var dto in dtos)
        {
            if (stateMap.TryGetValue(dto.Id, out var state))
            {
                dto.IsHandled = state.IsHandled;
                dto.HandledAt = state.HandledAt;
                dto.HandledBy = state.HandledByName;
                dto.HandleNote = state.HandledNote;
            }
        }
    }

    /// <summary>
    /// 异步导出审计日志。转后台作业，完成后发邮件通知。
    /// 同步导出请走 Controller 的 GET /api/app/audit-log/export。
    /// ABP 动态 API 路由：POST /api/app/audit-log/enqueue-export
    /// T2.5: 操作限流 - 按当前用户 1 天 10 次（租户隔离）
    /// </summary>
    [Authorize(AbpAdminPermissions.AuditLogs.Export)]
    [OperationRateLimiting(OperationRateLimitingPolicyNames.AuditLogExport)]
    public virtual async Task<AuditLogExportResultDto> EnqueueExportAsync(GetAuditLogListInput input)
    {
        // 入队失败直接上抛：ABP 异常处理管道已有统一日志，这里再 catch-log-rethrow 只会重复记一遍
        await _backgroundJobManager.EnqueueAsync(
            new AuditLogExportJobArgs
            {
                TenantId = CurrentTenant.Id,
                UserId = CurrentUser.GetId(),
                Email = CurrentUser.Email ?? string.Empty,
                Filter = input
            });
        return AuditLogExportResultDto.Queued();
    }

    /// <summary>
    /// 单实体完整变更历史。优先用实体级权限，未定义或未授予时回退到 AbpAdmin.AuditLogs。
    /// </summary>
    public virtual async Task<PagedResultDto<EntityChangeHistoryDto>> GetEntityChangeHistoryAsync(
        GetEntityChangeHistoryInput input)
    {
        // 实体级权限检查：未定义或未授予时回退到 AbpAdmin.AuditLogs
        var permissionChecker = LazyServiceProvider.LazyGetRequiredService<IPermissionChecker>();
        var specific = AbpAdminPermissions.AuditLogging.ViewChangeHistory(input.EntityTypeFullName);
        if (await permissionChecker.IsGrantedAsync(specific) == false)
        {
            await AuthorizationService.CheckAsync(AbpAdminPermissions.AuditLogs.Default);
        }

        // 变更历史的 Sorting 同样直达 Dynamic LINQ，须过白名单（null/空白合法，走默认 ChangeTime DESC）
        if (!SortingWhitelist.IsValid(input.Sorting, EntityChangeSortableFields))
        {
            throw new AbpValidationException(
                L["AbpAdmin:InvalidSorting", input.Sorting ?? string.Empty]);
        }

        var count = await _auditLogRepository.GetEntityChangeCountAsync(
            entityId: input.EntityId,
            entityTypeFullName: input.EntityTypeFullName);

        var items = await _auditLogRepository.GetEntityChangeListAsync(
            sorting: input.Sorting ?? "ChangeTime DESC",
            maxResultCount: input.MaxResultCount,
            skipCount: input.SkipCount,
            entityId: input.EntityId,
            entityTypeFullName: input.EntityTypeFullName,
            includeDetails: true);

        // 取用户名：只按当页 items 涉及的 AuditLogId 批量取（热点实体可能有上千条变更，
        // 之前的做法会把该实体全部变更拉回来，只为给当页 10 条填 UserName）
        var auditLogIds = items.Select(x => x.AuditLogId).Distinct().ToList();
        // 显式声明 Dictionary<Guid, string?>（含值选择器的 (string?) 转换）消除 CS8619：
        // ABP 10.6 的 AuditLog.UserName 声明为不可空 string（匿名请求运行时可为 null），
        // 三元两臂会分别推断出 Dictionary<Guid,string?> 与 Dictionary<Guid,string>，空异性不一致
        Dictionary<Guid, string?> usernameMap = auditLogIds.Count == 0
            ? new()
            : (await _auditLogRepository.GetListAsync(x => auditLogIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => (string?)x.UserName);

        var dtos = items.Select(x => new EntityChangeHistoryDto
        {
            Id = x.Id,
            AuditLogId = x.AuditLogId,
            ChangeTime = x.ChangeTime,
            ChangeType = (byte)x.ChangeType,
            EntityTypeFullName = x.EntityTypeFullName,
            EntityId = x.EntityId,
            UserName = usernameMap.GetValueOrDefault(x.AuditLogId),
            PropertyChanges = x.PropertyChanges.Select(p => new EntityPropertyChangeDto
            {
                PropertyName = p.PropertyName,
                OriginalValue = p.OriginalValue,
                NewValue = p.NewValue
            }).ToList()
        }).ToList();

        return new PagedResultDto<EntityChangeHistoryDto>(count, dtos);
    }

    /// <summary>
    /// 按一条实体变更记录回滚实体属性（把 OriginalValue 写回实体，借鉴 abp-next-admin EntityRestoreAppService）。
    /// 实现要点：
    /// 1) OriginalValue 由 ABP EntityHistoryHelper 以 JSON 序列化落库（含 null → "null"），反序列化按属性真实类型走
    ///    System.Text.Json，失败再按 InvariantCulture 显式转换兜底（历史手工造数）；
    /// 2) 实体类型双重收口：必须落在 <see cref="AuditLoggingEntityTypes.ChangeHistoryEnabled"/> 白名单内
    ///    （AddAllEntities 审计面是全库，回滚写权不允许外溢到任意 Guid 实体），再按存储 FullName 反解且仅放行
    ///    IEntity&lt;Guid&gt;；
    /// 3) 敏感防护：脱敏黑名单属性（PasswordHash/SecurityStamp/Payload/ClientSecret…）与任何
    ///    "[REDACTED]" 掩码值一律跳过——脱敏发生在落库前，掩码回流等于把字面量写进凭据字段；
    /// 4) 无任何属性被还原即抛错不落库——绝不留下"半回滚"状态；无法还原的属性（影子属性/敏感/掩码/不可写）
    ///    单独跳过并在结果中说明，不阻断整体；
    /// 5) 回滚本身也是一次受审计的变更（EntityHistory 全实体记录），因此可以继续回滚本次回滚（前滚）。
    /// </summary>
    [Authorize(AbpAdminPermissions.AuditLogs.Restore)]
    [OperationLog("审计日志", "回滚实体变更", BizNo = "{{input.entityId}}", Success = "回滚了 {{input.entityChangeId}}")]
    public virtual async Task<EntityRestoreResultDto> RestoreEntityChangeAsync(RestoreEntityChangeInput input)
    {
        // 回滚写权收敛到白名单实体：AddAllEntities 的审计面是全库，若无此门，
        // Restore 权限持有者可对任意 Guid 实体的任意可写属性做历史还原。
        // 只依赖 input（无需先查变更记录），白名单外的类型名在最前面就拒绝
        if (!AuditLoggingEntityTypes.ChangeHistoryEnabled.Contains(input.EntityTypeFullName))
        {
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreEntityTypeNotAllowed", input.EntityTypeFullName],
                code: AbpAdminDomainErrorCodes.EntityRestore.EntityTypeNotAllowed);
        }

        var change = await _entityChangeQueries.FindWithPropertiesAsync(input.EntityChangeId)
            ?? throw new UserFriendlyException(
                L["AbpAdmin:EntityChangeNotFound", input.EntityChangeId],
                code: AbpAdminDomainErrorCodes.EntityChange.NotFound);

        // 变更记录必须属于声明的实体，防止拿 A 实体的变更去恢复 B 实体
        if (change.EntityId != input.EntityId || change.EntityTypeFullName != input.EntityTypeFullName)
        {
            throw new UserFriendlyException(
                L["AbpAdmin:EntityChangeEntityMismatch"],
                code: AbpAdminDomainErrorCodes.EntityChange.EntityMismatch);
        }

        if (change.ChangeType != EntityChangeType.Updated)
        {
            // Created 的回滚=删除实体（危险且需级联策略），Deleted 的回滚=重建实体（同 Id 需处理关联数据），
            // 两者的业务策略远超"属性还原"的范畴，一期明确不支持
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreOnlyUpdated"],
                code: AbpAdminDomainErrorCodes.EntityRestore.OnlyUpdated);
        }

        var entityType = ResolveEntityType(change.EntityTypeFullName)
            ?? throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreEntityTypeNotResolvable", change.EntityTypeFullName],
                code: AbpAdminDomainErrorCodes.EntityRestore.EntityTypeNotResolvable);

        if (!typeof(IEntity<Guid>).IsAssignableFrom(entityType))
        {
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreOnlyGuidKeyed", change.EntityTypeFullName],
                code: AbpAdminDomainErrorCodes.EntityRestore.OnlyGuidKeyed);
        }

        if (!Guid.TryParse(change.EntityId, out var entityId))
        {
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreInvalidEntityId", change.EntityId],
                code: AbpAdminDomainErrorCodes.EntityRestore.InvalidEntityId);
        }

        var outcome = await ApplyRestoreAsync(entityType, entityId, change.PropertyChanges.ToList());

        return new EntityRestoreResultDto
        {
            EntityChangeId = change.Id,
            EntityTypeFullName = change.EntityTypeFullName,
            EntityId = change.EntityId,
            RestoredProperties = outcome.RestoredProperties,
            SkippedProperties = outcome.SkippedProperties
                .Select(x => new EntityRestoreSkippedPropertyDto { PropertyName = x.PropertyName, Reason = x.Reason })
                .ToList()
        };
    }

    /// <summary>
    /// 实体类型按 FullName 反解的缓存（只缓存命中；解析失败不缓存——避免动态加载程序集等
    /// 场景下把一次失败的解析固化到重启）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, Type> EntityTypeCache = new();

    private static Type? ResolveEntityType(string typeFullName)
    {
        if (EntityTypeCache.TryGetValue(typeFullName, out var cached))
        {
            return cached;
        }

        // 审计落库的类型全名可能被 AuditLogEntityTypeFullNameConverter 改写（泛型→C# 风格），
        // 常规聚合是非泛型类型，FullName 原样存储；这里按"先精确、再扫描"解析
        var resolved = Type.GetType(typeFullName, throwOnError: false, ignoreCase: false);
        if (resolved == null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                resolved = assembly.GetType(typeFullName, throwOnError: false, ignoreCase: false);
                if (resolved != null)
                {
                    break;
                }
            }
        }

        if (resolved != null)
        {
            EntityTypeCache[typeFullName] = resolved;
        }

        return resolved;
    }

    private sealed record RestoreOutcome(
        List<string> RestoredProperties,
        List<(string PropertyName, string Reason)> SkippedProperties);

    private async Task<RestoreOutcome> ApplyRestoreAsync(Type entityType, Guid entityId, List<EntityPropertyChange> propertyChanges)
    {
        // 反射构造 closed generic 的私有实现；TEntity 约束 IEntity<Guid> 已在入口校验。
        // 必须从声明类型取 MethodInfo：运行期 this 是 Castle 代理子类，private 方法不被继承，
        // GetType().GetMethod 会拿到 null（运行期 NRE，测试直连实现时暴露不出来）
        var applyMethod = typeof(AuditLogAppService)
            .GetMethod(nameof(ApplyRestoreCoreAsync), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ApplyRestoreCoreAsync reflection lookup failed");
        var closedMethod = applyMethod.MakeGenericMethod(entityType);
        var outcome = (Task<RestoreOutcome>)closedMethod.Invoke(this, [entityId, propertyChanges])!;
        return await outcome;
    }

    private async Task<RestoreOutcome> ApplyRestoreCoreAsync<TEntity>(Guid entityId, List<EntityPropertyChange> propertyChanges)
        where TEntity : class, IEntity<Guid>
    {
        IRepository<TEntity, Guid> repository;
        var entity = default(TEntity);
        try
        {
            // 类型通过反解门≠一定注册在当前 DbContext：第三方模块程序集里的同FullName类型会在这里
            // 以 AbpException 出局，转成业务错（400 语义）而非 500
            repository = LazyServiceProvider.LazyGetRequiredService<IRepository<TEntity, Guid>>();
            entity = await repository.FindAsync(entityId)
                ?? throw new UserFriendlyException(
                    L["AbpAdmin:EntityRestoreEntityNotFound", entityId],
                    code: AbpAdminDomainErrorCodes.EntityRestore.EntityNotFound);
        }
        // DataException 是 DbUpdateException 的基类——Application 层刻意不引 EF Core，
        // 用 provider 无关基类型接住存储层失败（唯一索引/并发冲突）
        catch (Exception ex) when (ex is not UserFriendlyException
            && (ex is AbpException || ex is System.Data.DataException))
        {
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreUpdateFailed", ex.Message],
                code: AbpAdminDomainErrorCodes.EntityRestore.UpdateFailed,
                innerException: ex);
        }

        var restored = new List<string>();
        var skipped = new List<(string PropertyName, string Reason)>();

        foreach (var propertyChange in propertyChanges)
        {
            // 并发戳由 EF 并发令牌机制管理，回滚它反而制造伪冲突
            if (propertyChange.PropertyName == nameof(IHasConcurrencyStamp.ConcurrencyStamp))
            {
                skipped.Add((propertyChange.PropertyName, L["AbpAdmin:EntityRestoreSkippedConcurrencyStamp"].Value));
                continue;
            }

            // 脱敏黑名单属性：掩码/被抹掉的凭据字段绝不允许回写（RedactedMarker 常量与写入侧脱敏同源）
            if (SensitiveEntityProperties.Names.Contains(propertyChange.PropertyName))
            {
                skipped.Add((propertyChange.PropertyName, L["AbpAdmin:EntityRestoreSkippedSensitive"].Value));
                continue;
            }

            // 实体类型上的属性才是权威（PropertyTypeFullName 可能被审计转换器改写）；
            // 沿继承链找，兼容 protected setter
            var property = FindProperty(typeof(TEntity), propertyChange.PropertyName);
            if (property == null)
            {
                skipped.Add((propertyChange.PropertyName, L["AbpAdmin:EntityRestoreSkippedPropertyMissing"].Value));
                continue;
            }

            if (!property.CanWrite)
            {
                skipped.Add((propertyChange.PropertyName, L["AbpAdmin:EntityRestoreSkippedPropertyNotWritable"].Value));
                continue;
            }

            // object/开放字典类属性：JSON 反序列化会产出 EF 无法映射的 JsonElement，
            // 直到 SaveChanges 才炸——提前跳过，不阻断整体
            if (IsObjectTypedProperty(property.PropertyType))
            {
                skipped.Add((propertyChange.PropertyName, L["AbpAdmin:EntityRestoreSkippedObjectValue"].Value));
                continue;
            }

            if (!TryConvertOriginalValue(propertyChange.OriginalValue, property.PropertyType, out var value, out var reason))
            {
                skipped.Add((propertyChange.PropertyName, reason!));
                continue;
            }

            property.SetValue(entity, value);
            restored.Add(propertyChange.PropertyName);
        }

        if (restored.Count == 0)
        {
            // 无任何属性被还原就不落库，避免制造一条"空变更"审计记录
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreNothingToRestore"],
                code: AbpAdminDomainErrorCodes.EntityRestore.NothingToRestore);
        }

        try
        {
            await repository.UpdateAsync(entity);
        }
        catch (System.Data.DataException ex)
        {
            // 唯一索引/并发冲突等存储层失败 → 业务错语义（整体 UoW 回滚，无半写）
            // （DataException 覆盖 DbUpdateException，保持 Application 层不依赖 EF Core）
            throw new UserFriendlyException(
                L["AbpAdmin:EntityRestoreUpdateFailed", ex.Message],
                code: AbpAdminDomainErrorCodes.EntityRestore.UpdateFailed,
                innerException: ex);
        }

        return new RestoreOutcome(restored, skipped);
    }

    /// <summary>object/开放字典类型属性判定：JSON 反序列化对它们产出 JsonElement，EF 无法映射。</summary>
    private static bool IsObjectTypedProperty(Type propertyType)
    {
        if (propertyType == typeof(object))
        {
            return true;
        }

        if (!propertyType.IsGenericType)
        {
            return false;
        }

        var genericDef = propertyType.GetGenericTypeDefinition();
        var args = propertyType.GetGenericArguments();
        return (genericDef == typeof(Dictionary<,>) || genericDef == typeof(KeyValuePair<,>))
            && args.Any(a => a == typeof(object));
    }

    private static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// 把落库的 JSON 字符串还原为属性值。ABP EntityHistoryHelper 用 IJsonSerializer 序列化属性值，
    /// 故首选 System.Text.Json 直接反序列化；失败（历史手工数据/被截断的字符串）再按
    /// InvariantCulture 显式转换兜底。截断值（MaxValueLength 之后带省略标记）无法完整还原，
    /// 会以转换失败被跳过并说明，不会静默写入残缺数据。
    /// 调用方已先行跳过敏感属性；此处再兜一道掩码值判断（防未来黑名单漏项时掩码经裸值路径回流）。
    /// </summary>
    private bool TryConvertOriginalValue(string? storedValue, Type targetType, out object? value, out string? reason)
    {
        value = null;
        reason = null;

        if (storedValue is null || storedValue == "null")
        {
            // 落库 null：引用类型/可空值类型恢复为 null；非可空值类型无法表达
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
            {
                reason = L["AbpAdmin:EntityRestoreNullToNonNullable", targetType.Name].Value;
            }
            return reason is null;
        }

        // 掩码兜底：脱敏后的值已是固定标记而非真实数据，任何属性还原它都是破坏
        if (storedValue == SensitiveEntityProperties.RedactedMarker)
        {
            reason = L["AbpAdmin:EntityRestoreSkippedRedacted"].Value;
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize(storedValue, targetType);
            return true;
        }
        catch (JsonException)
        {
            // 继续走显式转换兜底
        }
        catch (NotSupportedException)
        {
            // 继续走显式转换兜底
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (underlyingType.IsEnum)
            {
                value = Enum.Parse(underlyingType, storedValue, ignoreCase: true);
                return true;
            }

            if (underlyingType == typeof(Guid))
            {
                value = Guid.Parse(storedValue);
                return true;
            }

            if (underlyingType == typeof(string))
            {
                // 字符串兜底：接受未带 JSON 引号的裸值；被截断的 JSON 会落到这里并还原为残缺文本，
                // 这是唯一无法通过转换探测的损失场景（截断发生在落库侧，值已不可得）
                value = storedValue;
                return true;
            }

            value = Convert.ChangeType(storedValue, underlyingType, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            reason = L["AbpAdmin:EntityRestoreConvertFailed", targetType.Name].Value;
            return false;
        }
    }

    /// <summary>
    /// 平均执行时长统计。聚合在数据库侧完成（IRepository&lt;AuditLog, Guid&gt;.GetQueryableAsync + GroupBy）。
    /// </summary>
    public virtual async Task<List<AuditLogAverageDurationDto>> GetAverageExecutionDurationPerDayAsync(
        GetAuditLogStatisticsInput input)
    {
        // ExecutionTime 由 ABP IClock 写入（默认本地时区），统计窗口必须用同一时钟，
        // 用 DateTime.UtcNow 会在 UTC+X 部署上把最近一天的新日志截掉、日分桶错位。
        var startTime = input.StartTime ?? Clock.Now.AddDays(-7);
        var endTime = input.EndTime ?? Clock.Now;

        var repository = LazyServiceProvider.LazyGetRequiredService<IRepository<AuditLog, Guid>>();
        var queryable = await repository.GetQueryableAsync();

        // AsyncExecuter 走 ToListAsync，不在 async Action 里阻塞线程池线程（与 GetListAsync 风格一致）
        return await AsyncExecuter.ToListAsync(queryable
            .Where(x => x.ExecutionTime >= startTime && x.ExecutionTime <= endTime)
            .GroupBy(x => x.ExecutionTime.Date)
            .Select(g => new AuditLogAverageDurationDto
            {
                Date = g.Key,
                AvgExecutionDuration = g.Average(x => x.ExecutionDuration)
            })
            .OrderBy(x => x.Date));
    }

    /// <summary>
    /// 错误率统计。错误口径唯一出处：AuditLogQueryExtensions.IsErrorPredicate()
    /// （与「仅未处理错误」列表/导出共用）。总量/错误量按两次 GroupBy 查询后在内存合并——
    /// 换取谓词共享，避免同一段判断在两处维护漂移。
    /// </summary>
    public virtual async Task<AuditLogErrorRateDto> GetErrorRateAsync(
        GetAuditLogStatisticsInput input)
    {
        // 同上：与 ExecutionTime 的写入时钟保持一致（IClock，默认本地时区）
        var startTime = input.StartTime ?? Clock.Now.AddDays(-7);
        var endTime = input.EndTime ?? Clock.Now;

        var repository = LazyServiceProvider.LazyGetRequiredService<IRepository<AuditLog, Guid>>();
        var queryable = await repository.GetQueryableAsync();

        var baseQuery = queryable
            .Where(x => x.ExecutionTime >= startTime && x.ExecutionTime <= endTime);

        var totalPerDay = await AsyncExecuter.ToListAsync(
            baseQuery
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date));

        var errorPerDay = await AsyncExecuter.ToListAsync(
            baseQuery
                .Where(AuditLogQueryExtensions.IsErrorPredicate())
                .GroupBy(x => x.ExecutionTime.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderBy(x => x.Date));

        var errorCount = errorPerDay.Sum(x => (long)x.Count);
        var totalCount = totalPerDay.Sum(x => (long)x.Count);

        var dataPoints = totalPerDay
            .GroupBy(x => x.Date)
            .Select(g =>
            {
                var dayTotal = (long)g.Sum(x => x.Count);
                var dayError = (long)errorPerDay
                    .Where(x => x.Date == g.Key)
                    .Sum(x => x.Count);
                return new AuditLogErrorRateDataPoint
                {
                    Date = g.Key,
                    TotalCount = dayTotal,
                    ErrorCount = dayError,
                    ErrorRate = dayTotal == 0 ? 0 : Math.Round((double)dayError / dayTotal * 100, 2)
                };
            })
            .ToList();

        return new AuditLogErrorRateDto
        {
            TotalCount = totalCount,
            ErrorCount = errorCount,
            ErrorRate = totalCount == 0 ? 0 : Math.Round((double)errorCount / totalCount * 100, 2),
            DataPoints = dataPoints
        };
    }

    /// <summary>
    /// Sorting 是客户端自由文本且直接进 Dynamic LINQ OrderBy（未处理错误视图分支）：
    /// 白名单外输入（注入载荷或拼错字段）前置转成校验错（AbpValidationException → 400）。
    /// </summary>
    private void ValidateSorting(string? sorting)
    {
        if (SortingWhitelist.IsValid(sorting, SortableFields))
        {
            return;
        }

        throw new AbpValidationException(
            L["AbpAdmin:InvalidSorting", sorting ?? string.Empty]);
    }

    private static AuditLogDto Map(AuditLog log, bool includeDetails = false)
    {
        var dto = new AuditLogDto
        {
            Id = log.Id,
            ApplicationName = log.ApplicationName,
            UserName = log.UserName,
            ExecutionTime = log.ExecutionTime,
            ExecutionDuration = log.ExecutionDuration,
            ClientIpAddress = log.ClientIpAddress,
            ClientId = log.ClientId,
            HttpMethod = log.HttpMethod,
            Url = log.Url,
            Exceptions = log.Exceptions,
            HttpStatusCode = log.HttpStatusCode.HasValue ? (int)log.HttpStatusCode.Value : null,
            CorrelationId = log.CorrelationId
        };
        if (includeDetails)
        {
            dto.Actions = log.Actions.Select(x => new AuditLogActionDto
            {
                ServiceName = x.ServiceName,
                MethodName = x.MethodName,
                ExecutionDuration = x.ExecutionDuration
            }).ToList();
            dto.EntityChanges = log.EntityChanges.Select(x => new EntityChangeDto
            {
                EntityTypeFullName = x.EntityTypeFullName,
                EntityId = x.EntityId,
                ChangeType = (byte)x.ChangeType,
                PropertyChanges = x.PropertyChanges.Select(p => new EntityPropertyChangeDto
                {
                    PropertyName = p.PropertyName,
                    OriginalValue = p.OriginalValue,
                    NewValue = p.NewValue
                }).ToList()
            }).ToList();
        }

        return dto;
    }
}
