namespace AbpAdmin.OpenIddict;

/// <summary>
/// OpenIddict 模块的共享字面量常量。
/// Application.Contracts 层不能引用 OpenIddict.Abstractions（其 OpenIddictConstants 不适用），
/// DTO 默认值与白名单校验统一从这里取值，与 OpenIddict 官方常量的字面量保持一致。
/// </summary>
public static class AbpAdminOpenIddictDefaults
{
    public static class ClientTypes
    {
        public const string Public = "public";
        public const string Confidential = "confidential";
    }

    public static class ApplicationTypes
    {
        public const string Web = "web";
        public const string Native = "native";
    }

    public static class ConsentTypes
    {
        public const string Explicit = "explicit";
        public const string External = "external";
        public const string Implicit = "implicit";
        public const string Systematic = "systematic";
    }

    /// <summary>
    /// T2.7 扩展授权（grant_type 字面量，与 HttpApi.Host 的 Passwordless/Impersonation
    /// ExtensionGrant 实现及 Domain 种子共享；值即 OpenIddict Permissions.Prefixes.GrantType 后缀）。
    /// </summary>
    public static class GrantTypes
    {
        public const string Passwordless = "passwordless";
        public const string Impersonation = "impersonation";

        /// <summary>关联账号切换（LinkAccounts，v1 限同租户）。</summary>
        public const string LinkedAccount = "linked-account";
    }
}
