using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IAbpApplicationManager _applicationManager;

    public OpenIddictDataSeedContributor(
        IConfiguration configuration,
        IOpenIddictApplicationRepository openIddictApplicationRepository,
        IAbpApplicationManager applicationManager,
        IOpenIddictScopeRepository openIddictScopeRepository,
        IOpenIddictScopeManager scopeManager)
        : base(configuration, openIddictApplicationRepository, applicationManager, openIddictScopeRepository, scopeManager)
    {
        // 基类持有的 ApplicationManager 不对外暴露，这里自留一份句柄做种子后的
        // PKCE requirement / 回调白名单补挂（见 EnsurePkceRequirementAsync / EnsureRedirectUrisAsync）。
        _applicationManager = applicationManager;
    }

    /// <summary>
    /// 覆写官方同签名方法（已核对 rel-10.6 源码：基类对已存在应用无条件 UpdateAsync，每种子轮
    /// 翻并发戳、产生写放大）。已存在 → 与配置逐域比对（类型/consent/显示名/回调/注销回调/
    /// 授权类型/scope），全等则整只跳过；有差异或新应用 → 走基类原逻辑。
    /// 已知取舍：ClientSecret 轮换检测不到（OpenIddict 存哈希，无法与明文比对）——轮换 secret
    /// 请删应用行后重跑种子。EnsureRedirectUrisAsync 的回调整表覆盖仍保留（双保险，比对域一致）。
    /// </summary>
    protected override async Task CreateOrUpdateApplicationAsync(
        string applicationType,
        string name,
        string type,
        string consentType,
        string displayName,
        string? secret,
        List<string> grantTypes,
        List<string> scopes,
        List<string>? redirectUris = null,
        List<string>? postLogoutRedirectUris = null,
        string? clientUri = null,
        string? logoUri = null)
    {
        var application = await _applicationManager.FindByClientIdAsync(name);
        if (application is null)
        {
            await base.CreateOrUpdateApplicationAsync(applicationType, name, type, consentType, displayName,
                secret, grantTypes, scopes, redirectUris, postLogoutRedirectUris, clientUri, logoUri);
            return;
        }

        var same =
            (await _applicationManager.GetClientTypeAsync(application)) == type
            && (await _applicationManager.GetConsentTypeAsync(application)) == consentType
            && (await _applicationManager.GetDisplayNameAsync(application)) == displayName
            && (await _applicationManager.GetClientUriAsync(application)) == clientUri
            && (await _applicationManager.GetLogoUriAsync(application)) == logoUri
            && SameSet((await _applicationManager.GetPermissionsAsync(application)).Where(p => p.StartsWith("gt:", StringComparison.Ordinal)), grantTypes.Select(g => "gt:" + g))
            && SameSet((await _applicationManager.GetPermissionsAsync(application)).Where(p => p.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.Ordinal)), scopes.Select(s2 => OpenIddictConstants.Permissions.Prefixes.Scope + s2))
            && SameUriSet(await _applicationManager.GetRedirectUrisAsync(application), redirectUris ?? [])
            && SameUriSet(await _applicationManager.GetPostLogoutRedirectUrisAsync(application), postLogoutRedirectUris ?? []);
        if (same)
        {
            return;
        }

        await base.CreateOrUpdateApplicationAsync(applicationType, name, type, consentType, displayName,
            secret, grantTypes, scopes, redirectUris, postLogoutRedirectUris, clientUri, logoUri);
    }

    private static bool SameSet(IEnumerable<string> current, IEnumerable<string> target) =>
        new HashSet<string>(current).SetEquals(target);

    private static bool SameUriSet(IEnumerable<string> current, IEnumerable<string?> target) =>
        new HashSet<Uri?>(current.Select(u => ParseUri(u))).SetEquals(target.Select(u => ParseUri(u!)));

    /// <summary>宽容解析配置里的 URI：非法/相对返回 null（调用方跳过该条并留日志）。
    /// IsFile 且原串未显式写 file: 视为非法——Unix 上 Uri.TryCreate("/x", Absolute) 会按
    /// 绝对路径解析成 file:///x 返回 true，不拦会把相对 RootUrl 种进库。</summary>
    private static Uri? ParseUri(string raw) =>
        Uri.TryCreate((raw ?? "").Trim(), UriKind.Absolute, out var uri)
        && (!uri.IsFile || (raw ?? "").Trim().StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            ? uri
            : null;

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
    /// 配置了 ClientId 却漏了 RootUrl 时，后续 redirect URI 拼接会产生空引用——
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

    /// <summary>
    /// 幂等补挂 PKCE requirement：应用已带则跳过；否则 Populate 现值到 descriptor，
    /// 追加 requirement 后整只更新（保留既有 permissions/settings/凭据）。
    /// 仅用于公共客户端；swagger-ui 等不发 code_challenge 的工具不要调用。
    /// </summary>
    private async Task EnsurePkceRequirementAsync(string clientId)
    {
        var application = await _applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            return;
        }

        var requirements = await _applicationManager.GetRequirementsAsync(application);
        if (requirements.Contains(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange))
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);
        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        await _applicationManager.UpdateAsync(application, descriptor);
    }

    /// <summary>
    /// 幂等同步回调白名单：以配置拼出的 redirect/post-logout 列表整表覆盖库内值。
    /// 端口/源只来自 OpenIddict:Applications:AbpAdmin_App:RootUrl + Extra*（Host Configuration），
    /// 本方法不硬编码任何端口。CreateOrUpdate 对已有客户端的 URI 集合更新在部分路径上不可靠，
    /// 这里用 Populate + 清空 + 再写入，保证 compose 改 WEB_PUBLIC_URL 后重跑种子立即生效。
    /// 库内值与配置完全一致时跳过本方法的整只更新——消掉种子路径上的重复二次写——
    /// 否则每次种子都无条件重写）。整只级别的零写放大由本类覆写的 CreateOrUpdateApplicationAsync
    /// 保证（对已存在应用逐域比对，全等直接跳过基类的无条件 UpdateAsync）；本方法只在其
    /// 判定出回调集合有差异时才会被走到，是差异路径里的二次兜底。
    /// </summary>
    private async Task EnsureRedirectUrisAsync(
        string clientId,
        IReadOnlyList<string> redirectUris,
        IReadOnlyList<string> postLogoutRedirectUris)
    {
        var application = await _applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
        {
            return;
        }

        // 解析与写入走同一套校验，先比对后写：库内已是配置白名单 → 原样返回
        var redirectTargets = ParseUris(clientId, redirectUris, "redirectUri");
        var postLogoutTargets = ParseUris(clientId, postLogoutRedirectUris, "postLogoutRedirectUri");

        var descriptor = new OpenIddictApplicationDescriptor();
        await _applicationManager.PopulateAsync(descriptor, application);

        if (SameUris(descriptor.RedirectUris, redirectTargets) && SameUris(descriptor.PostLogoutRedirectUris, postLogoutTargets))
        {
            return;
        }

        descriptor.RedirectUris.Clear();
        foreach (var uri in redirectTargets)
        {
            descriptor.RedirectUris.Add(uri);
        }

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var uri in postLogoutTargets)
        {
            descriptor.PostLogoutRedirectUris.Add(uri);
        }

        await _applicationManager.UpdateAsync(application, descriptor);
    }

    private static List<Uri> ParseUris(string clientId, IReadOnlyList<string> raws, string field)
    {
        var result = new List<Uri>();
        foreach (var raw in raws)
        {
            if (raw.IsNullOrWhiteSpace())
            {
                continue;
            }

            if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString())
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.SeedRootUrlMissing)
                    .WithData("client", clientId)
                    .WithData(field, raw);
            }

            result.Add(uri);
        }

        return result;
    }

    /// <summary>回调集合按集合等价比较（顺序无关、忽略重复项）。</summary>
    private static bool SameUris(IEnumerable<Uri> current, IReadOnlyList<Uri> target) =>
        new HashSet<Uri>(current).SetEquals(target);

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
                $"{appClientRootUrl}/",
                // SPA（oidc-client-ts）的 post_logout_redirect_uri = {origin}/user/login
                //（与 redirect_uri 的 /user/callback 对称）。缺了它 end-session 在 Validate
                // 阶段即被拒（invalid post_logout_redirect_uri→400），服务端会话吊销处理器
                // 根本不执行，SPA 只是本地清 token——UI 登出路径的会话吊销被静默短路。
                $"{appClientRootUrl}/user/login"
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
                    // ROPC（grant_type=password）已从 SPA 客户端移除：OAuth 2.0 Security BCP 禁用，
                    // OAuth 2.1 移除；SPA 实际走授权码+PKCE。移除后 Turnstile 保护的登录页
                    // 不再能被令牌端点旁路。脚本化/E2E 测试通道走下方 AbpAdmin_TestCli（机密客户端）。
                    // 无密码登录与模拟登录扩展授权；LinkAccounts: 关联账号切换
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

            // 公共客户端的授权码通道强制 PKCE（前端 oidc-client-ts 的 code flow
            // 默认发出 code_challenge，服务端强制不改变现有登录行为，只封住「无
            // code_challenge 兑换授权码」的劫持面；种子后 AbpAdmin_App 的
            // requirePkce=true 已落库，authorize 无 challenge → 400）。ABP 10.6.1 的种子
            // 基类没有 requirements 参数，种入后在这里幂等补挂（已带该 requirement 则
            // 跳过，不产生写放大）。
            await EnsurePkceRequirementAsync(appClientId!);
            // 回调白名单以 Host Configuration（RootUrl + Extra*）为准整表覆盖，端口不写死在种子里。
            await EnsureRedirectUrisAsync(appClientId!, redirectUris, postLogoutRedirectUris);
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
