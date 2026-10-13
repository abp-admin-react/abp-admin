using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.DataScopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 业务模块自带 DbContext：与宿主 AbpAdminDbContext 同库（Default 连接串）但互不相干。
/// 建表走本模块自己的 EF Core 迁移（Migrations/）和 __BizTemplate_EFMigrationsHistory，
/// 不产生框架仓库改动。
///
/// 数据权限：本模块实现 <see cref="IHasDataScope"/> 的实体（如 BizDataScopeDemo）在此
/// 接入框架同款的数据范围全局筛选器与写入侧自动填充——筛选器覆写是 DbContext 级的，
/// 框架 AbpAdminDbContext 的覆写管不到本上下文，模块必须自带一份（自包含样板的取舍：
/// 复制模块即带走数据权限能力，不依赖框架仓库改动）。语义与框架实现逐条对齐：
/// null OU 只对 All 可见（fail-closed）、SelfOnly 与机构范围「或」、显式赋值不覆盖、
/// 算不出组织抛业务异常。
/// </summary>
[ConnectionStringName(BizTemplateConsts.ConnectionStringName)]
public class BizTemplateDbContext : AbpDbContext<BizTemplateDbContext>
{
    public DbSet<BizProject> BizProjects { get; set; }

    public DbSet<BizDataScopeDemo> BizDataScopeDemos { get; set; }

    public BizTemplateDbContext(DbContextOptions<BizTemplateDbContext> options)
        : base(options)
    {
    }

    #region 数据范围筛选器属性（与框架 AbpAdminDbContext 同构）

    protected ICurrentDataScopeState DataScopeState
        => LazyServiceProvider.LazyGetRequiredService<ICurrentDataScopeState>();

    protected virtual bool IsDataScopeFilterEnabled => DataFilter?.IsEnabled<IDataScopeEnabled>() ?? false;
    protected virtual bool IsDataScopeAll => DataScopeState.IsAll;
    protected virtual bool IsDataScopeSelfOnly => DataScopeState.SelfOnly;
    protected virtual IReadOnlyCollection<Guid> CurrentDataScopeOuIds => DataScopeState.OrganizationUnitIds;
    protected virtual Guid? CurrentDataScopeUserId => DataScopeState.UserId;

    #endregion

    protected override bool ShouldFilterEntity<TEntity>(IMutableEntityType entityType)
    {
        if (typeof(IHasDataScope).IsAssignableFrom(typeof(TEntity)))
        {
            return true;
        }

        return base.ShouldFilterEntity<TEntity>(entityType);
    }

    protected override Expression<Func<TEntity, bool>>? CreateFilterExpression<TEntity>(
        ModelBuilder modelBuilder,
        EntityTypeBuilder<TEntity> entityTypeBuilder)
    {
        var expression = base.CreateFilterExpression<TEntity>(modelBuilder, entityTypeBuilder);

        if (typeof(IHasDataScope).IsAssignableFrom(typeof(TEntity)))
        {
            var ouPropertyName = entityTypeBuilder.Metadata
                .FindProperty(nameof(IHasDataScope.OrganizationUnitId))?.Name
                ?? nameof(IHasDataScope.OrganizationUnitId);

            // 过滤谓词与 DataScopeTypeEnum 的映射（读侧语义全景，框架 AbpAdminDbContext 同构）：
            //   !IsDataScopeFilterEnabled        → IDataFilter.Disable<IDataScopeEnabled>（种子/后台作业/DbMigrator 显式旁路）
            //   IsDataScopeAll                   → All=0
            //   CurrentDataScopeOuIds.Contains   → CurrentOuAndChildren=1 / CurrentOu=2 / Custom=3
            //                                       （三种类型在解析链里已折叠为「可见组织集合」，过滤器无感知）
            //   IsDataScopeSelfOnly && CreatorId == UserId → SelfOnly=4（与机构范围「或」）
            // 短路顺序即放行优先级：过滤器关 > All > 命中组织 > 本人创建；全不命中即不可见
            //（fail-closed：无角色配置/空组织集合的登录态看到零行，而不是全部）。
            // OrganizationUnitId 为 null 的行只有 IsAll 分支放行（Contains 不命中 null）——同样 fail-closed。
            Expression<Func<TEntity, bool>> scopeFilter = e =>
                !IsDataScopeFilterEnabled
                || IsDataScopeAll
                || CurrentDataScopeOuIds.Contains(EF.Property<Guid>(e, ouPropertyName))
                || (IsDataScopeSelfOnly && EF.Property<Guid?>(e, nameof(Volo.Abp.Auditing.IMayHaveCreator.CreatorId)) == CurrentDataScopeUserId);

            expression = expression == null
                ? scopeFilter
                : QueryFilterExpressionHelper.CombineExpressions(expression, scopeFilter);
        }

        return expression;
    }

    /// <summary>
    /// 写入侧自动填充 <see cref="IHasDataScope.OrganizationUnitId"/>（与框架实现同款规则）：
    /// 显式赋值不覆盖；<see cref="IDataFilter{T}"/> 被 Disable 时允许 null（种子/后台作业）；
    /// 从快照取第一个可见组织；All / SelfOnly 允许 null；算不出组织抛业务异常，不静默写 null。
    /// </summary>
    protected override void ApplyAbpConceptsForAddedEntity(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        base.ApplyAbpConceptsForAddedEntity(entry);

        if (entry.Entity is not IHasDataScope dataScopeEntity)
        {
            return;
        }

        // 已显式赋值时尊重调用方，不覆盖
        if (dataScopeEntity.OrganizationUnitId != null)
        {
            return;
        }

        // 过滤器被 Disable 时允许 null（种子、后台作业、DbMigrator）
        if (!IsDataScopeFilterEnabled)
        {
            return;
        }

        // 从当前数据范围快照取第一个可见组织
        var ouIds = DataScopeState.OrganizationUnitIds;
        if (ouIds.Count > 0)
        {
            entry.Property(nameof(IHasDataScope.OrganizationUnitId)).CurrentValue = ouIds.First();
            return;
        }

        // IsAll 时允许 null：All 范围能看到所有数据（包括 null OU 的），写入 null 合理
        if (DataScopeState.IsAll)
        {
            return;
        }

        // SelfOnly 时允许 null：SelfOnly 的过滤条件是 CreatorId == UserId，不是 OU
        if (DataScopeState.SelfOnly)
        {
            return;
        }

        // 算不出组织 → 抛异常，不要静默写 null（错误码沿用框架定义，语义同一）
        throw new Volo.Abp.BusinessException(AbpAdminDomainErrorCodes.DataScopes.CannotResolveOrganizationUnit)
            .WithData("EntityType", entry.Entity.GetType().Name);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new BizProjectConfig());
        builder.ApplyConfiguration(new BizDataScopeDemoConfig());
    }
}
