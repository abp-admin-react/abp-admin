namespace AbpAdmin.Settings;

/// <summary>
/// 自研 SettingUi 元数据的常量（与 EasyAbp SettingUi 2.10 的 SettingUiConst 并存）。
/// 分组名与 "password" 控件类型在后端定义（各 SettingDefinitionProvider）、后端脱敏覆写
/// （AbpAdminSettingUiAppService）、前端类型映射（web/src/abp/settingUi.ts 的
/// SettingUiComponentTypes.Password）三处必须同源——任何一端改字面量即静默断链
/// （密码框退化成明文 text 框），所以集中在这里定义。
/// </summary>
public static class AbpAdminSettingUiConsts
{
    /// <summary>
    /// 密码控件类型。EasyAbp SettingUi 2.10 的 SettingUiConst.Components 没有该类型（T3.5 前端扩展）。
    /// </summary>
    public const string PasswordType = "password";

    /// <summary>
    /// SettingUi 一级分组（Group1 = 设置页一级 Tab），与按域拆分的各 SettingDefinitionProvider 对应。
    /// </summary>
    public static class Groups
    {
        public const string System = "System";
        public const string Account = "Account";
        public const string Saas = "Saas";
        public const string Audit = "Audit";
        public const string Sms = "Sms";
        public const string Payment = "Payment";
    }
}
