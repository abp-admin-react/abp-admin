using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.PersonalData;

public interface IPersonalDataAppService : IApplicationService
{
    Task<PersonalDataDto> GetAsync();
}

public class PersonalDataDto
{
    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? Name { get; set; }

    public string? Surname { get; set; }

    public string? PhoneNumber { get; set; }

    public string[] Roles { get; set; } = [];

    public string? TenantName { get; set; }
}
