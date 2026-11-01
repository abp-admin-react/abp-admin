namespace AbpAdmin.Biz.Template.Configuration;

/// <summary>
/// 模块部署期配置（配置三层约定的第②层）：换部署环境要变、改完需要重启的模块自有配置。
///
/// 配置三层约定（详见 <see cref="BizTemplateModule"/> 类注释）：
/// ① 基础设施配置（连接串/Redis/端口）——只属于宿主 appsettings.json，模块只读不写；
/// ② 模块部署期配置——本类 + 同目录 BizTemplate.json，基线随模块工程走，宿主文件零改动；
/// ③ 运行期业务参数——Settings/BizTemplateSettingDefinitionProvider，设置页可调、免重启。
///
/// 绑定与覆盖（BizTemplateModule.ConfigureServices）：
/// 基线 = 随模块复制的 Configuration/BizTemplate.json；
/// 覆盖 = 宿主 IConfiguration 的同名节（SectionName）——宿主 appsettings.json /
/// appsettings.secrets.json / 环境变量（BizTemplate__MaxPageSize=200）都汇入这一层，
/// 后绑定生效，不配置则完全用基线。宿主文件里不写任何业务节也照常运行。
///
/// 纪律：凭证类敏感值不要进 BizTemplate.json 基线（该文件随源码入库），直接走 secrets/环境变量；
/// 管理员日常要调的参数不要放这里（改了要重启），用第③层 Settings。
/// 复制模块改名时：SectionName、类名、BizTemplate.json 的根节名一起换词根。
/// </summary>
public class BizTemplateOptions
{
    /// <summary>配置节名：模块 JSON 的根节与宿主覆盖通道共用，保持一致才能完成「基线 + 覆盖」合并。</summary>
    public const string SectionName = "BizTemplate";

    /// <summary>
    /// 列表查询单页上限。消费点：BizProjectAppService.GetListAsync（钳制客户端传入的 MaxResultCount，防深分页）。
    /// 这是运维口径的开关，不随运营日常变动，所以放部署期；要界面可调的参数用第③层 Settings。
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// 是否写入模块样例数据。消费点：BizTemplateDataSeedContributor（生产环境置 false 即不产生演示数据）。
    /// 生产切换走环境变量 BizTemplate__SeedSampleData=false，不改宿主任何文件。
    /// </summary>
    public bool SeedSampleData { get; set; } = true;
}
