namespace AbpAdmin.Webhooks;

/// <summary>模块内字符串长度上限（实体检查与 DbContext 列长同源）。</summary>
public static class WebhooksConsts
{
    public const int MaxWebhookUriLength = 512;
    public const int MaxSecretLength = 256;
    public const int MaxDescriptionLength = 256;
    public const int MaxEventNameLength = 128;
    public const int MaxPayloadLength = 65536;
}
