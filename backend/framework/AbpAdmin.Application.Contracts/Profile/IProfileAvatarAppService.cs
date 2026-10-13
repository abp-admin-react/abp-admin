using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Profile;

/// <summary>
/// 当前用户头像（T3.1）。上传走完整链路：扩展名白名单 → 大小上限 → magic bytes →
/// 解码前像素上限 → 缩放（Crop 正方形）→ 压缩 → 写 BLOB。
/// </summary>
public interface IProfileAvatarAppService : IApplicationService
{
    /// <summary>上传并替换当前用户头像（multipart/form-data，字段名 file）。</summary>
    Task UploadAsync(UploadAvatarInput input);

    /// <summary>删除当前用户头像。</summary>
    Task DeleteAsync();

    /// <summary>
    /// 读取任意用户的头像（头像要在用户列表等场景展示，允许已登录用户互读）。
    /// id 会按当前租户过滤，防止跨租户探测；用户或头像不存在返回 null。
    /// </summary>
    Task<IRemoteStreamContent?> GetAsync(Guid id);

    /// <summary>
    /// 当前用户头像信息（含带版本参数的 URL）。头像 URL 由后端拼装下发，
    /// 前端不要自己拼路径（规格 T3.1 第 5 步）。
    /// </summary>
    Task<AvatarInfoDto> GetMyAvatarInfoAsync();
}

public class UploadAvatarInput
{
    public IRemoteStreamContent File { get; set; } = default!;
}

public class AvatarInfoDto
{
    /// <summary>形如 /api/app/profile-avatar/{userId}?v={version}；无头像时为 null。</summary>
    public string? AvatarUrl { get; set; }
}
