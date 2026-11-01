using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AbpAdmin.Account;

[Authorize]
public class AccountPasskeyAppService : AbpAdminAppService, IAccountPasskeyAppService
{
    private readonly IdentityUserManager _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public AccountPasskeyAppService(
        IdentityUserManager userManager,
        SignInManager<IdentityUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public virtual async Task<ListResultDto<UserPasskeyDto>> GetListAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var passkeys = await _userManager.GetPasskeysAsync(user);
        return new ListResultDto<UserPasskeyDto>(passkeys.Select(Map).ToList());
    }

    public virtual async Task<PasskeyJsonDto> GetCreationOptionsAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var json = await _signInManager.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.UserName ?? user.Email ?? user.Id.ToString(),
            DisplayName = user.Name.IsNullOrWhiteSpace() ? user.UserName ?? user.Id.ToString() : user.Name
        });

        return new PasskeyJsonDto { Json = json };
    }

    public virtual async Task RegisterAsync(PasskeyCredentialInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var result = await _signInManager.PerformPasskeyAttestationAsync(input.CredentialJson);
        if (!result.Succeeded)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.PasskeyAttestationFailed)
                .WithData("Reason", result.Failure?.ToString() ?? "attestation failed");
        }

        var passkey = result.Passkey;
        if (!input.Name.IsNullOrWhiteSpace())
        {
            passkey.Name = input.Name;
        }

        var add = await _userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!add.Succeeded)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.PasskeyAttestationFailed)
                .WithData("Reason", string.Join("; ", add.Errors.Select(e => e.Description)));
        }
    }

    public virtual async Task DeleteAsync(string credentialId)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var result = await _userManager.RemovePasskeyAsync(user, DecodeCredentialId(credentialId));
        if (!result.Succeeded)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.PasskeyRemoveFailed)
                .WithData("Reason", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [AllowAnonymous]
    public virtual async Task<PasskeyJsonDto> GetAssertionOptionsAsync()
    {
        var json = await _signInManager.MakePasskeyRequestOptionsAsync(user: null);
        return new PasskeyJsonDto { Json = json };
    }

    private static UserPasskeyDto Map(UserPasskeyInfo passkey)
    {
        return new UserPasskeyDto
        {
            CredentialId = EncodeCredentialId(passkey.CredentialId),
            Name = passkey.Name,
            CreatedAt = passkey.CreatedAt
        };
    }

    private static string EncodeCredentialId(byte[] credentialId)
    {
        return Convert.ToBase64String(credentialId)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// 删除凭据 Id 来自 URL。垃圾 / 截断的 base64url 直接当找不到，不把 FormatException 漏成 500。
    /// </summary>
    private static byte[] DecodeCredentialId(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId) || credentialId.Length > 512)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.PasskeyRemoveFailed);
        }

        var padded = credentialId.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.PasskeyRemoveFailed);
        }
    }
}
