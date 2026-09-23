using System.Threading.Tasks;

namespace AbpAdmin.Data;

public interface IAbpAdminDbSchemaMigrator
{
    Task MigrateAsync();
}
