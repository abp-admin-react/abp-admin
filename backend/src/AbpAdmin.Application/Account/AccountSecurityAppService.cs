using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace AbpAdmin.Account;

[Authorize]
public class AccountSecurityAppService : AbpAdminAppService, IAccountSecurityAppService
{
    private readonly IdentityUserManager _userManager;

    public AccountSecurityAppService(IdentityUserManager userManager)
    {
        _userManager = userManager;
    }

    public virtual async Task<List<UserLoginDto>> GetLoginsAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var logins = await _userManager.GetLoginsAsync(user);
        return logins.Select(x => new UserLoginDto
        {
            LoginProvider = x.LoginProvider,
            ProviderKey = x.ProviderKey,
            ProviderDisplayName = x.ProviderDisplayName
        }).ToList();
    }

    public virtual async Task RemoveLoginAsync(RemoveUserLoginInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var logins = await _userManager.GetLoginsAsync(user);
        if (logins.Count <= 1 && !await _userManager.HasPasswordAsync(user))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.CannotRemoveLastExternalLogin);
        }

        var result = await _userManager.RemoveLoginAsync(user, input.LoginProvider, input.ProviderKey);
        if (!result.Succeeded)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.RemoveExternalLoginFailed)
                .WithData("Errors", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    public virtual async Task<AuthenticatorStatusDto> GetAuthenticatorStatusAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        var providers = await _userManager.GetValidTwoFactorProvidersAsync(user);
        return new AuthenticatorStatusDto
        {
            HasAuthenticatorKey = !key.IsNullOrWhiteSpace(),
            Enabled = providers.Contains(TokenOptions.DefaultAuthenticatorProvider) && user.TwoFactorEnabled
        };
    }

    public virtual async Task<AuthenticatorKeyDto> ResetAuthenticatorKeyAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user)
                  ?? throw new BusinessException(AbpAdminDomainErrorCodes.Account.AuthenticatorKeyMissing);

        return new AuthenticatorKeyDto
        {
            SharedKey = FormatKey(key),
            AuthenticatorUri = BuildAuthenticatorUri(user, key)
        };
    }

    public virtual async Task<AuthenticatorRecoveryCodesDto> EnableAuthenticatorAsync(EnableAuthenticatorInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        if (!await VerifyAuthenticatorCodeAsync(user, input.Code))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidAuthenticatorCode);
        }

        var enable = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enable.Succeeded)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.SetTwoFactorEnabledFailed)
                .WithData("Errors", string.Join("; ", enable.Errors.Select(e => e.Description)));
        }

        // 恢复码数量集中定义见 AbpAdminAccountConsts（重构报告问题 22）
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, AbpAdminAccountConsts.RecoveryCodeCount);
        return new AuthenticatorRecoveryCodesDto
        {
            RecoveryCodes = codes?.ToList() ?? new List<string>()
        };
    }

    public virtual async Task DisableAuthenticatorAsync(DisableAuthenticatorInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        if (!await VerifyAuthenticatorCodeAsync(user, input.Code))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidAuthenticatorCode);
        }

        await _userManager.ResetAuthenticatorKeyAsync(user);
        await _userManager.SetTwoFactorEnabledAsync(user, false);
    }

    private async Task<bool> VerifyAuthenticatorCodeAsync(IdentityUser user, string code)
    {
        var normalized = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        try
        {
            return await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, normalized);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        var position = 0;
        while (position + 4 < key.Length)
        {
            builder.Append(key.AsSpan(position, 4)).Append(' ');
            position += 4;
        }

        builder.Append(key.AsSpan(position));
        return builder.ToString().ToLowerInvariant();
    }

    private string BuildAuthenticatorUri(IdentityUser user, string unformattedKey)
    {
        var issuer = "AbpAdmin";
        var email = user.Email ?? user.UserName;
        return string.Format(
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            HttpUtility.UrlEncode(issuer),
            HttpUtility.UrlEncode(email),
            unformattedKey);
    }
}
