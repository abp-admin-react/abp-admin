using System;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using AbpAdmin.Profile;
using Microsoft.Extensions.Options;
using Volo.Abp.BlobStoring;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.Uow;

namespace AbpAdmin.Gdpr.Providers;

/// <summary>
/// 头像 GDPR 数据贡献者：把头像二进制（base64 内嵌 JSON）导出进个人数据包。
/// 头像是个人数据（可识别个人的图像），此前导出与删户均未覆盖。
/// 无头像也回发 Prepared（hasAvatar=false）：贡献者计数稳定，
/// 且「其它 Provider 都就绪、唯独头像缺失」不会被误判为整体未就绪。
/// </summary>
public class ProfileAvatarGdprDataProvider :
    IDistributedEventHandler<GdprUserDataRequestedEto>,
    ITransientDependency
{
    public const string ProviderName = "ProfileAvatar";

    private readonly IBlobContainer<AvatarContainer> _avatarContainer;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly AbpAdminImagingOptions _options;

    public ProfileAvatarGdprDataProvider(
        IBlobContainer<AvatarContainer> avatarContainer,
        IIdentityUserRepository userRepository,
        IDistributedEventBus distributedEventBus,
        IJsonSerializer jsonSerializer,
        IOptions<AbpAdminImagingOptions> options)
    {
        _avatarContainer = avatarContainer;
        _userRepository = userRepository;
        _distributedEventBus = distributedEventBus;
        _jsonSerializer = jsonSerializer;
        _options = options.Value;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(GdprUserDataRequestedEto eventData)
    {
        var user = await _userRepository.FindAsync(eventData.UserId);
        if (user == null)
        {
            return;
        }

        // blob 扩展名以用户 ExtraProperties 里持久化的实际格式优先（上传时刻写入），
        // 当前白名单候选兜底（历史数据/白名单收窄场景）——探测读取统一走 AvatarBlobReader
        var storedExtension = user.GetProperty<string?>(AbpAdminConsts.AvatarBlobExtensionPropertyName, null);
        var found = await AvatarBlobReader.FindFirstAsync(_avatarContainer, _options, eventData.UserId, storedExtension);

        var payload = new
        {
            HasAvatar = found is not null,
            ContentType = found == null
                ? null
                : ImageMimeTypes.GetMimeType(found.Value.Extension),
            // 头像已压缩到 256px / 白名单格式，base64 内嵌体积可控
            Base64 = found == null ? null : Convert.ToBase64String(found.Value.Bytes)
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

