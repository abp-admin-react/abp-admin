namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// EF Core 迁移按提供程序分程序集托管（每库一个基线迁移）：
/// <see cref="Sqlite"/> 与 <see cref="PostgreSql"/> 各自持有独立的 Migrations + Model Snapshot，
/// 运行时按 Database:Provider 切换 MigrationsAssembly，互不干扰。
/// 程序集名同时是 csproj 文件名，改名须两处同步。
/// </summary>
public static class AbpAdminMigrationsAssemblies
{
    public const string Sqlite = "AbpAdmin.EntityFrameworkCore.Sqlite";
    public const string PostgreSql = "AbpAdmin.EntityFrameworkCore.PostgreSql";
}
