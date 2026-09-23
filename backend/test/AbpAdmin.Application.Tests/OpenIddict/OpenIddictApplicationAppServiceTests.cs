using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenIddict.Abstractions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Xunit;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// T2.9 OpenIddict Pro 缺口。断言要点：
/// 权限不再只增不减（更新时 Clear 重建）、写死的权限已移除、Hybrid/强制 PAR 两条派生规则、
/// client secret 只写语义五条规则、内置 scope 名拒绝、token lifetime 读写（Settings 字段）。
/// </summary>
public abstract class OpenIddictApplicationAppServiceTests<TStartupModule>
    : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOpenIddictApplicationAppService _applicationAppService;
    private readonly IOpenIddictScopeAppService _scopeAppService;
    private readonly IOpenIddictApplicationRepository _applicationRepository;
    private readonly IOpenIddictScopeRepository _scopeRepository;

    protected OpenIddictApplicationAppServiceTests()
    {
        _applicationAppService = GetRequiredService<IOpenIddictApplicationAppService>();
        _scopeAppService = GetRequiredService<IOpenIddictScopeAppService>();
        _applicationRepository = GetRequiredService<IOpenIddictApplicationRepository>();
        _scopeRepository = GetRequiredService<IOpenIddictScopeRepository>();
    }

    private static string NewClientId() => "t29-" + Guid.NewGuid().ToString("N")[..12];

    private static CreateOpenIddictApplicationDto NewInput(string clientId)
    {
        return new CreateOpenIddictApplicationDto
        {
            ClientId = clientId,
            DisplayName = clientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ApplicationType = OpenIddictConstants.ApplicationTypes.Web,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit
        };
    }

    private async Task<OpenIddictApplication> GetEntityAsync(Guid id)
    {
        return await WithUnitOfWorkAsync(() => _applicationRepository.GetAsync(id));
    }

    private static List<string> PermissionsOf(OpenIddictApplication entity)
    {
        return JsonSerializer.Deserialize<List<string>>(entity.Permissions!) ?? new List<string>();
    }

    private static List<string> RequirementsOf(OpenIddictApplication entity)
    {
        return JsonSerializer.Deserialize<List<string>>(entity.Requirements!) ?? new List<string>();
    }

    [Fact]
    public async Task Should_Not_Add_Unselected_Flow_Permissions()
    {
        // 验收：只勾选 Client Credentials flow 的应用，Permissions 不含
        // Authorization Code / Implicit / Refresh Token 等未勾选项（写死权限已移除）
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "secret-123";
        input.AllowClientCredentialsFlow = true;

        var created = await _applicationAppService.CreateAsync(input);

        var permissions = PermissionsOf(await GetEntityAsync(created.Id));
        permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        permissions.ShouldContain(OpenIddictConstants.Permissions.Endpoints.Token);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.Implicit);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.Password);
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.ResponseTypes.Code);
        created.AllowClientCredentialsFlow.ShouldBeTrue();
        created.AllowAuthorizationCodeFlow.ShouldBeFalse();
        created.AllowRefreshTokenFlow.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Remove_Permission_When_Flow_Unchecked()
    {
        // 验收：取消勾选 Refresh Token 保存后该项确实消失（权限不再只增不减）
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "secret-123";
        input.AllowClientCredentialsFlow = true;
        input.AllowRefreshTokenFlow = true;
        var created = await _applicationAppService.CreateAsync(input);
        PermissionsOf(await GetEntityAsync(created.Id))
            .ShouldContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);

        var update = new UpdateOpenIddictApplicationDto
        {
            ClientId = input.ClientId,
            DisplayName = input.DisplayName,
            ClientType = input.ClientType,
            ApplicationType = input.ApplicationType,
            ConsentType = input.ConsentType,
            AllowClientCredentialsFlow = true,
            AllowRefreshTokenFlow = false
        };
        var updated = await _applicationAppService.UpdateAsync(created.Id, update);

        var permissions = PermissionsOf(await GetEntityAsync(updated.Id));
        permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        updated.AllowRefreshTokenFlow.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Derive_AuthorizationCode_And_Implicit_When_Hybrid_Enabled()
    {
        // 验收：勾选 Hybrid 保存后 Authorization Code 与 Implicit 被自动启用
        var input = NewInput(NewClientId());
        input.AllowHybridFlow = true;
        input.RedirectUris = "https://example.com/callback";

        var created = await _applicationAppService.CreateAsync(input);

        var permissions = PermissionsOf(await GetEntityAsync(created.Id));
        permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.Implicit);
        permissions.ShouldContain(OpenIddictConstants.Permissions.ResponseTypes.CodeIdToken);
        permissions.ShouldContain(OpenIddictConstants.Permissions.Endpoints.Authorization);
        permissions.ShouldContain(OpenIddictConstants.Permissions.Endpoints.Token);
        created.AllowHybridFlow.ShouldBeTrue();
        created.AllowAuthorizationCodeFlow.ShouldBeTrue();
        created.AllowImplicitFlow.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Enable_PushedAuthorization_Endpoint_When_Required()
    {
        // 验收：勾选「强制 PAR」保存后 Pushed Authorization 端点被自动启用
        var input = NewInput(NewClientId());
        input.AllowAuthorizationCodeFlow = true;
        input.RequirePushedAuthorization = true;
        input.RedirectUris = "https://example.com/callback";

        var created = await _applicationAppService.CreateAsync(input);

        var entity = await GetEntityAsync(created.Id);
        PermissionsOf(entity).ShouldContain(OpenIddictConstants.Permissions.Endpoints.PushedAuthorization);
        RequirementsOf(entity).ShouldContain(
            OpenIddictConstants.Requirements.Features.PushedAuthorizationRequests);
        created.EnablePushedAuthorizationEndpoint.ShouldBeTrue();
        created.RequirePushedAuthorization.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Reject_Relative_Redirect_Uri_With_BusinessException()
    {
        // 验收：相对路径 redirect URI 返回中文业务异常而不是 500
        var input = NewInput(NewClientId());
        input.AllowAuthorizationCodeFlow = true;
        input.RedirectUris = "/callback";

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _applicationAppService.CreateAsync(input));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.InvalidAbsoluteUri);
    }

    [Fact]
    public async Task Should_Hash_Secret_And_Keep_When_Left_Empty()
    {
        // 规则 1/2：落库的是哈希不是明文；更新时留空（null）保持原值
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "plain-secret-1";
        input.AllowClientCredentialsFlow = true;
        var created = await _applicationAppService.CreateAsync(input);

        var before = (await GetEntityAsync(created.Id)).ClientSecret;
        before.ShouldNotBeNull();
        before.ShouldNotBe("plain-secret-1");

        var update = new UpdateOpenIddictApplicationDto
        {
            ClientId = input.ClientId,
            DisplayName = "renamed",
            ClientType = input.ClientType,
            ApplicationType = input.ApplicationType,
            ConsentType = input.ConsentType,
            ClientSecret = null, // 留空保持
            AllowClientCredentialsFlow = true
        };
        await _applicationAppService.UpdateAsync(created.Id, update);

        (await GetEntityAsync(created.Id)).ClientSecret.ShouldBe(before);
    }

    [Fact]
    public async Task Should_Reject_Public_Client_With_Credentials()
    {
        // 规则 5：Public 客户端不能持有 secret
        var input = NewInput(NewClientId());
        input.ClientSecret = "not-allowed";

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _applicationAppService.CreateAsync(input));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.PublicClientCannotHaveCredentials);
    }

    [Fact]
    public async Task Should_Reject_Confidential_Without_Any_Credential()
    {
        // 规则 5：Confidential 客户端至少要有一种凭据
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.AllowClientCredentialsFlow = true;

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _applicationAppService.CreateAsync(input));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.ConfidentialClientRequiresCredential);
    }

    [Fact]
    public async Task Should_Remove_Credentials_When_Switching_To_Public()
    {
        // 规则 3：Confidential 切成 Public，其 secret 与 JWKS 被移除
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "plain-secret-1";
        input.AllowClientCredentialsFlow = true;
        var created = await _applicationAppService.CreateAsync(input);
        (await GetEntityAsync(created.Id)).ClientSecret.ShouldNotBeNull();

        var update = new UpdateOpenIddictApplicationDto
        {
            ClientId = input.ClientId,
            DisplayName = input.DisplayName,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ApplicationType = input.ApplicationType,
            ConsentType = input.ConsentType,
            AllowClientCredentialsFlow = true
        };
        await _applicationAppService.UpdateAsync(created.Id, update);

        var entity = await GetEntityAsync(created.Id);
        entity.ClientType.ShouldBe(OpenIddictConstants.ClientTypes.Public);
        entity.ClientSecret.ShouldBeNull();
        entity.JsonWebKeySet.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Reject_Clearing_Last_Credential()
    {
        // 规则 4（对称）：只有 secret 的 Confidential 应用显式清空 secret（空字符串）被拒
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "plain-secret-1";
        input.AllowClientCredentialsFlow = true;
        var created = await _applicationAppService.CreateAsync(input);

        var update = new UpdateOpenIddictApplicationDto
        {
            ClientId = input.ClientId,
            DisplayName = input.DisplayName,
            ClientType = input.ClientType,
            ApplicationType = input.ApplicationType,
            ConsentType = input.ConsentType,
            ClientSecret = string.Empty, // 显式清空，最后一种凭据 → 拒绝
            AllowClientCredentialsFlow = true
        };
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _applicationAppService.UpdateAsync(created.Id, update));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.CannotRemoveLastCredential);

        // secret 仍在
        (await GetEntityAsync(created.Id)).ClientSecret.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Write_And_Read_Token_Lifetimes()
    {
        // token lifetime 读写：存 Settings（tkn_lft:* 键），单位秒，留空=移除覆盖
        var input = NewInput(NewClientId());
        input.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        input.ClientSecret = "plain-secret-1";
        input.AllowClientCredentialsFlow = true;
        var created = await _applicationAppService.CreateAsync(input);

        // 初始无任何覆盖
        var initial = await _applicationAppService.GetTokenLifetimeAsync(created.Id);
        initial.AccessTokenLifetime.ShouldBeNull();
        initial.RefreshTokenLifetime.ShouldBeNull();

        var saved = await _applicationAppService.UpdateTokenLifetimeAsync(created.Id,
            new UpdateOpenIddictApplicationTokenLifetimeDto
            {
                AccessTokenLifetime = 300,
                RefreshTokenLifetime = 3600
            });
        saved.AccessTokenLifetime.ShouldBe(300);
        saved.RefreshTokenLifetime.ShouldBe(3600);

        // 读回 + 直接验证 Settings 里确实是 tkn_lft:* 键（不是 Properties）
        var read = await _applicationAppService.GetTokenLifetimeAsync(created.Id);
        read.AccessTokenLifetime.ShouldBe(300);
        read.RefreshTokenLifetime.ShouldBe(3600);
        read.IdentityTokenLifetime.ShouldBeNull();

        var entity = await GetEntityAsync(created.Id);
        entity.Settings.ShouldContain("tkn_lft:act");
        entity.Settings.ShouldContain("tkn_lft:reft");
        (entity.Properties ?? string.Empty).ShouldNotContain("tkn_lft");

        // 留空=移除该项覆盖，其它项保留
        var cleared = await _applicationAppService.UpdateTokenLifetimeAsync(created.Id,
            new UpdateOpenIddictApplicationTokenLifetimeDto
            {
                AccessTokenLifetime = null,
                RefreshTokenLifetime = 3600
            });
        cleared.AccessTokenLifetime.ShouldBeNull();
        cleared.RefreshTokenLifetime.ShouldBe(3600);
        (await GetEntityAsync(created.Id)).Settings.ShouldNotContain("tkn_lft:act");
    }

    [Fact]
    public async Task GetAll_Should_Include_Managed_And_BuiltIn_Scopes()
    {
        // 验收：5 个内置 scope 的 IsBuiltIn 为 true 且不在 OpenIddictScopes 表中
        var managedName = "t29-scope-" + Guid.NewGuid().ToString("N")[..8];
        await _scopeAppService.CreateAsync(new CreateOpenIddictScopeDto { Name = managedName });

        var all = await _scopeAppService.GetAllAsync();

        all.Items.ShouldContain(x => x.Name == managedName && !x.IsBuiltIn);
        foreach (var name in new[] { "address", "email", "phone", "profile", "roles" })
        {
            all.Items.ShouldContain(x => x.Name == name && x.IsBuiltIn);
            // 内置 scope 不是数据库记录
            (await WithUnitOfWorkAsync(() => _scopeRepository.FindByNameAsync(name))).ShouldBeNull();
        }
    }

    [Fact]
    public async Task Should_Reject_Creating_Scope_With_BuiltIn_Name()
    {
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _scopeAppService.CreateAsync(new CreateOpenIddictScopeDto { Name = "email" }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.BuiltInScopeName);
    }

    [Fact]
    public async Task Should_Reject_Renaming_Scope_To_BuiltIn_Name()
    {
        var created = await _scopeAppService.CreateAsync(
            new CreateOpenIddictScopeDto { Name = "t29-scope-" + Guid.NewGuid().ToString("N")[..8] });

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _scopeAppService.UpdateAsync(created.Id, new UpdateOpenIddictScopeDto
            {
                Name = "profile",
                DisplayName = created.DisplayName
            }));
        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.OpenIddict.BuiltInScopeName);
    }

    [Fact]
    public async Task Should_Update_And_Delete_Scope()
    {
        // manager 泛型实参是 OpenIddictScopeModel 而非 EF 实体，
        // Update/Delete 必须经 FindByIdAsync 拿 Model（防 InvalidCastException 回归）
        var created = await _scopeAppService.CreateAsync(
            new CreateOpenIddictScopeDto { Name = "t29-scope-" + Guid.NewGuid().ToString("N")[..8] });

        var updated = await _scopeAppService.UpdateAsync(created.Id, new UpdateOpenIddictScopeDto
        {
            Name = created.Name!,
            DisplayName = "renamed",
            Description = "desc",
            Resources = "res1\nres2"
        });
        updated.DisplayName.ShouldBe("renamed");

        var entity = await WithUnitOfWorkAsync(() => _scopeRepository.GetAsync(created.Id));
        entity.DisplayName.ShouldBe("renamed");

        await _scopeAppService.DeleteAsync(created.Id);
        (await WithUnitOfWorkAsync(() => _scopeRepository.FindByNameAsync(created.Name!))).ShouldBeNull();
    }

    [Fact]
    public async Task Should_Delete_Application()
    {
        // manager 泛型实参是 OpenIddictApplicationModel 而非 EF 实体（防 InvalidCastException 回归）
        var input = NewInput(NewClientId());
        var created = await _applicationAppService.CreateAsync(input);

        await _applicationAppService.DeleteAsync(created.Id);

        (await WithUnitOfWorkAsync(() => _applicationRepository.FindByClientIdAsync(input.ClientId)))
            .ShouldBeNull();
    }
}
