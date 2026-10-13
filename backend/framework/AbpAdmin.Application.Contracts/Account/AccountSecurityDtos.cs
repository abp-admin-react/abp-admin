using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Account;

public interface IAccountSecurityAppService : IApplicationService
{
    Task<List<UserLoginDto>> GetLoginsAsync();

    Task RemoveLoginAsync(RemoveUserLoginInput input);

    Task<AuthenticatorStatusDto> GetAuthenticatorStatusAsync();

    Task<AuthenticatorKeyDto> ResetAuthenticatorKeyAsync();

    Task<AuthenticatorRecoveryCodesDto> EnableAuthenticatorAsync(EnableAuthenticatorInput input);

    Task DisableAuthenticatorAsync(DisableAuthenticatorInput input);
}

public class UserLoginDto
{
    public string LoginProvider { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = string.Empty;

    public string? ProviderDisplayName { get; set; }
}

public class RemoveUserLoginInput
{
    [Required]
    public string LoginProvider { get; set; } = string.Empty;

    [Required]
    public string ProviderKey { get; set; } = string.Empty;
}

public class AuthenticatorStatusDto
{
    public bool Enabled { get; set; }

    public bool HasAuthenticatorKey { get; set; }
}

public class AuthenticatorKeyDto
{
    public string SharedKey { get; set; } = string.Empty;

    public string AuthenticatorUri { get; set; } = string.Empty;
}

public class EnableAuthenticatorInput
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public class DisableAuthenticatorInput
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

public class AuthenticatorRecoveryCodesDto
{
    public List<string> RecoveryCodes { get; set; } = new();
}

public interface IIdentityUserDelegationAppService : IApplicationService
{
    Task<List<IdentityUserDelegationDto>> GetDelegatedToOthersAsync();

    Task<List<IdentityUserDelegationDto>> GetDelegatedToMeAsync();

    Task<IdentityUserDelegationDto> DelegateAsync(CreateUserDelegationInput input);

    Task DeleteAsync(Guid id);

    Task<ImpersonationResultDto> StartAsync(Guid id);
}

public class IdentityUserDelegationDto : EntityDto<Guid>
{
    public Guid SourceUserId { get; set; }

    public Guid TargetUserId { get; set; }

    public string? SourceUserName { get; set; }

    public string? TargetUserName { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public bool IsActive { get; set; }
}

public class CreateUserDelegationInput
{
    [Required]
    public Guid TargetUserId { get; set; }

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }
}
