using System.Threading.Tasks;
using AbpAdmin.AuditLogs;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Domains;

/// <summary>
/// 「已处理」标记的列映射存在性断言（防回归）：HandledAt 等四列依赖
/// AbpAdminEfCoreEntityExtensionMappings 的 MapEfCoreProperty 注册；若注册丢失，
/// 模型里没有这些影子属性，EfCoreAuditLogHandledColumnQueries 的 EF.Property
/// 会在查询时抛 InvalidOperationException——与其等运行时炸，不如在模型层直接断言。
/// </summary>
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreAuditLogHandledColumnTests : AbpAdminEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task AuditLog_Model_Should_Have_Handled_Mapped_Columns()
    {
        var repository = GetRequiredService<IRepository<AuditLog>>();

        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await repository.GetDbContextAsync();
            var entityType = dbContext.Model.FindEntityType(typeof(AuditLog));
            entityType.ShouldNotBeNull();

            foreach (var propertyName in new[]
                     {
                         AuditLogHandleConsts.HandledAtPropertyName,
                         AuditLogHandleConsts.HandledByUserIdPropertyName,
                         AuditLogHandleConsts.HandledByNamePropertyName,
                         AuditLogHandleConsts.HandledNotePropertyName
                     })
            {
                entityType.FindProperty(propertyName).ShouldNotBeNull(
                    $"AuditLog 缺少扩展属性映射列 {propertyName}（检查 AbpAdminEfCoreEntityExtensionMappings.Configure）");
            }
        });
    }
}
