namespace AbpAdmin.TokenGateway;

public enum UpstreamMode { OneApi, Mock }

public enum PaymentMode { Stub, Live }

public class TokenGatewayOptions
{
    public const string Section = "TokenGateway";

    public PaymentOptions Payment { get; set; } = new();
    public UpstreamOptions Upstream { get; set; } = new();
    public List<AliasOption> Aliases { get; set; } = new();
    public string DefaultAlias { get; set; } = "hb-chat";
    /// <summary>新用户首次登录赠送试用代币；产品未定，默认 0（不送）。</summary>
    public long TrialTokens { get; set; }
    public int SessionHours { get; set; } = 24 * 7;

    internal IReadOnlyList<AliasOption> EffectiveAliases => Aliases.Count > 0 ? Aliases : DefaultAliases;

    internal static readonly AliasOption[] DefaultAliases =
    {
        new() { Alias = "hb-chat", Model = "CHANGE_ME", PromptRate = 1, CompletionRate = 1, DefaultMaxTokens = 4096 },
    };
}

public class AliasOption
{
    public string Alias { get; set; } = "";
    /// <summary>one-api 侧模型名；仅内部使用，绝不回给客户端。</summary>
    public string Model { get; set; } = "";
    public decimal PromptRate { get; set; } = 1;
    public decimal CompletionRate { get; set; } = 1;
    public int DefaultMaxTokens { get; set; } = 4096;
}

public class PaymentOptions
{
    public PaymentMode Mode { get; set; } = PaymentMode.Stub;
    public int OrderTtlSeconds { get; set; } = 300;
    public string WechatMchId { get; set; } = "CHANGE_ME";
    public string WechatApiV3Key { get; set; } = "CHANGE_ME";
    public string AlipayAppId { get; set; } = "CHANGE_ME";
    public string AlipayPrivateKey { get; set; } = "CHANGE_ME";
}

public class UpstreamOptions
{
    public UpstreamMode Mode { get; set; } = UpstreamMode.OneApi;
    /// <summary>one-api 内网地址，如 http://one-api:3000 。空 = 未接，对话返回 UPSTREAM_UNAVAILABLE。</summary>
    public string BaseUrl { get; set; } = "";
    public string InternalToken { get; set; } = "CHANGE_ME";
    public int TimeoutSeconds { get; set; } = 120;
}
