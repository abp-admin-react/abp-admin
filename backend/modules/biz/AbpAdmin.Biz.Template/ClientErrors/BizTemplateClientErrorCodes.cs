namespace AbpAdmin.Biz.Template.ClientErrors;

/// <summary>客户错误样例的内部/公开码。公开码进入客户响应；内部码只留服务端。</summary>
public static class BizTemplateClientErrorCodes
{
    public const string ProjectUnavailable = "BizTemplate:ProjectUnavailable";

    public const string ProjectUnavailablePublic = "BizTemplate:Public:ProjectUnavailable";
}
