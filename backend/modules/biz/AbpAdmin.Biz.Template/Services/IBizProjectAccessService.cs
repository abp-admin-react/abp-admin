using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 管理入口与客户入口共用的最小读取/状态检查。
/// 不关闭数据过滤器；权限由各入口自己执行。
/// </summary>
public interface IBizProjectAccessService
{
    /// <summary>管理端读取：记录不存在则 <c>EntityNotFoundException</c>。</summary>
    Task<BizProject> GetRequiredAsync(Guid id);

    /// <summary>
    /// 客户读取：缺失、非本人或停用一律抛出同一内部码，不区分原因。
    /// </summary>
    Task<BizProject> GetOwnedActiveAsync(Guid id, Guid ownerUserId);

    /// <summary>客户列表：仅 <c>CreatorId == ownerUserId</c>。</summary>
    Task<List<BizProject>> GetOwnedListAsync(Guid ownerUserId);
}
