using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace AbpAdmin.PersonalData;

// 对齐 Pro 的授权模型：个人数据是每个用户对自己的数据的能力，不是可授予的权限，只要求已认证
[Authorize]
public class PersonalDataAppService : AbpAdminAppService, IPersonalDataAppService
{
    public virtual Task<PersonalDataDto> GetAsync()
    {
        return Task.FromResult(new PersonalDataDto
        {
            UserName = CurrentUser.UserName,
            Email = CurrentUser.Email,
            Name = CurrentUser.Name,
            Surname = CurrentUser.SurName,
            PhoneNumber = CurrentUser.PhoneNumber,
            Roles = CurrentUser.Roles ?? [],
            TenantName = CurrentTenant.Name
        });
    }
}
