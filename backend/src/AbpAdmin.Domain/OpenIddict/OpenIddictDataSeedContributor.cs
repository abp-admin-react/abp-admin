using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.Applications;
using Volo.Abp.OpenIddict.Scopes;
using Volo.Abp.Uow;

namespace AbpAdmin.OpenIddict;

/* Creates initial data that is needed to property run the application
 * and make client-to-server communication possible.
 */
public class OpenIddictDataSeedContributor : OpenIddictDataSeedContributorBase, IDataSeedContributor, ITransientDependency
{
    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await CreateScopesAsync();
        await CreateApplicationsAsync();
    }

    private async Task CreateScopesAsync()
    {
        await CreateScopesAsync(new OpenIddictScopeDescriptor
        {
            Name = "AbpAdmin",
            DisplayName = "AbpAdmin API",
            Resources = { "AbpAdmin" }
        });
    }

    /// <summary>
    /// 配置了 ClientId 却漏配 RootUrl 时，后续 redirect URI 拼接会产生空引用——
    /// 该代码跑在 DbMigrator/Host 启动路径，NRE 会中断整个迁移，改为带明确配置指引的业务异常。
    /// </summary>
    private static string GetRequiredRootUrl(IConfiguration configuration, string clientSectionName)
    {
        var rootUrl = configuration[clientSectionName + ":RootUrl"]?.TrimEnd('/');
        if (rootUrl.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.SeedRootUrlMissing)
                .WithData("client", clientSectionName);
        }

        return rootUrl!;
    }

    private async Task CreateApplicationsAsync()
    {
        var commonScopes = new List<string> {
            OpenIddictConstants.Permissions.Scopes.Address,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Phone,
            OpenIddictConstants.Permissions.Scopes.Profile,
            OpenIddictConstants.Permissions.Scopes.Roles,
            "AbpAdmin"
        };

        var configurationSection = Configuration.GetSection("OpenIddict:Applications");


        // Console Test / Angular Client

        var appClientId = configurationSection["AbpAdmin_App:ClientId"];
        if (!appClientId.IsNullOrWhiteSpace())
        {
            var appClientRootUrl = GetRequiredRootUrl(configurationSection, "AbpAdmin_App");
            // 开发机回调地址从配置逐条读取（ExtraRedirectUris/ExtraPostLogoutRedirectUris，每项一条完整 URI）：
            // 生产环境不配置即不会被种入，避免开发 URI 混入认证回调白名单
            var redirectUris = new List<string>
            {
                appClientRootUrl,
                $"{appClientRootUrl}/callback",
                $"{appClientRootUrl}/user/callback",
                $"{appClientRootUrl}/silent-renew"
            };
            redirectUris.AddRange(configurationSection
                .GetSection("AbpAdmin_App:ExtraRedirectUris").Get<List<string>>() ?? new List<string>());

            var postLogoutRedirectUris = new List<string>
            {
                appClientRootUrl,
                $"{appClientRootUrl}/"
            };
            postLogoutRedirectUris.AddRange(configurationSection
                .GetSection("AbpAdmin_App:ExtraPostLogoutRedirectUris").Get<List<string>>() ?? new List<string>());

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: appClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Ant Design Pro SPA",
                secret: null,
                grantTypes: new List<string> {
                    OpenIddictConstants.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.GrantTypes.RefreshToken,
                    // ROPC（grant_type=password）已从 SPA 客户端移除：OAuth 2.0 Security BCP 禁用、
                    // OAuth 2.1 移除；SPA 实际走授权码+PKCE。移除后 Turnstile 保护的登录页
                    // 不再能被令牌端点旁路。脚本化/E2E 通道改走下方 AbpAdmin_TestCli（机密客户端）。
                    // T2.7: 无密码登录与模拟登录扩展授权；LinkAccounts: 关联账号切换
                    AbpAdminOpenIddictDefaults.GrantTypes.Passwordless,
                    AbpAdminOpenIddictDefaults.GrantTypes.Impersonation,
                    AbpAdminOpenIddictDefaults.GrantTypes.LinkedAccount
                },
                scopes: commonScopes,
                redirectUris: redirectUris,
                postLogoutRedirectUris: postLogoutRedirectUris,
                clientUri: appClientRootUrl,
                logoUri: "/images/clients/angular.svg"
            );
        }




        // Swagger Client
        var swaggerClientId = configurationSection["AbpAdmin_Swagger:ClientId"];
        if (!swaggerClientId.IsNullOrWhiteSpace())
        {
            var swaggerRootUrl = GetRequiredRootUrl(configurationSection, "AbpAdmin_Swagger");

            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Web,
                name: swaggerClientId!,
                type: OpenIddictConstants.ClientTypes.Public,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Swagger Application",
                secret: null,
                grantTypes: new List<string> { OpenIddictConstants.GrantTypes.AuthorizationCode, },
                scopes: commonScopes,
                redirectUris: new List<string> { $"{swaggerRootUrl}/swagger/oauth2-redirect.html" },
                clientUri: swaggerRootUrl.EnsureEndsWith('/') + "swagger",
                logoUri: "/images/clients/swagger.svg"
            );
        }

        // 脚本化/E2E 测试客户端（机密）：ROPC 从 SPA 客户端移除后，密码换令牌的唯一通道。
        // 必须配 ClientSecret（机密客户端），不配即不种入；生产环境可不配置该节直接关闭此通道。
        // 限流、账号锁定等令牌端点防护对本客户端同样生效。
        var testCliClientId = configurationSection["AbpAdmin_TestCli:ClientId"];
        var testCliSecret = configurationSection["AbpAdmin_TestCli:ClientSecret"];
        if (!testCliClientId.IsNullOrWhiteSpace() && !testCliSecret.IsNullOrWhiteSpace())
        {
            await CreateOrUpdateApplicationAsync(
                applicationType: OpenIddictConstants.ApplicationTypes.Native,
                name: testCliClientId!,
                type: OpenIddictConstants.ClientTypes.Confidential,
                consentType: OpenIddictConstants.ConsentTypes.Implicit,
                displayName: "Scripted Test Client (ROPC, confidential)",
                secret: testCliSecret,
                grantTypes: new List<string>
                {
                    OpenIddictConstants.GrantTypes.Password,
                    OpenIddictConstants.GrantTypes.RefreshToken
                },
                scopes: commonScopes,
                redirectUris: new List<string>(),
                postLogoutRedirectUris: new List<string>()
            );
        }


    }
}
