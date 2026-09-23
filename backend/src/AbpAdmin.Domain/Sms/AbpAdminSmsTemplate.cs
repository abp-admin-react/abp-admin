using EasyAbp.Abp.Aliyun.Sms.Templates;

namespace AbpAdmin.Sms;

/// <summary>
/// ISmsTemplate 的可用实现（T3.5）。
/// EasyAbp 的 BaseSmsTemplate 三个属性只有 getter、构造函数不赋值，
/// 直接用会得到三个 null（1.21.0 反编译核实），所以自己实现接口。
/// 注意接口第三成员是 object TemplateContent（不是规格草图里的 string TemplateParam）。
/// </summary>
public class AbpAdminSmsTemplate : ISmsTemplate
{
    public string SignName { get; }

    public string TemplateCode { get; }

    public object TemplateContent { get; }

    public AbpAdminSmsTemplate(string signName, string templateCode, object templateContent)
    {
        SignName = signName;
        TemplateCode = templateCode;
        TemplateContent = templateContent;
    }
}
