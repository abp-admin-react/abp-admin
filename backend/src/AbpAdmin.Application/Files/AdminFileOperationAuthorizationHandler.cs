using System.Threading.Tasks;
using EasyAbp.FileManagement.Containers;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Options.Containers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Files;

/* EasyAbp.FileManagement 的资源级授权由 FileOperationAuthorizationHandler 完成。
 * 模块自带的 BasicFileOperationAuthorizationHandler 在「功能权限」之外，
 * 还会对 Private 容器强制要求 resource.OwnerUserId == 当前用户Id
 * （SetFailIfUserIsNotPersonalContainerOwnerAsync：FileContainerType == Private(0)
 * 且 OwnerUserId 不匹配时显式 context.Fail()）。
 *
 * 本系统是管理后台：文件容器 admin 配置为 Private，但语义是"有权限的管理员都能管"，
 * 文件/目录不属于某个 owner（OwnerUserId 为 null）。Basic handler 的 owner 检查会把
 * 合法的管理员操作（GetList / Create 等）全部 403。
 *
 * 因此按官方文档 Usage 第 4 条自定义授权处理器：继承 Basic 复用其功能权限校验，
 * 仅 override 掉 owner 限制（设为空实现，不做 owner 检查）。
 *
 * 替换机制（关键）：EasyAbp 的 Basic handler 以 ISingletonDependency 注册，ABP 会把它
 * 暴露为「自身类型 BasicFileOperationAuthorizationHandler」+「IAuthorizationHandler 工厂
 * （内部解析自身类型）」。若直接移除 Basic 的自身类型注册，那个 IAuthorizationHandler 工厂
 * 会因解析不到自身类型而返回 null，导致整个授权数组解析失败（500）。
 *
 * 正确做法是用 ABP 的服务替换：ExposeServices(typeof(BasicFileOperationAuthorizationHandler))
 * 让本类顶替 Basic 的自身类型注册，ReplaceServices=true 覆盖之。因为本类继承自 Basic，
 * 可赋值给 Basic 类型，替换合法。这样 Basic 的 IAuthorizationHandler 工厂解析到的就是
 * 本实例（owner 检查已被 override 为空），授权正常。
 */
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(BasicFileOperationAuthorizationHandler))]
public class AdminFileOperationAuthorizationHandler : BasicFileOperationAuthorizationHandler
{
    private readonly FileStorageQuotaChecker _quotaChecker;

    public AdminFileOperationAuthorizationHandler(
        IFileContainerConfigurationProvider configurationProvider,
        IPermissionChecker permissionChecker,
        FileStorageQuotaChecker quotaChecker)
        : base(configurationProvider, permissionChecker)
    {
        _quotaChecker = quotaChecker;
    }

    /* 配额检查挂在这里（而非上传领域事件）的原因与取舍：
     * EasyAbp 的上传路径是模块内部的 FileManager，本系统没有自己的上传 AppService 包装可挂点；
     * 授权检查是所有创建路径（含将来的其他入口）的必经点，在这里拒绝能把「blob 已写、行未建」
     * 的孤儿中间态挡在最前面。代价是授权层承担了业务资源校验（耦合），
     * 且检查用的是 SUM + 进程锁（见 FileStorageQuotaChecker 的并发取舍注释）。
     * 中期方案：记账表原子化后，把检查挪到上传领域事件/独立域服务调用点。 */
    protected override async Task HandleCreateAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        FileCreationOperationInfoModel resource)
    {
        // 先走 base 的功能权限判定（File.Create），未 Fail（即有权创建）才做配额检查：
        // 配额检查是一次 SUM 聚合 + 进程锁，放在权限之前会让无权限请求也白付这笔开销
        //（代码审查 round1），且无权限时配额异常会盖掉 403 的语义。
        await base.HandleCreateAsync(context, requirement, resource);
        if (!context.HasFailed)
        {
            await _quotaChecker.CheckAsync(resource.ByteSize ?? 0);
        }
    }

    /* 管理后台语义：容器 Private 仅表示"不对外公开"，不代表"按 owner 隔离"。
     * 有功能权限（或 File.Manage）的管理员即可操作，不强制 OwnerUserId 匹配。
     * override 为空实现，跳过 Basic 的 owner 检查。 */
    protected override Task SetFailIfUserIsNotPersonalContainerOwnerAsync(
        IFileContainerConfiguration configuration,
        AuthorizationHandlerContext context,
        IFileOperationInfoModel resource)
    {
        return Task.CompletedTask;
    }
}
