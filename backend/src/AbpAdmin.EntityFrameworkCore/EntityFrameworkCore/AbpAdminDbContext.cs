using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using AbpAdmin.DataDictionaries;
using AbpAdmin.DataScopes;
using AbpAdmin.Editions;
using AbpAdmin.Gdpr;
using AbpAdmin.Localization;
using AbpAdmin.EntityFrameworkCore.Configs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using EasyAbp.Abp.DataDictionary.EntityFrameworkCore;
using EasyAbp.FileManagement.EntityFrameworkCore;
using EasyAbp.NotificationService.EntityFrameworkCore;
using EasyAbp.PaymentService.EntityFrameworkCore;
using EasyAbp.PaymentService.Prepayment.EntityFrameworkCore;
using EasyAbp.PaymentService.WeChatPay.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class AbpAdminDbContext :
    AbpDbContext<AbpAdminDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */


    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // 用户导入失败报告 / 异步导出文件记录（Identity 模块自有表，见 UserExcelFile）
    public DbSet<AbpAdmin.Identity.UserExcelFile> UserExcelFiles { get; set; }

    public DbSet<AbpAdmin.Editions.Edition> Editions { get; set; }

    public DbSet<AbpAdmin.TextTemplates.TextTemplateContent> TextTemplateContents { get; set; }

    public DbSet<Language> Languages { get; set; }
    public DbSet<LanguageText> LanguageTexts { get; set; }

    public DbSet<RoleDataScope> RoleDataScopes { get; set; }
    public DbSet<RoleDataScopeOrganizationUnit> RoleDataScopeOrganizationUnits { get; set; }
    public DbSet<DataScopeDemo> DataScopeDemos { get; set; }

    public DbSet<GdprRequest> GdprRequests { get; set; }
    public DbSet<GdprInfo> GdprInfos { get; set; }

    public DbSet<AbpAdmin.Files.FileThumbnail> FileThumbnails { get; set; }

    public DbSet<AbpAdmin.ScheduledJobs.ScheduledJob> ScheduledJobs { get; set; }
    public DbSet<AbpAdmin.ScheduledJobs.ScheduledJobExecution> ScheduledJobExecutions { get; set; }

    public DbSet<DataDictionaryItemMeta> DataDictionaryItemMetas { get; set; }

    public DbSet<AbpAdmin.Notifications.NotificationBroadcast> NotificationBroadcasts { get; set; }

    public DbSet<AbpAdmin.Files.FileShareLink> FileShareLinks { get; set; }

    public DbSet<AbpAdmin.Menus.Menu> Menus { get; set; }

    public DbSet<AbpAdmin.Menus.MenuGrant> MenuGrants { get; set; }

    // 岗位（对标 RuoYi sys_post / sys_user_post）
    public DbSet<AbpAdmin.Posts.Post> Posts { get; set; }

    public DbSet<AbpAdmin.Posts.IdentityUserPost> IdentityUserPosts { get; set; }

    // 语义化操作日志（对标 RuoYi system_operate_log）
    public DbSet<AbpAdmin.OperationLogs.OperationLog> OperationLogs { get; set; }

    // 审计日志「已处理」标记不建侧表：经 AbpAdminEfCoreEntityExtensionMappings 映射为
    // AbpAuditLogs 的真实列（官方 Entity Extensions 机制，同 Tenant 三列先例），
    // 「仅未处理」筛选因此是单表条件，不再有跨上下文组合与孤儿清理问题。

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    public DbSet<AbpAdmin.Tenants.TenantPackage> TenantPackages { get; set; }

    public DbSet<AbpAdmin.Tenants.TenantPackageMenu> TenantPackageMenus { get; set; }

    #endregion

    #region 数据范围筛选器属性

    protected ICurrentDataScopeState DataScopeState
        => LazyServiceProvider.LazyGetRequiredService<ICurrentDataScopeState>();

    protected virtual bool IsDataScopeFilterEnabled => DataFilter?.IsEnabled<IDataScopeEnabled>() ?? false;
    protected virtual bool IsDataScopeAll => DataScopeState.IsAll;
    protected virtual bool IsDataScopeSelfOnly => DataScopeState.SelfOnly;
    protected virtual IReadOnlyCollection<Guid> CurrentDataScopeOuIds => DataScopeState.OrganizationUnitIds;
    protected virtual Guid? CurrentDataScopeUserId => DataScopeState.UserId;

    #endregion

    public AbpAdminDbContext(DbContextOptions<AbpAdminDbContext> options)
        : base(options)
    {

    }

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
    /// 写入侧自动填充 <see cref="IHasDataScope.OrganizationUnitId"/>。
    /// 过滤列与填充列必须是同一列；填不出值时抛 <see cref="Volo.Abp.BusinessException"/>，
    /// 但 <see cref="IDataFilter{T}"/> 被 Disable 时除外（种子、后台作业、DbMigrator 没有登录用户）。
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

        // 算不出组织 → 抛异常，不要静默写 null
        throw new Volo.Abp.BusinessException(AbpAdminDomainErrorCodes.DataScopes.CannotResolveOrganizationUnit)
            .WithData("EntityType", entry.Entity.GetType().Name);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();
        builder.ConfigureFileManagement();
        // T3.5：模块表 EasyAbpNotificationServiceNotifications / EasyAbpNotificationServiceNotificationInfos。
        // 模块自己的 NotificationServiceDbContext 运行时走同名连接串回落到 Default，与本上下文共表。
        builder.ConfigureNotificationService();
        builder.ConfigurePaymentService();
        builder.ConfigurePaymentServicePrepayment();
        builder.ConfigurePaymentServiceWeChatPay();
        // T3.4：模块的 ConfigureDataDictionary 没有 optionsAction 参数（表名是模块既定的
        // EasyAbpAbpDataDictionaryDataDictionaries / EasyAbpAbpDataDictionaryDataDictionaryItems，不要改）。
        // 模块自己的 DataDictionaryDbContext 运行时走同名连接串回落到 Default，与本上下文共用这两张表。
        builder.ConfigureDataDictionary();

        // 问题6 修复：自研 20+ 个实体的内联配置拆分到 EntityFrameworkCore/Configs/ 的
        // IEntityTypeConfiguration<T>（纯结构性搬移，模型零差异，完成后以生成空迁移验证）。
        // 显式逐个 ApplyConfiguration 注册，不用 ApplyConfigurationsFromAssembly（避免意外扫描）。
        builder.ApplyConfiguration(new EditionConfig());
        builder.ApplyConfiguration(new TextTemplateContentConfig());
        builder.ApplyConfiguration(new LanguageConfig());
        builder.ApplyConfiguration(new LanguageTextConfig());
        builder.ApplyConfiguration(new RoleDataScopeConfig());
        builder.ApplyConfiguration(new RoleDataScopeOrganizationUnitConfig());
        builder.ApplyConfiguration(new DataScopeDemoConfig());
        builder.ApplyConfiguration(new GdprRequestConfig());
        builder.ApplyConfiguration(new GdprInfoConfig());
        builder.ApplyConfiguration(new FileThumbnailConfig());
        builder.ApplyConfiguration(new FileShareLinkConfig());
        builder.ApplyConfiguration(new FileManagementFileConfig());
        builder.ApplyConfiguration(new ScheduledJobConfig());
        builder.ApplyConfiguration(new ScheduledJobExecutionConfig());
        builder.ApplyConfiguration(new DataDictionaryItemMetaConfig());
        builder.ApplyConfiguration(new NotificationBroadcastConfig());
        builder.ApplyConfiguration(new MenuConfig());
        builder.ApplyConfiguration(new MenuGrantConfig());
        builder.ApplyConfiguration(new TenantPackageConfig());
        builder.ApplyConfiguration(new TenantPackageMenuConfig());
        builder.ApplyConfiguration(new PostConfig());
        builder.ApplyConfiguration(new IdentityUserPostConfig());
        builder.ApplyConfiguration(new UserExcelFileConfig());
        builder.ApplyConfiguration(new OperationLogConfig());

        /* Configure your own tables/entities inside here */

        //builder.Entity<YourEntity>(b =>
        //{
        //    b.ToTable(AbpAdminConsts.DbTablePrefix + "YourEntities", AbpAdminConsts.DbSchema);
        //    b.ConfigureByConvention(); //auto configure for the base class props
        //    //...
        //});

        // GUI 全链路测试 J8（并发双击/多端同投）发现的并发窗口：ABP 侧重靠
        // IdentityUserManager 校验重名，check-then-insert 在并发下双双通过，
        // 同租户同用户名可落库多条（SQLite 库实测 5 连发创建出 2-3 条）。
        // ABP 原生 UserNameIndex 是全局唯一、与「多租户同名 admin」设计冲突，
        // 这里补租户维度的复合唯一索引作为数据库侧最后防线；
        // Host 行（TenantId=NULL）在 SQLite 唯一索引里 NULL 互不相等、不受复合索引保护，
        // 由迁移里的表达式部分唯一索引（COALESCE）兜底。
        builder.Entity<Volo.Abp.Identity.IdentityUser>(b =>
        {
            b.HasIndex(nameof(Volo.Abp.Identity.IdentityUser.TenantId), nameof(Volo.Abp.Identity.IdentityUser.NormalizedUserName))
                .HasDatabaseName("UX_AbpUsers_TenantId_NormalizedUserName")
                .IsUnique();
        });
    }
}
