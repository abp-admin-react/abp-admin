using System;
using System.Collections.Generic;
using EasyAbp.NotificationService.Notifications;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信通知创建 ETO（T3.5），形状对齐模块自带的邮件创建 ETO。
/// 业务代码不要直接构造它——走 INotificationDispatcher（ETO 构造函数签名随模块版本漂移）。
/// </summary>
[Serializable]
public class CreateInAppNotificationEto : CreateNotificationInfoModel, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string? Title
    {
        get => this.GetProperty<string?>(InAppNotificationConsts.TitlePropertyName);
        set => this.SetProperty(InAppNotificationConsts.TitlePropertyName, value);
    }

    public string? Body
    {
        get => this.GetProperty<string?>(InAppNotificationConsts.BodyPropertyName);
        set => this.SetProperty(InAppNotificationConsts.BodyPropertyName, value);
    }

    public CreateInAppNotificationEto()
    {
    }

    public CreateInAppNotificationEto(Guid? tenantId, IEnumerable<NotificationUserInfoModel> users, string title, string body)
        : base(InAppNotificationConsts.NotificationMethod, users)
    {
        TenantId = tenantId;
        Title = title;
        Body = body;
    }

    public CreateInAppNotificationEto(Guid? tenantId, IEnumerable<Guid> userIds, string title, string body)
        : base(InAppNotificationConsts.NotificationMethod, userIds)
    {
        TenantId = tenantId;
        Title = title;
        Body = body;
    }

    public CreateInAppNotificationEto(Guid? tenantId, NotificationUserInfoModel user, string title, string body)
        : base(InAppNotificationConsts.NotificationMethod, user)
    {
        TenantId = tenantId;
        Title = title;
        Body = body;
    }

    public CreateInAppNotificationEto(Guid? tenantId, Guid userId, string title, string body)
        : base(InAppNotificationConsts.NotificationMethod, userId)
    {
        TenantId = tenantId;
        Title = title;
        Body = body;
    }
}
