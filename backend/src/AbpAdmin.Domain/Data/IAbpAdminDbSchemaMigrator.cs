using System.Threading.Tasks;

namespace AbpAdmin.Data;

public interface IAbpAdminDbSchemaMigrator
{
    Task MigrateAsync();

    /// <summary>
    /// 本迁移器范围内是否还有未应用的脚本（History 记账比对）。
    /// 宿主启动的「schema 是否就绪」检查据此枚举全部实现逐个询问；
    /// History 表不可读（空库）应直接抛错，由调用方统一视为「需要迁移」。
    /// </summary>
    Task<bool> HasPendingAsync();
}
