using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Account;

public interface IAccountPasskeyAppService : IApplicationService
{
    Task<ListResultDto<UserPasskeyDto>> GetListAsync();

    Task<PasskeyJsonDto> GetCreationOptionsAsync();

    Task RegisterAsync(PasskeyCredentialInput input);

    Task DeleteAsync(string credentialId);

    Task<PasskeyJsonDto> GetAssertionOptionsAsync();
}

public class UserPasskeyDto : EntityDto
{
    public string CredentialId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

public class PasskeyJsonDto
{
    public string Json { get; set; } = string.Empty;
}

public class PasskeyCredentialInput
{
    [Required]
    public string CredentialJson { get; set; } = string.Empty;

    public string? Name { get; set; }
}
