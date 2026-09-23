namespace AbpAdmin;

public static class AbpAdminDomainErrorCodes
{
    /* You can add your business exception error codes here, as constants */

    /// <summary>Host 专属资源在租户上下文被直接调用（权限定义已是 Host-only，此为双保险守卫）。</summary>
    public const string HostSideOnly = "AbpAdmin:HostSideOnly";

    /// <summary>
    /// 菜单管理（动态菜单）相关错误码。
    /// </summary>
    public static class Menus
    {
        /// <summary>菜单不存在。</summary>
        public const string MenuNotFound = "AbpAdmin:MenuNotFound";

        /// <summary>同级下已存在相同路由地址。</summary>
        public const string MenuDuplicatePath = "AbpAdmin:MenuDuplicatePath";

        /// <summary>删除目录时其下仍有子菜单。</summary>
        public const string MenuHasChildren = "AbpAdmin:MenuHasChildren";

        /// <summary>上级菜单不能是自身或其子孙节点。</summary>
        public const string MenuParentCycle = "AbpAdmin:MenuParentCycle";

        /// <summary>目录类型必须有子菜单，菜单类型必须有路由地址。</summary>
        public const string MenuTypeMismatch = "AbpAdmin:MenuTypeMismatch";

        /// <summary>分配菜单时角色不存在。</summary>
        public const string MenuRoleNotFound = "AbpAdmin:MenuRoleNotFound";

        /// <summary>租户上下文读不到宿主菜单模板（独立数据库租户或模板未播种）。</summary>
        public const string HostTemplateMissing = "AbpAdmin:MenuHostTemplateMissing";

        /// <summary>套餐勾选的模板菜单已不存在（模板重种后 Guid 悬空），拒绝执行以防租户菜单被清空。</summary>
        public const string PackageMenusDangling = "AbpAdmin:MenuPackageMenusDangling";

        /// <summary>菜单路由地址不是合法的站内路径（须以 / 开头且非外链）。</summary>
        public const string MenuInvalidPath = "AbpAdmin:MenuInvalidPath";

        /// <summary>绑定的权限不存在，或对当前租户不可用（Host-only 权限）。</summary>
        public const string MenuInvalidPermission = "AbpAdmin:MenuInvalidPermission";
    }

    /// <summary>
    /// 租户套餐（Host 专属）相关错误码。
    /// </summary>
    public static class TenantPackages
    {
        /// <summary>套餐不存在。</summary>
        public const string TenantPackageNotFound = "AbpAdmin:TenantPackageNotFound";

        /// <summary>套餐名已存在。</summary>
        public const string TenantPackageNameDuplicate = "AbpAdmin:TenantPackageNameDuplicate";

        /// <summary>勾选的菜单不属于 Host 全局模板。</summary>
        public const string InvalidTemplateMenu = "AbpAdmin:TenantPackageInvalidTemplateMenu";

        /// <summary>套餐未勾选任何菜单，应用前须先配置（放行会把租户菜单重置为空）。</summary>
        public const string TenantPackageMenusEmpty = "AbpAdmin:TenantPackageMenusEmpty";
    }

    public static class Localization
    {
        public const string LanguageAlreadyExists = "AbpAdmin:LanguageAlreadyExists";
        public const string CannotDeleteDefaultLanguage = "AbpAdmin:CannotDeleteDefaultLanguage";
        public const string CannotDisableDefaultLanguage = "AbpAdmin:CannotDisableDefaultLanguage";
        public const string CannotSetDisabledLanguageAsDefault = "AbpAdmin:CannotSetDisabledLanguageAsDefault";
        public const string CannotModifyCultureName = "AbpAdmin:CannotModifyCultureName";

        /// <summary>语言文本覆盖写入口校验：资源名未注册（孤儿覆盖行不可见且不可管理）。</summary>
        public const string UnknownResourceName = "AbpAdmin:UnknownLocalizationResource";

        /// <summary>语言文本覆盖写入口校验：文化名不是合法的文化标识。</summary>
        public const string InvalidCultureName = "AbpAdmin:InvalidLocalizationCultureName";
    }

    /// <summary>
    /// T2.8 SaaS Pro 缺口：租户相关错误码。
    /// </summary>
    public static class Tenants
    {
        /// <summary>租户已停用（ActivationState = Passive）。</summary>
        public const string TenantIsPassive = "AbpAdmin:TenantIsPassive";

        /// <summary>租户已到期（ActiveWithLimitedTime 且 ActivationEndDate 已过）。</summary>
        public const string TenantActivationExpired = "AbpAdmin:TenantActivationExpired";

        /// <summary>租户连接字符串管理被设置项禁用。</summary>
        public const string TenantConnectionStringManagementDisabled = "AbpAdmin:TenantConnectionStringManagementDisabled";

        /// <summary>连接字符串名称不是 Default 或 IsUsedByTenants 的数据库。</summary>
        public const string InvalidTenantConnectionStringName = "AbpAdmin:InvalidTenantConnectionStringName";

        /// <summary>连接字符串值包含掩码字面量（掩码是管理端占位符，不允许作为真值提交）。</summary>
        public const string InvalidTenantConnectionStringValue = "AbpAdmin:InvalidTenantConnectionStringValue";

        /// <summary>租户菜单清理在错误的租户上下文中调用（删除范围跟随 ICurrentTenant，防御性校验）。</summary>
        public const string TenantMenuCleanupWrongTenantContext = "AbpAdmin:TenantMenuCleanupWrongTenantContext";
    }

    /// <summary>
    /// T2.8 SaaS Pro 缺口：版本相关错误码。
    /// </summary>
    public static class Editions
    {
        /// <summary>删除版本时迁移目标不能是被删除的版本自身。</summary>
        public const string CannotMoveTenantsToSameEdition = "AbpAdmin:CannotMoveTenantsToSameEdition";
    }

    /// <summary>
    /// 数据范围（RoleDataScope）相关错误码。
    /// </summary>
    public static class DataScopes
    {
        /// <summary>角色的数据范围配置不存在（WithData("Id")）。</summary>
        public const string RoleDataScopeNotFound = "AbpAdmin:DataScope:RoleDataScopeNotFound";

        /// <summary>该角色已存在数据范围配置（WithData("RoleName")）。</summary>
        public const string RoleDataScopeAlreadyExists = "AbpAdmin:DataScope:RoleDataScopeAlreadyExists";

        /// <summary>角色不存在（WithData("RoleName")）。</summary>
        public const string RoleDataScopeRoleNotFound = "AbpAdmin:DataScope:RoleDataScopeRoleNotFound";

        /// <summary>自定义范围里存在不存在的组织单元（WithData("Count")）。</summary>
        public const string RoleDataScopeOrganizationUnitNotFound = "AbpAdmin:DataScope:RoleDataScopeOrganizationUnitNotFound";

        /// <summary>保存实体时无法解析出应写入的组织单元 Id（WithData("EntityType")）。</summary>
        public const string CannotResolveOrganizationUnit = "AbpAdmin:DataScope:CannotResolveOrganizationUnit";
    }

    /// <summary>
    /// 数据字典视图/字典项元数据相关错误码。
    /// </summary>
    public static class DataDictionaries
    {
        /// <summary>标签颜色不在允许集合内（WithData("TagType")）。</summary>
        public const string InvalidTagType = "AbpAdmin:DataDictionary:InvalidTagType";

        /// <summary>字典不存在（WithData("DictionaryCode")）。</summary>
        public const string DictionaryNotFound = "AbpAdmin:DataDictionary:DictionaryNotFound";

        /// <summary>字典项不存在，无法设置元数据（WithData("DictionaryCode")、WithData("ItemCode")）。</summary>
        public const string ItemNotFound = "AbpAdmin:DataDictionary:ItemNotFound";

        /// <summary>字典编码已被占用（WithData("DictionaryCode")）。</summary>
        public const string DictionaryCodeAlreadyExists = "AbpAdmin:DataDictionary:DictionaryCodeAlreadyExists";

        /// <summary>原子保存字典项时编码重复（WithData("DictionaryCode")、WithData("Code")）。</summary>
        public const string DuplicateItemCode = "AbpAdmin:DataDictionary:DuplicateItemCode";

        /// <summary>静态字典的项集合由代码定义，不允许增删项或改编码（WithData("DictionaryCode")）。</summary>
        public const string StaticStructureLocked = "AbpAdmin:DataDictionary:StaticStructureLocked";
    }

    /// <summary>
    /// T2.9 OpenIddict Pro 缺口：应用/scope 管理错误码。
    /// </summary>
    public static class OpenIddict
    {
        /// <summary>不能创建/改名为内置 scope（address/email/phone/profile/roles）。</summary>
        public const string BuiltInScopeName = "AbpAdmin:OpenIddictBuiltInScopeName";

        /// <summary>redirect/post-logout/front-channel logout URI 必须是绝对 URI。</summary>
        public const string InvalidAbsoluteUri = "AbpAdmin:OpenIddictInvalidAbsoluteUri";

        /// <summary>JWKS 不是合法的 JSON Web Key Set。</summary>
        public const string InvalidJsonWebKeySet = "AbpAdmin:OpenIddictInvalidJsonWebKeySet";

        /// <summary>Public 客户端不能持有 client secret 或 JWKS。</summary>
        public const string PublicClientCannotHaveCredentials = "AbpAdmin:OpenIddictPublicClientCannotHaveCredentials";

        /// <summary>Confidential 客户端至少要有一种凭据（secret 或 JWKS）。</summary>
        public const string ConfidentialClientRequiresCredential = "AbpAdmin:OpenIddictConfidentialClientRequiresCredential";

        /// <summary>不能移除最后一种凭据（空 JWKS 仅在 secret 存在时才允许显式移除）。</summary>
        public const string CannotRemoveLastCredential = "AbpAdmin:OpenIddictCannotRemoveLastCredential";

        /// <summary>应用不是 Confidential 或未启用 Client Credentials flow，不能代取 token。</summary>
        public const string GenerateAccessTokenNotAllowed = "AbpAdmin:OpenIddictGenerateAccessTokenNotAllowed";

        /// <summary>请求的 scope 未分配给该应用。</summary>
        public const string ScopeNotAssigned = "AbpAdmin:OpenIddictScopeNotAssigned";

        /// <summary>向认证服务器请求 token 失败（含未配置 AuthServer:Authority）。</summary>
        public const string GenerateAccessTokenFailed = "AbpAdmin:OpenIddictGenerateAccessTokenFailed";

        /// <summary>按用户吊销全部令牌时必须提供 subject（用户 Id）。</summary>
        public const string SubjectRequired = "AbpAdmin:OpenIddictSubjectRequired";

        /// <summary>种子数据配置了 ClientId 但缺 RootUrl（迁移启动期显式失败，优于 NRE）。</summary>
        public const string SeedRootUrlMissing = "AbpAdmin:OpenIddictSeedRootUrlMissing";
    }

    /// <summary>
    /// T2.7 Account Pro 缺口：账户相关错误码。
    /// </summary>
    public static class Account
    {
        /// <summary>模拟登录的目标用户不存在。</summary>
        public const string ImpersonationTargetUserNotFound = "AbpAdmin:ImpersonationTargetUserNotFound";

        /// <summary>模拟登录的目标租户不存在。</summary>
        public const string ImpersonationTenantNotFound = "AbpAdmin:ImpersonationTenantNotFound";

        /// <summary>目标租户没有可用的管理员用户，无法模拟。</summary>
        public const string ImpersonationTenantAdminNotFound = "AbpAdmin:ImpersonationTenantAdminNotFound";

        /// <summary>模拟令牌中记录的原始用户已不存在。</summary>
        public const string ImpersonatorUserNotFound = "AbpAdmin:ImpersonatorUserNotFound";

        /// <summary>向认证服务器请求模拟登录令牌失败。</summary>
        public const string ImpersonationTokenExchangeFailed = "AbpAdmin:ImpersonationTokenExchangeFailed";

        /// <summary>邮箱未注册（仅在关闭防邮箱枚举时才会暴露此错误）。</summary>
        public const string EmailNotRegistered = "AbpAdmin:EmailNotRegistered";

        public const string CannotRemoveLastExternalLogin = "AbpAdmin:CannotRemoveLastExternalLogin";
        public const string RemoveExternalLoginFailed = "AbpAdmin:RemoveExternalLoginFailed";
        public const string AuthenticatorKeyMissing = "AbpAdmin:AuthenticatorKeyMissing";
        public const string InvalidAuthenticatorCode = "AbpAdmin:InvalidAuthenticatorCode";
        public const string SetTwoFactorEnabledFailed = "AbpAdmin:SetTwoFactorEnabledFailed";
        public const string CannotDelegateToSelf = "AbpAdmin:CannotDelegateToSelf";
        public const string InvalidDelegationPeriod = "AbpAdmin:InvalidDelegationPeriod";
        public const string DelegationNotFound = "AbpAdmin:DelegationNotFound";
        public const string DelegationNotActive = "AbpAdmin:DelegationNotActive";
        public const string CaptchaNotConfigured = "AbpAdmin:CaptchaNotConfigured";
        public const string CaptchaFailed = "AbpAdmin:CaptchaFailed";
        public const string PasskeyAttestationFailed = "AbpAdmin:PasskeyAttestationFailed";
        public const string PasskeyRemoveFailed = "AbpAdmin:PasskeyRemoveFailed";
        public const string PasskeyAssertionFailed = "AbpAdmin:PasskeyAssertionFailed";

        /// <summary>邮箱确认验证码无效或已过期。</summary>
        public const string InvalidEmailConfirmationCode = "AbpAdmin:InvalidEmailConfirmationCode";

        /// <summary>手机号确认验证码无效或已过期。</summary>
        public const string InvalidPhoneConfirmationCode = "AbpAdmin:InvalidPhoneConfirmationCode";

        /// <summary>双因素验证码无效或已过期。</summary>
        public const string InvalidTwoFactorCode = "AbpAdmin:InvalidTwoFactorCode";

        /// <summary>无密码登录链接（或兜底验证码）无效、已过期或已被使用。</summary>
        public const string InvalidMagicLink = "AbpAdmin:InvalidMagicLink";

        /// <summary>恢复码无效或已被使用。</summary>
        public const string InvalidRecoveryCode = "AbpAdmin:InvalidRecoveryCode";

        /// <summary>关联账号：账号不存在或密码错误（统一口径，防用户名枚举）。</summary>
        public const string LinkedAccountInvalidCredentials = "AbpAdmin:LinkedAccountInvalidCredentials";

        /// <summary>关联账号：两个账号已存在关联。</summary>
        public const string LinkedAccountAlreadyLinked = "AbpAdmin:LinkedAccountAlreadyLinked";

        /// <summary>关联账号：不能关联自己的当前账号。</summary>
        public const string LinkedAccountSelfNotAllowed = "AbpAdmin:LinkedAccountSelfNotAllowed";

        /// <summary>关联账号：关联记录不存在或不属于当前用户。</summary>
        public const string LinkedAccountNotFound = "AbpAdmin:LinkedAccountNotFound";

        /// <summary>关联账号：切换到关联账号时换票失败。</summary>
        public const string LinkedAccountSwitchFailed = "AbpAdmin:LinkedAccountSwitchFailed";

        /// <summary>没有已确认的邮箱或手机号，无法发送/校验双因素验证码。</summary>
        public const string TwoFactorNoConfirmedProvider = "AbpAdmin:TwoFactorNoConfirmedProvider";

        /// <summary>启用双因素认证前必须先确认邮箱或手机号。</summary>
        public const string TwoFactorRequiresConfirmedProvider = "AbpAdmin:TwoFactorRequiresConfirmedProvider";

        /// <summary>AuthServer:Authority 未配置，无法向认证服务器请求 token（作为 WithData("reason") 的本地化文案键引用）。</summary>
        public const string OpenIddictAuthorityNotConfigured = "AbpAdmin:OpenIddictAuthorityNotConfigured";
    }

    public static class Files
    {
        public const string ShareLinkExpired = "AbpAdmin:FileShareLinkExpired";
        public const string ShareLinkInvalid = "AbpAdmin:FileShareLinkInvalid";
        public const string StorageQuotaExceeded = "AbpAdmin:FileStorageQuotaExceeded";
    }

    public static class Payments
    {
        public const string RefundAmountInvalid = "AbpAdmin:PaymentRefundAmountInvalid";

        /// <summary>该支付单已有一个退款操作正在处理（分布式锁占用中）。</summary>
        public const string RefundInProgress = "AbpAdmin:PaymentRefundInProgress";
    }

    /// <summary>
    /// 文本模板管理错误码。
    /// </summary>
    public static class TextTemplates
    {
        /// <summary>模板定义不存在（WithData("Name")）。</summary>
        public const string TemplateNotFound = "AbpAdmin:TextTemplateNotFound";
    }

    /// <summary>
    /// 虚拟文件浏览器错误码。
    /// </summary>
    public static class VirtualFileExplorer
    {
        /// <summary>请求的虚拟文件不存在（WithData("Path")）。</summary>
        public const string FileNotFound = "AbpAdmin:VirtualFileNotFound";
    }

    /// <summary>
    /// T2.5 操作限流错误码。
    /// </summary>
    public static class RateLimiting
    {
        /// <summary>常规超限。</summary>
        public const string OperationRateLimitExceeded = "AbpAdmin:OperationRateLimitExceeded";

        /// <summary>Ban 策略永久拒绝（maxCount: 0 的规则，此时 RetryAfter 为 null）。</summary>
        public const string OperationBanned = "AbpAdmin:OperationBanned";

        /// <summary>防重复提交：同用户同方法同参数在窗口内重复提交（WithData("IntervalSeconds")）。</summary>
        public const string DuplicateSubmit = "AbpAdmin:DuplicateSubmit";
    }

    /// <summary>
    /// T3.1 图片处理错误码。
    /// </summary>
    public static class Imaging
    {
        /// <summary>头像字节数超过上限（WithData("MaxSize")）。</summary>
        public const string AvatarTooLarge = "AbpAdmin:AvatarTooLarge";

        /// <summary>图片处理并发闸门等待超时，HTTP 503 + Retry-After。</summary>
        public const string ImageProcessingBusy = "AbpAdmin:ImageProcessingBusy";

        /// <summary>流内容与扩展名声明的图片格式不一致（magic bytes 校验失败），或像素超过解码上限。</summary>
        public const string InvalidImageContent = "AbpAdmin:InvalidImageContent";

        /// <summary>扩展名不在白名单内。</summary>
        public const string InvalidImageExtension = "AbpAdmin:InvalidImageExtension";
    }

    /// <summary>
    /// 岗位管理（对标 RuoYi sys_post）错误码。
    /// </summary>
    public static class Posts
    {
        /// <summary>岗位名已存在（WithData("Name")）。</summary>
        public const string PostNameDuplicate = "AbpAdmin:PostNameDuplicate";

        /// <summary>岗位编码已存在（WithData("Code")）。</summary>
        public const string PostCodeDuplicate = "AbpAdmin:PostCodeDuplicate";
    }

    /// <summary>
    /// GDPR 个人数据请求错误码（WithData 参数见 zh-Hans/en 消息模板）。
    /// </summary>
    public static class Gdpr
    {
        /// <summary>距上次请求的间隔未到（WithData("RequestTimeInterval")）。</summary>
        public const string RequestIntervalNotElapsed = "AbpAdmin:Gdpr:RequestIntervalNotElapsed";

        /// <summary>下载 token 无效或已过期（一次性消耗，需重新获取）。</summary>
        public const string InvalidDownloadToken = "AbpAdmin:Gdpr:InvalidDownloadToken";

        /// <summary>导出数据尚未就绪（WithData("ReadyTime")）。</summary>
        public const string DataNotReady = "AbpAdmin:Gdpr:DataNotReady";

        /// <summary>账户删除二次确认的密码不正确。</summary>
        public const string IncorrectPassword = "AbpAdmin:Gdpr:IncorrectPassword";

        /// <summary>账户删除失败（WithData("Errors") 为 Identity 框架原始错误）。</summary>
        public const string UserDeletionFailed = "AbpAdmin:Gdpr:UserDeletionFailed";
    }

    /// <summary>
    /// 缓存监控错误码。
    /// </summary>
    public static class Monitoring
    {
        /// <summary>缓存后端是 Memory，无法枚举键。</summary>
        public const string CacheMonitorRedisDisabled = "AbpAdmin:CacheMonitorRedisDisabled";

        /// <summary>目标键不在 ABP 缓存前缀下，拒绝读取/删除。</summary>
        public const string CacheMonitorKeyNotAllowed = "AbpAdmin:CacheMonitorKeyNotAllowed";
    }

    /// <summary>
    /// T3.3 定时作业错误码。
    /// </summary>
    public static class ScheduledJobs
    {
        /// <summary>cron 表达式非法（WithData("Expression")）。Quartz 需要 7 段：秒 分 时 日 月 周 [年]。</summary>
        public const string InvalidCronExpression = "AbpAdmin:InvalidCronExpression";

        /// <summary>未知 JobType（WithData("JobType")）。GUI 测试 D10：不在已知处理器清单内的类型在创建/更新时即拒绝，避免作业到点必败。</summary>
        public const string UnknownJobType = "AbpAdmin:ScheduledJobUnknownJobType";

        /// <summary>同名定时作业已存在（WithData("Name")）。</summary>
        public const string ScheduledJobNameAlreadyExists = "AbpAdmin:ScheduledJobNameAlreadyExists";

        /// <summary>停用的作业不能手动触发。</summary>
        public const string ScheduledJobDisabled = "AbpAdmin:ScheduledJobDisabled";
    }

    /// <summary>
    /// T3.5 通知服务错误码。
    /// </summary>
    public static class Notifications
    {
        /// <summary>阿里云短信凭据未配置（AccessKeyId/AccessKeySecret 设置项为空）。</summary>
        public const string SmsAliyunCredentialNotConfigured = "AbpAdmin:SmsAliyunCredentialNotConfigured";

        /// <summary>腾讯云短信凭据未配置（SecretId/SecretKey/SmsSdkAppid 设置项为空）。</summary>
        public const string SmsTencentCloudCredentialNotConfigured = "AbpAdmin:SmsTencentCloudCredentialNotConfigured";

        /// <summary>短信模板未配置或未随消息给出（签名/模板编码缺失），或 Text 不是模板参数 JSON。</summary>
        public const string SmsTemplateNotConfigured = "AbpAdmin:SmsTemplateNotConfigured";

        /// <summary>厂商接口返回失败（WithData("Provider")、WithData("ProviderError")）。</summary>
        public const string SmsSendFailed = "AbpAdmin:SmsSendFailed";

        /// <summary>只有失败的通知才能重试。</summary>
        public const string OnlyFailedNotificationCanRetry = "AbpAdmin:OnlyFailedNotificationCanRetry";

        /// <summary>广播目标类型非法（All/Role/OrganizationUnit 之外），或 Role/OrganizationUnit 缺 TargetId。</summary>
        public const string InvalidBroadcastTarget = "AbpAdmin:InvalidBroadcastTarget";

        /// <summary>广播渠道为空或含未知渠道（Mailing/Sms/InApp 之外）。</summary>
        public const string InvalidBroadcastMethods = "AbpAdmin:InvalidBroadcastMethods";

        /// <summary>发送目标用户列表为空（WithData("ParamName")）。</summary>
        public const string EmptyUserIds = "AbpAdmin:EmptyUserIds";
    }
}
