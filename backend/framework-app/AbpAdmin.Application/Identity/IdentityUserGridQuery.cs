using System;
using System.Linq;
using AbpAdmin.Grids;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

public static class IdentityUserGridQuery
{
    /// <summary>列表和导出在 EF 的租户、软删除过滤器之上使用同一组业务条件和稳定排序。</summary>
    public static IOrderedQueryable<IdentityUser> Apply(IQueryable<IdentityUser> users, GridQueryInput input,
        bool canUpdateUsers, DateTimeOffset now)
    {
        // 同一次查询统一使用一个时刻，以免筛选和显示在锁定到期边界上产生分歧。
        var columns = new GridQueryDefinition<IdentityUser>()
            .Field("id", u => u.Id)
            .Field("userName", u => u.UserName)
            .Field("name", u => u.Name)
            .Field("surname", u => u.Surname)
            .Field("emailConfirmed", u => u.EmailConfirmed)
            .Field("isActive", u => u.IsActive)
            .Field("isLocked", u => u.LockoutEnd > now)
            .Field("status", u => !u.IsActive ? "inactive" : u.LockoutEnd > now ? "locked" : "active", "eq", "ne")
            .Field("accessFailedCount", u => u.AccessFailedCount)
            .Field("creationTime", u => u.CreationTime);
        if (canUpdateUsers)
        {
            // 读取明文及 2FA 状态均需 Users.Update。只读账号的条件和排序也遵循同一权限。
            columns.Field("email", u => u.Email)
                .Field("phoneNumber", u => u.PhoneNumber)
                .Field("twoFactorEnabled", u => u.TwoFactorEnabled);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var term = input.Keyword.Trim().ToLowerInvariant();
            users = users.Where(u => u.UserName.ToLower().Contains(term)
                || (u.Name != null && u.Name.ToLower().Contains(term))
                || (u.Surname != null && u.Surname.ToLower().Contains(term))
                || (canUpdateUsers && (u.Email.ToLower().Contains(term)
                    || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)))));
        }

        return columns.OrderBy(columns.Filter(users, input), input, "userName", false, u => u.Id);
    }
}
