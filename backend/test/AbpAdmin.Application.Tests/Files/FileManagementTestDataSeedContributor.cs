using System;
using System.Threading.Tasks;
using EasyAbp.FileManagement.Users;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace AbpAdmin.Files;

/* EasyAbp.FileManagement 的 FileAppService.GetListAsync 会按 CreatorId 回查用户:
 * FileUserLookupService.FindByIdAsync 先查本地 FileUser 表,
 * 命中且 SkipExternalLookupIfLocalUserExists=true 时直接返回,不再回查 Identity。
 * 测试环境的 FakeCurrentPrincipalAccessor 固定当前用户 Id 为
 * 2e701e62-0953-4dd3-910b-dc6cc93ccb0d(admin),但 FileUser 表默认没有该用户,
 * 会导致 FindByIdAsync 返回 null 进而 NullReferenceException。
 * 这里直接 seed 对应的 FileUser,使用户查找链路可用。
 *
 * 存在性检查必须关掉 IMultiTenant 过滤器：FileUser 主键就是用户 Id（全局唯一，不含租户），
 * host 已种过该行时，租户种子的查询被租户过滤器挡住会误判不存在 → 重复插入撞主键
 * （租户创建回归测试 TenantCreationSeedTests 暴露）。
 */
public class FileManagementTestDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public const string TestUserName = "admin";
    public const string TestUserEmail = "admin@abp.io";

    private static readonly Guid TestUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IFileUserRepository _fileUserRepository;
    private readonly IDataFilter _dataFilter;

    public FileManagementTestDataSeedContributor(
        IFileUserRepository fileUserRepository,
        IDataFilter dataFilter)
    {
        _fileUserRepository = fileUserRepository;
        _dataFilter = dataFilter;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            if (await _fileUserRepository.FindAsync(TestUserId) != null)
            {
                return;
            }
        }

        var userData = new UserData(
            TestUserId,
            TestUserName,
            email: TestUserEmail,
            name: TestUserName,
            surname: TestUserName,
            emailConfirmed: true,
            phoneNumber: null,
            phoneNumberConfirmed: false,
            tenantId: context.TenantId,
            isActive: true);

        await _fileUserRepository.InsertAsync(new FileUser(userData));
    }
}
