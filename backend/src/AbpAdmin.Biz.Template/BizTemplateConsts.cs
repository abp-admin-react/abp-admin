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
}

/// <summary>本模块双提供程序迁移程序集名（与框架 AbpAdminMigrationsAssemblies 同一约定）</summary>
public static class BizTemplateMigrationsAssemblies
{
    public const string PostgreSql = "AbpAdmin.Biz.Template.Migrations.PostgreSql";

    public const string Sqlite = "AbpAdmin.Biz.Template.Migrations.Sqlite";
}
