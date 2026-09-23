using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using AbpAdmin.Profile;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.Gdpr.Handlers;

/// <summary>
/// 订阅 <see cref="GdprUserDataDeletionRequestedEto"/>，删除 Identity 用户。
///
/// 匿名化与删除的先后顺序：先匿名化再删除。
/// 这是为了让删除操作本身产生的审计日志里不再含真实个人信息——
/// 如果先删除，审计日志会记录真实的 UserName / Email，违背 GDPR 删除意图。
///
/// 用户删除成功后，末尾清理该用户的 GdprRequest/GdprInfo 与头像 blob（保持"账户在则数据在"：
/// 删除半途失败时请求记录不会先行消失；本地总线下本 handler 内联执行，
/// 与发布方同一事务，任何一步抛异常整体回滚）。
/// </summary>
public class IdentityUserDeletionHandler :
    IDistributedEventHandler<GdprUserDataDeletionRequestedEto>,
    ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IRepository<GdprRequest, Guid> _gdprRequestRepository;
    private readonly IRepository<GdprInfo, Guid> _gdprInfoRepository;
    private readonly IBlobContainer<AvatarContainer> _avatarContainer;
    private readonly AbpAdminImagingOptions _imagingOptions;

    public IdentityUserDeletionHandler(
        IdentityUserManager userManager,
        IRepository<GdprRequest, Guid> gdprRequestRepository,
        IRepository<GdprInfo, Guid> gdprInfoRepository,
        IBlobContainer<AvatarContainer> avatarContainer,
        IOptions<AbpAdminImagingOptions> imagingOptions)
    {
        _userManager = userManager;
        _gdprRequestRepository = gdprRequestRepository;
        _gdprInfoRepository = gdprInfoRepository;
        _avatarContainer = avatarContainer;
        _imagingOptions = imagingOptions.Value;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(GdprUserDataDeletionRequestedEto eventData)
    {
        var user = await _userManager.FindByIdAsync(eventData.UserId.ToString());
        if (user == null)
        {
            return;
        }

        // 第一步：匿名化个人字段（通过 UserManager 保证规范化字段同步更新）
        var anonymized = $"deleted-{user.Id:N}";
        CheckIdentityResult(await _userManager.SetUserNameAsync(user, anonymized));
        CheckIdentityResult(await _userManager.SetEmailAsync(user, $"{anonymized}@invalid.local"));
        user.Name = null;
        user.Surname = null;
        CheckIdentityResult(await _userManager.SetPhoneNumberAsync(user, null));
        user.SetIsActive(false);

        CheckIdentityResult(await _userManager.UpdateAsync(user));

        // 第二步：删除 identity user 记录
        CheckIdentityResult(await _userManager.DeleteAsync(user));

        // 第三步：清理该用户的 GDPR 请求记录与各模块贡献的 payload（数据最小化）
        var requests = await _gdprRequestRepository.GetListAsync(x => x.UserId == eventData.UserId);
        foreach (var request in requests)
        {
            await _gdprInfoRepository.DeleteAsync(x => x.RequestId == request.Id);
        }

        await _gdprRequestRepository.DeleteManyAsync(requests);

        // 第四步（最后）：清理头像 blob——头像是可识别个人的图像，属个人数据。
        // 放在所有 DB 操作之后：blob 存储不参与数据库事务，若先行删除而后续 DB 步骤
        // 回滚，会出现"用户仍在、头像已丢"的不可逆损伤；放最后则 DB 回滚时 blob 未动。
        // blob 名按上传时的实际格式落盘（{userId:N}{ext}，持久化在用户 ExtraProperties），
        // 存储值优先精确删除，当前白名单候选兜底（历史格式/白名单收窄场景）
        var storedExtension = user.GetProperty<string?>(AbpAdminConsts.AvatarBlobExtensionPropertyName, null);
        var extensionsToDelete = new List<string>();
        if (!storedExtension.IsNullOrWhiteSpace())
        {
            extensionsToDelete.Add(storedExtension);
        }
        extensionsToDelete.AddRange(AvatarBlobNames.CandidateExtensions(_imagingOptions)
            .Where(e => e != storedExtension));

        foreach (var extension in extensionsToDelete)
        {
            await _avatarContainer.DeleteAsync(AvatarBlobNames.ForUser(eventData.UserId, extension));
        }
    }

    private static void CheckIdentityResult(Microsoft.AspNetCore.Identity.IdentityResult result)
    {
        if (!result.Succeeded)
        {
            // 原始 Identity 错误进 WithData（日志可见），给用户的消息走本地化错误码，
            // 不再把框架英文描述直接透给终端用户
            throw new BusinessException(AbpAdminDomainErrorCodes.Gdpr.UserDeletionFailed)
                .WithData("Errors", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
