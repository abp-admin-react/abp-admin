using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.Uow;

namespace AbpAdmin.Gdpr.Providers;

/// <summary>
/// Identity 模块 GDPR 数据贡献者。
/// 订阅 <see cref="GdprUserDataRequestedEto"/>，收集当前用户的档案字段、角色、组织单元、Claims，
/// 用相同的 RequestId 发布 <see cref="GdprUserDataPreparedEto"/>。
///
/// 新增 Provider 的 checklist：订阅 GdprUserDataRequestedEto → 收集数据（自限条数，
/// 参考 AuditLogging 的 100 条上限）→ 注入 IJsonSerializer 序列化（不要直用 System.Text.Json，
/// 保持全库序列化口径一致）→ 用相同 RequestId/UserId 回发 GdprUserDataPreparedEto，
/// ProviderName 即 ZIP 内文件名（{Provider}.json）。
/// </summary>
public class IdentityGdprDataProvider :
    IDistributedEventHandler<GdprUserDataRequestedEto>,
    ITransientDependency
{
    public const string ProviderName = "Identity";

    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IJsonSerializer _jsonSerializer;

    public IdentityGdprDataProvider(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IDistributedEventBus distributedEventBus,
        IJsonSerializer jsonSerializer)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _distributedEventBus = distributedEventBus;
        _jsonSerializer = jsonSerializer;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(GdprUserDataRequestedEto eventData)
    {
        var user = await _userRepository.FindAsync(eventData.UserId);
        if (user == null)
        {
            return;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var organizationUnits = await _userManager.GetOrganizationUnitsAsync(user);
        var claims = await _userManager.GetClaimsAsync(user);

        var payload = new
        {
            user.UserName,
            user.Email,
            user.Name,
            user.Surname,
            user.PhoneNumber,
            user.IsActive,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            user.CreationTime,
            Roles = roles,
            OrganizationUnits = organizationUnits.Select(ou => new { ou.Code, ou.DisplayName }),
            Claims = claims.Select(c => new { c.Type, c.Value })
        };

        await _distributedEventBus.PublishAsync(new GdprUserDataPreparedEto
        {
            TenantId = eventData.TenantId,
            RequestId = eventData.RequestId,
            UserId = eventData.UserId,
            Provider = ProviderName,
            Data = _jsonSerializer.Serialize(payload)
        });
    }
}
