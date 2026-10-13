using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Notifications;

/// <summary>
/// 用户侧的"我的通知"（T3.5 第 11 步）。
/// 不能用模块的 INotificationAppService：它的 GetListPolicyName 是 Manage，
/// 而 Manage 意味着能看所有人的通知（3.9.0 反编译核实）。
/// 本服务只标 [Authorize]（任意已登录用户），查询强制 CurrentUser.GetId()，
/// 输入 DTO 刻意没有 UserId 字段——从根上消除"忘了过滤"和"参数被篡改"两种风险。
/// </summary>
public interface IMyNotificationAppService : IApplicationService
{
    Task<PagedResultDto<MyNotificationDto>> GetListAsync(GetMyNotificationsInput input);

    Task<UnreadCountDto> GetUnreadCountAsync();

    /// <summary>全部标记已读（时间戳方案：记录"上次全部已读时间"，不支持单条已读——规格第 7 步的取舍）。</summary>
    Task MarkAllAsReadAsync();
}

public class GetMyNotificationsInput : PagedResultRequestDto
{
}

[Serializable]
public class MyNotificationDto
{
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>CreationTime &lt;= 上次"全部已读"时间戳。</summary>
    public bool IsRead { get; set; }
}

[Serializable]
public class UnreadCountDto
{
    public int Count { get; set; }
}
