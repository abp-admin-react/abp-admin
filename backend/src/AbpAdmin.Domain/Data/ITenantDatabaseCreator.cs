using System.Threading.Tasks;

namespace AbpAdmin.Data;

/// <summary>
/// 确保「独立租户数据库」存在（只管建库，不管 schema——schema 由 IAbpAdminDbSchemaMigrator 负责）。
/// 实现在 EntityFrameworkCore 层（需要具体 provider 的驱动），按 Database:Provider 分派；
/// 租户库与 host 同 DBMS（连接串管理界面的语义约定）。
/// </summary>
public interface ITenantDatabaseCreator
{
    /// <summary>库已存在时无副作用；不存在时创建空库（schema 由后续迁移填充）。</summary>
    Task CreateIfNotExistsAsync(string connectionString);
}
