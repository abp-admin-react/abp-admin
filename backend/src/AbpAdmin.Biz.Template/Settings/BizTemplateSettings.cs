namespace AbpAdmin.Biz.Template.Settings;

/// <summary>
/// 模块运行期业务参数名称（配置三层约定的第③层）：管理员在「设置页」日常调整的参数。
/// 值存框架 SettingManagement 表（按主机/租户区分，ABP 设置管理），改后即时生效、无需重启。
/// 与第②层（Configuration/BizTemplate.json，改了要重启）的分工判定：
/// 换部署环境要变的进文件，运营/管理员日常要调的进这里。
///
/// 机制：定义写在 BizTemplateSettingDefinitionProvider（ABP 免注册自动发现），
/// 存储复用框架 SettingManagement——定义归模块、存储归框架，分离在「定义归属」层，
/// 模块不建自己的参数表。设置页一级 Tab 由 Group1 元数据动态生成，模块不改前端即得独立分组；
/// Tab 显示名走 SettingUiResource 链（模块在 BizTemplateModule 里把自己的本地化 JSON 挂上去）。
///
/// 残留说明：设置页保存过的值在模块移除或参数改名后会变成 SettingManagement 表里的孤儿行
/// ——不影响运行（SettingUi 按定义枚举，不按存储行），但改名上线前应先迁移/清理旧键值。
///
/// 命名约定：BizTemplate.域.参数——复制模块改名时整串换词根（参数名即存储键，改名即换参数）。
/// </summary>
public static class BizTemplateSettings
{
    /// <summary>词根前缀：参数名由它组合（对齐框架 AbpAdminSettings.Prefix 的形态）。</summary>
    private const string Prefix = "BizTemplate";

    /// <summary>
    /// 本模块设置页一级 Tab 名（Group1 元数据值）。显示名 = SettingUiResource 链里查这个
    /// 原样字符串（模块本地化 JSON 里的裸键 "BizTemplate"，经 BizTemplateModule 挂链后生效；
    /// 框架的 "Payment" → "支付" 是同一机制）。
    /// 模块自带 Tab 名，不往框架 AbpAdminSettingUiConsts.Groups 里加——业务分组不混进框架分组。
    /// </summary>
    public const string SettingUiGroup = Prefix;

    /// <summary>项目域参数。</summary>
    public static class Project
    {
        /// <summary>
        /// 单个用户可创建的项目数上限（"0" = 不限）。默认 "100"（定义在 SettingDefinitionProvider）。
        /// 消费点：BizProjectAppService.CreateAsync（配额校验示例——真实业务照这个模式读自己的参数）。
        /// </summary>
        public const string MaxProjectsPerUser = Prefix + ".Project.MaxProjectsPerUser";

        /// <summary>
        /// 配额的定义默认值（与 SettingDefinitionProvider 里的默认保持一致）。
        /// 消费点：CreateAsync 对"存储值非数字/负数"的非法配置按此默认值兜底（fail-closed），
        /// 显式 "0" 仍是不限——配置打错的后果是回到默认上限，而不是悄悄关掉限制。
        /// </summary>
        public const int DefaultMaxProjectsPerUser = 100;
    }
}
