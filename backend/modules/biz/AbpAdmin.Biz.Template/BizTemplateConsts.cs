namespace AbpAdmin.Biz.Template;

/// <summary>
/// 自包含业务模块样板常量。模块自带表前缀/连接串名，不引用框架 AbpAdminConsts——
/// 模块对框架层只依赖 AbpAdminEntityFrameworkCoreModule（EF 基建与
/// IAbpAdminDbSchemaMigrator 约定接口），其余自持，保证派生项目里业务可整体搬走。
/// </summary>
public class BizTemplateConsts
{
    /// <summary>业务模块统一走宿主 Default 库（同库多上下文，各自 History 表记账）</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>模块专属表前缀，与框架 "App" 前缀互不侵占</summary>
    public const string DbTablePrefix = "Biz";

    public const string? DbSchema = null;

    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 2048;

    /// <summary>本模块 EF Core 迁移记账表，与框架 __EFMigrationsHistory 分开（两本账）。</summary>
    public const string SchemaHistoryTable = "__BizTemplate_EFMigrationsHistory";

    /// <summary>
    /// 本模块首个迁移（存量库打戳 baseline 用，与 Migrations/ 下 Initial 保持一致；
    /// 漂移由 EfCoreLegacySchemaBaselinerDecideTests 工程测试钉死）。
    /// </summary>
    public const string InitialMigrationId = "20261007125818_Initial";
}
