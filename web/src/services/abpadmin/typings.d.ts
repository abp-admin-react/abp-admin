declare namespace API {
  type AbpLoginResult = {
    result?: LoginResultType;
    description?: string;
  };

  type AccountDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    accountGroupName?: string;
    userId?: string;
    balance?: number;
    lockedBalance?: number;
    pendingTopUpPaymentId?: string;
    pendingWithdrawalRecordId?: string;
    pendingWithdrawalAmount?: number;
  };

  type AccountStatusDto = {
    shouldChangePassword?: boolean;
    reason?: string;
  };

  type ActionApiDescriptionModel = {
    uniqueName?: string;
    name?: string;
    httpMethod?: string;
    url?: string;
    supportedVersions?: string[];
    parametersOnMethod?: MethodParameterApiDescriptionModel[];
    parameters?: ParameterApiDescriptionModel[];
    returnValue?: ReturnValueApiDescriptionModel;
    allowAnonymous?: boolean;
    authorizeDatas?: AuthorizeDataApiDescriptionModel[];
    implementFrom?: string;
    summary?: string;
    remarks?: string;
    description?: string;
    displayName?: string;
  };

  type ApplicationApiDescriptionModel = {
    modules?: Record<string, any>;
    types?: Record<string, any>;
  };

  type ApplicationAuthConfigurationDto = {
    grantedPolicies?: Record<string, any>;
  };

  type ApplicationConfigurationDto = {
    localization?: ApplicationLocalizationConfigurationDto;
    auth?: ApplicationAuthConfigurationDto;
    setting?: ApplicationSettingConfigurationDto;
    currentUser?: CurrentUserDto;
    features?: ApplicationFeatureConfigurationDto;
    globalFeatures?: ApplicationGlobalFeatureConfigurationDto;
    multiTenancy?: MultiTenancyInfoDto;
    currentTenant?: CurrentTenantDto;
    timing?: TimingDto;
    clock?: ClockDto;
    objectExtensions?: ObjectExtensionsDto;
    extraProperties?: Record<string, any>;
  };

  type ApplicationFeatureConfigurationDto = {
    values?: Record<string, any>;
  };

  type ApplicationGlobalFeatureConfigurationDto = {
    enabledFeatures?: string[];
  };

  type ApplicationLocalizationConfigurationDto = {
    values?: Record<string, any>;
    resources?: Record<string, any>;
    languages?: LanguageInfo[];
    currentCulture?: CurrentCultureDto;
    defaultResourceName?: string;
    languagesMap?: Record<string, any>;
    languageFilesMap?: Record<string, any>;
    useRouteBasedCulture?: boolean;
  };

  type ApplicationLocalizationDto = {
    resources?: Record<string, any>;
    currentCulture?: CurrentCultureDto;
  };

  type ApplicationLocalizationResourceDto = {
    texts?: Record<string, any>;
    baseResources?: string[];
  };

  type ApplicationSettingConfigurationDto = {
    values?: Record<string, any>;
  };

  type ApplyTenantPackageDto = {
    /** null = 全量重置（不限套餐）。 */
    packageId?: string;
  };

  type AuditLogActionDto = {
    serviceName?: string;
    methodName?: string;
    executionDuration?: number;
  };

  type AuditLogAverageDurationDto = {
    date?: string;
    avgExecutionDuration?: number;
  };

  type AuditLogDto = {
    id?: string;
    applicationName?: string;
    userName?: string;
    executionTime?: string;
    executionDuration?: number;
    clientIpAddress?: string;
    /** 客户端 Id（OpenIddict client_id，详情页展示用；匿名/用户令牌请求可为空）。 */
    clientId?: string;
    httpMethod?: string;
    url?: string;
    exceptions?: string;
    httpStatusCode?: number;
    correlationId?: string;
    /** IP 归属地（展示字段，由应用服务按当页 IP 批量解析后填充）。 */
    ipLocation?: string;
    /** 是否已被标记处理（行内 HandledAt 映射列非空即视为已处理）。 */
    isHandled?: boolean;
    handledBy?: string;
    handledAt?: string;
    handleNote?: string;
    actions?: AuditLogActionDto[];
    entityChanges?: EntityChangeDto[];
  };

  type AuditLogErrorRateDataPoint = {
    date?: string;
    totalCount?: number;
    errorCount?: number;
    errorRate?: number;
  };

  type AuditLogErrorRateDto = {
    totalCount?: number;
    errorCount?: number;
    errorRate?: number;
    dataPoints?: AuditLogErrorRateDataPoint[];
  };

  type AuditLogExportResultDto = {
    /** 是否已转为后台任务。true 时前端应提示用户等待邮件通知。 */
    isQueued?: boolean;
  };

  type AuthenticatorKeyDto = {
    sharedKey?: string;
    authenticatorUri?: string;
  };

  type AuthenticatorRecoveryCodesDto = {
    recoveryCodes?: string[];
  };

  type AuthenticatorStatusDto = {
    enabled?: boolean;
    hasAuthenticatorKey?: boolean;
  };

  type AuthorizeDataApiDescriptionModel = {
    policy?: string;
    roles?: string;
  };

  type AvatarInfoDto = {
    /** 形如 /api/app/profile-avatar/{userId}?v={version}；无头像时为 null。 */
    avatarUrl?: string;
  };

  type BackgroundJobDto = {
    id?: string;
    applicationName?: string;
    jobName?: string;
    jobArgs?: string;
    tryCount?: number;
    creationTime?: string;
    nextTryTime?: string;
    lastTryTime?: string;
    isAbandoned?: boolean;
    completionTime?: string;
    priority?: number;
  };

  type BatchUpdateDataDictionaryItemsMetaInput = {
    items?: UpdateDataDictionaryItemMetaItemInput[];
  };

  type BriefFileUserInfoModel = {
    id?: string;
    userName?: string;
  };

  type BroadcastNotificationInput = {
    /** All / Role / OrganizationUnit。 */
    targetType: string;
    /** 角色 Id 或组织单元 Id；TargetType = All 时为 null。 */
    targetId?: string;
    /** 渠道集合：Mailing / Sms / InApp，至少一个。 */
    notificationMethods: string[];
    /** 标题（邮件 Subject / 站内信标题）。 */
    title: string;
    /** 正文（邮件 / 站内信）。 */
    body: string;
    /** 含 Sms 渠道时的短信内容/模板参数 JSON；不填则回落用 Body。 */
    smsText?: string;
    /** 含 Sms 渠道时的渠道属性（模板编码/签名/参数）。 */
    smsProperties?: Record<string, any>;
  };

  type CacheKeyDto = {
    /** 完整键名。 */
    key?: string;
    /** 值类型（string/hash/list/set/zset/stream/none）。 */
    type?: string;
    /** 内存占用估算（字节，MEMORY USAGE；服务器不支持时为 null）。 */
    sizeBytes?: number;
    /** 剩余过期秒数；null = 永不过期。 */
    ttlSeconds?: number;
  };

  type CacheKeyListResultDto = {
    keys?: CacheKeyDto[];
    /** 下一次 SCAN 的游标；0 表示扫描已到末尾。 */
    nextCursor?: number;
  };

  type CacheMonitorInfoDto = {
    /** redis | memory。 */
    backend?: string;
    /** ABP 分布式缓存键前缀（AbpDistributedCacheOptions.KeyPrefix，默认为空）。
            键的结构性前缀是 c:（宿主）/ t:租户Id,（租户），本字段只是 KeyPrefix 配置的透出。 */
    keyPrefix?: string;
    /** 库内总键数（Redis；memory 后端为 null）。 */
    totalKeys?: number;
    /** Redis 服务器版本。 */
    redisVersion?: string;
    /** Redis 已用内存（字节）。 */
    usedMemoryBytes?: number;
    /** Redis maxmemory（字节，0 = 未限制）。 */
    maxMemoryBytes?: number;
    /** 连接失败时的错误信息（Backend=redis 且连不上时非空）。 */
    connectionError?: string;
  };

  type CacheValueDto = {
    key?: string;
    type?: string;
    /** 剩余过期秒数；null = 永不过期。 */
    ttlSeconds?: number;
    /** 值的文本预览（string 原文；hash 逐字段 field = value；集合逐元素）。 */
    content?: string;
    /** 内容超过预览上限被截断。 */
    truncated?: boolean;
  };

  type CaptchaImageDto = {
    /** 验证码标识，提交时随答案一起回传。 */
    id?: string;
    /** data:image/png;base64,... 形式的图片。 */
    imageDataUrl?: string;
  };

  type ChangeBalanceInput = {
    changedBalance?: number;
  };

  type ChangeLockedBalanceInput = {
    changedLockedBalance?: number;
  };

  type ChangePasswordInput = {
    currentPassword?: string;
    newPassword: string;
  };

  type CheckTenantConnectionStringInput = {
    connectionString: string;
  };

  type CheckTenantConnectionStringResultDto = {
    isValid?: boolean;
    /** 失败原因（本地化后的通用文案；底层驱动异常原文只进服务端日志，不回传客户端）。 */
    errorMessage?: string;
  };

  type ClaimTypeDto = {
    id?: string;
    name?: string;
    required?: boolean;
    isStatic?: boolean;
    regex?: string;
    regexDescription?: string;
    description?: string;
    valueType?: IdentityClaimValueType;
  };

  type ClaimValueDto = {
    claimType?: string;
    claimValue?: string;
  };

  type ClockDto = {
    kind?: string;
  };

  type ConfirmEmailInput = {
    email: string;
    code: string;
  };

  type ConfirmPhoneNumberInput = {
    phoneNumber: string;
    code: string;
  };

  type ControllerApiDescriptionModel = {
    controllerName?: string;
    controllerGroupName?: string;
    isRemoteService?: boolean;
    isIntegrationService?: boolean;
    apiVersion?: string;
    type?: string;
    summary?: string;
    remarks?: string;
    description?: string;
    displayName?: string;
    interfaces?: ControllerInterfaceApiDescriptionModel[];
    actions?: Record<string, any>;
  };

  type ControllerInterfaceApiDescriptionModel = {
    type?: string;
    name?: string;
    methods?: InterfaceMethodApiDescriptionModel[];
  };

  type CreateClaimTypeDto = {
    name: string;
    required?: boolean;
    regex?: string;
    regexDescription?: string;
    description?: string;
    valueType?: IdentityClaimValueType;
  };

  type CreateDataScopeDemoDto = {
    name: string;
    /** 显式指定组织单元。为 null 时由写入侧拦截器从当前数据范围快照自动填充。 */
    organizationUnitId?: string;
  };

  type CreateEditionDto = {
    displayName: string;
  };

  type CreateFileInput = {
    extraProperties?: Record<string, any>;
    fileContainerName: string;
    parentId?: string;
    ownerUserId?: string;
    fileName: string;
    mimeType?: string;
    fileType?: FileType;
    content?: string;
  };

  type CreateFileOutput = {
    extraProperties?: Record<string, any>;
    fileInfo?: FileInfoDto;
    downloadInfo?: FileDownloadInfoModel;
  };

  type CreateFileShareLinkInput = {
    fileId: string;
    /** 过期时间。为空则默认 7 天；不可早于现在，也不可超过 AbpAdmin.Files.FileShareLinkConsts.MaxExpireDays 天。 */
    expireTime?: string;
    isPublic?: boolean;
    /** 可选下载次数上限。null 不限次；若提供必须 >= 1。 */
    maxDownloads?: number;
  };

  type CreateLanguageDto = {
    cultureName: string;
    uiCultureName: string;
    displayName: string;
    flagIcon?: string;
    isEnabled?: boolean;
  };

  type CreateManyFileInput = {
    extraProperties?: Record<string, any>;
    fileInfos?: CreateFileInput[];
  };

  type CreateManyFileOutput = {
    items?: CreateFileOutput[];
  };

  type CreateOpenIddictApplicationDto = {
    clientId: string;
    displayName: string;
    /** public / confidential，见 OpenIddictConstants.ClientTypes。 */
    clientType: string;
    /** web / native，见 OpenIddictConstants.ApplicationTypes。 */
    applicationType: string;
    /** explicit / external / implicit / systematic，见 OpenIddictConstants.ConsentTypes。 */
    consentType: string;
    /** 只写：更新时留空表示保持原值。管理 API 永不回读该值。 */
    clientSecret?: string;
    /** 只写：JWKS（JSON）。更新时 null=保持原值；空字符串=显式移除
（仅在 secret 仍存在时才允许移除，见 secret 只写语义规则 4）。 */
    jsonWebKeySet?: string;
    clientUri?: string;
    logoUri?: string;
    /** 每行一个，必须是绝对 URI。 */
    redirectUris?: string;
    /** 每行一个，必须是绝对 URI。 */
    postLogoutRedirectUris?: string;
    /** 必须是绝对 URI。 */
    frontChannelLogoutUri?: string;
    allowAuthorizationCodeFlow?: boolean;
    allowImplicitFlow?: boolean;
    /** 勾选后服务端派生：同时启用 Authorization Code 与 Implicit。 */
    allowHybridFlow?: boolean;
    allowPasswordFlow?: boolean;
    allowClientCredentialsFlow?: boolean;
    allowRefreshTokenFlow?: boolean;
    allowTokenExchangeFlow?: boolean;
    allowDeviceAuthorizationFlow?: boolean;
    enableEndSessionEndpoint?: boolean;
    enablePushedAuthorizationEndpoint?: boolean;
    requirePkce?: boolean;
    /** 勾选后服务端派生：同时启用 Pushed Authorization 端点。 */
    requirePushedAuthorization?: boolean;
    /** 允许的 scopes（授予 scp: 权限）。 */
    scopes?: string[];
    /** extension grant types（授予 gt: 权限）。 */
    extensionGrantTypes?: string[];
  };

  type CreateOpenIddictScopeDto = {
    name: string;
    displayName?: string;
    description?: string;
    resources?: string;
  };

  type CreateOrganizationUnitDto = {
    parentId?: string;
    displayName: string;
  };

  type CreatePostDto = {
    name: string;
    code: string;
    sortOrder?: number;
    status?: PostStatusEnum;
    remark?: string;
  };

  type CreateScheduledJobDto = {
    name: string;
    jobType: string;
    cronExpression: string;
    isEnabled?: boolean;
    description?: string;
    payload?: string;
  };

  type CreateUpdateRoleDataScopeDto = {
    roleName: string;
    scopeType: DataScopeTypeEnum;
    /** 自定义组织单元集合（AbpAdmin.DataScopes.DataScopeTypeEnum.Custom 时必填）。 */
    customOrganizationUnitIds?: string[];
  };

  type CreateUserDelegationInput = {
    targetUserId: string;
    startTime: string;
    endTime: string;
  };

  type CronPreviewDto = {
    isValid?: boolean;
    errorMessage?: string;
    nextFireTimes?: string[];
  };

  type CurrentCultureDto = {
    displayName?: string;
    englishName?: string;
    threeLetterIsoLanguageName?: string;
    twoLetterIsoLanguageName?: string;
    isRightToLeft?: boolean;
    cultureName?: string;
    name?: string;
    nativeName?: string;
    dateTimeFormat?: DateTimeFormatDto;
  };

  type CurrentTenantDto = {
    id?: string;
    name?: string;
    isAvailable?: boolean;
  };

  type CurrentUserDto = {
    isAuthenticated?: boolean;
    id?: string;
    tenantId?: string;
    impersonatorUserId?: string;
    impersonatorTenantId?: string;
    impersonatorUserName?: string;
    impersonatorTenantName?: string;
    userName?: string;
    name?: string;
    surName?: string;
    email?: string;
    emailVerified?: boolean;
    phoneNumber?: string;
    phoneNumberVerified?: boolean;
    roles?: string[];
    sessionId?: string;
  };

  type DataDictionaryCreateDto = {
    displayText?: string;
    description?: string;
    items?: DataDictionaryItemDto[];
    code?: string;
    isStatic?: boolean;
  };

  type DataDictionaryDto = {
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    code?: string;
    displayText?: string;
    isStatic?: boolean;
    description?: string;
    items?: DataDictionaryItemDto[];
  };

  type DataDictionaryItemDto = {
    code?: string;
    displayText?: string;
    description?: string;
  };

  type DataDictionaryItemViewDto = {
    code?: string;
    displayText?: string;
    description?: string;
    /** 模块 DTO 不带这个字段，但实体上有——静态项在管理页禁止删除、禁止改编码。 */
    isStatic?: boolean;
    /** antd 标签色标识（DataDictionaryTagTypes），来自 AppDataDictionaryItemMetas。 */
    tagType?: string;
    /** 显示顺序（枚举字典 = 成员声明顺序）。不要按 Code 排序：数值字符串序下 "10" 排在 "2" 前。 */
    order?: number;
  };

  type DataDictionaryUpdateDto = {
    displayText?: string;
    description?: string;
    items?: DataDictionaryItemDto[];
  };

  type DataDictionaryViewDto = {
    code?: string;
    displayText?: string;
    description?: string;
    /** 静态字典由代码定义：不允许删除、不允许改编码，只允许改显示名。 */
    isStatic?: boolean;
    items?: DataDictionaryItemViewDto[];
  };

  type DataScopeDemoDto = {
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    tenantId?: string;
    organizationUnitId?: string;
    name?: string;
  };

  type DataScopeTypeEnum = 0 | 1 | 2 | 3 | 4;

  type DateTimeFormatDto = {
    calendarAlgorithmType?: string;
    dateTimeFormatLong?: string;
    shortDatePattern?: string;
    fullDateTimePattern?: string;
    dateSeparator?: string;
    shortTimePattern?: string;
    longTimePattern?: string;
  };

  type DeleteAccountInput = {
    /** 当前登录密码，用于二次确认。 */
    password: string;
  };

  type deleteApiAppAccountLinkIdParams = {
    id: string;
  };

  type deleteApiAppAccountPasskeyParams = {
    credentialId?: string;
  };

  type deleteApiAppAccountSecurityLoginParams = {
    LoginProvider: string;
    ProviderKey: string;
  };

  type deleteApiAppBackgroundJobIdParams = {
    id: string;
  };

  type deleteApiAppCacheMonitorKeyParams = {
    key?: string;
  };

  type deleteApiAppClaimTypeIdParams = {
    id: string;
  };

  type deleteApiAppDataScopeDemoIdParams = {
    id: string;
  };

  type deleteApiAppEditionIdParams = {
    id: string;
    /** 迁移目标版本。留空表示清空这些租户的版本分配（EditionId 设为 null）。 */
    MoveTenantsToEditionId?: string;
  };

  type deleteApiAppFileShareIdParams = {
    id: string;
  };

  type deleteApiAppIdentityUserDelegationIdParams = {
    id: string;
  };

  type deleteApiAppLanguageIdParams = {
    id: string;
  };

  type deleteApiAppMenuIdParams = {
    id: string;
  };

  type deleteApiAppOpenIddictApplicationIdParams = {
    id: string;
  };

  type deleteApiAppOpenIddictScopeIdParams = {
    id: string;
  };

  type deleteApiAppOrganizationUnitIdMemberUserIdParams = {
    id: string;
    userId: string;
  };

  type deleteApiAppOrganizationUnitIdParams = {
    id: string;
  };

  type deleteApiAppOrganizationUnitIdRoleRoleIdParams = {
    id: string;
    roleId: string;
  };

  type deleteApiAppPostIdMemberUserIdParams = {
    id: string;
    userId: string;
  };

  type deleteApiAppPostIdParams = {
    id: string;
  };

  type deleteApiAppRoleDataScopeIdParams = {
    id: string;
  };

  type deleteApiAppScheduledJobIdParams = {
    id: string;
  };

  type deleteApiAppTenantIdDefaultConnectionStringParams = {
    id: string;
  };

  type deleteApiAppTenantIdParams = {
    id: string;
  };

  type deleteApiAppTenantPackageIdParams = {
    id: string;
  };

  type deleteApiDataDictionaryDataDictionaryIdParams = {
    id: string;
  };

  type deleteApiFeatureManagementFeaturesParams = {
    providerName?: string;
    providerKey?: string;
  };

  type deleteApiFileManagementFileIdParams = {
    id: string;
  };

  type deleteApiIdentityRolesIdParams = {
    id: string;
  };

  type deleteApiIdentityUsersIdParams = {
    id: string;
  };

  type deleteApiMultiTenancyTenantsIdDefaultConnectionStringParams = {
    id: string;
  };

  type deleteApiMultiTenancyTenantsIdParams = {
    id: string;
  };

  type deleteApiPermissionManagementPermissionsResourceParams = {
    resourceName?: string;
    resourceKey?: string;
    providerName?: string;
    providerKey?: string;
  };

  type DisableAuthenticatorInput = {
    code: string;
  };

  type DiskInfoDto = {
    /** 分区名（Windows 盘符 C:\ 或 Linux 挂载点 /）。 */
    name?: string;
    /** 分区文件系统格式。 */
    driveFormat?: string;
    /** 总容量（字节）。 */
    totalBytes?: number;
    /** 剩余空间（字节）。 */
    freeBytes?: number;
  };

  type EditionDto = {
    id?: string;
    displayName?: string;
  };

  type EmailSettingsDto = {
    smtpHost?: string;
    smtpPort?: number;
    smtpUserName?: string;
    smtpPassword?: string;
    smtpDomain?: string;
    smtpEnableSsl?: boolean;
    smtpUseDefaultCredentials?: boolean;
    defaultFromAddress?: string;
    defaultFromDisplayName?: string;
  };

  type EnableAuthenticatorInput = {
    code: string;
  };

  type EnqueueTestBackgroundJobDto = {
    emailAddress: string;
    subject: string;
    body?: string;
  };

  type EntityChangeDto = {
    entityTypeFullName?: string;
    entityId?: string;
    changeType?: number;
    propertyChanges?: EntityPropertyChangeDto[];
  };

  type EntityChangeHistoryDto = {
    id?: string;
    auditLogId?: string;
    changeTime?: string;
    changeType?: number;
    entityTypeFullName?: string;
    entityId?: string;
    userName?: string;
    propertyChanges?: EntityPropertyChangeDto[];
  };

  type EntityExtensionDto = {
    properties?: Record<string, any>;
    configuration?: Record<string, any>;
  };

  type EntityPropertyChangeDto = {
    propertyName?: string;
    originalValue?: string;
    newValue?: string;
  };

  type ExtensionEnumDto = {
    fields?: ExtensionEnumFieldDto[];
    localizationResource?: string;
  };

  type ExtensionEnumFieldDto = {
    name?: string;
    value?: any;
  };

  type ExtensionPropertyApiCreateDto = {
    isAvailable?: boolean;
  };

  type ExtensionPropertyApiDto = {
    onGet?: ExtensionPropertyApiGetDto;
    onCreate?: ExtensionPropertyApiCreateDto;
    onUpdate?: ExtensionPropertyApiUpdateDto;
  };

  type ExtensionPropertyApiGetDto = {
    isAvailable?: boolean;
  };

  type ExtensionPropertyApiUpdateDto = {
    isAvailable?: boolean;
  };

  type ExtensionPropertyAttributeDto = {
    typeSimple?: string;
    config?: Record<string, any>;
  };

  type ExtensionPropertyDto = {
    type?: string;
    typeSimple?: string;
    displayName?: LocalizableStringDto;
    api?: ExtensionPropertyApiDto;
    ui?: ExtensionPropertyUiDto;
    policy?: ExtensionPropertyPolicyDto;
    attributes?: ExtensionPropertyAttributeDto[];
    configuration?: Record<string, any>;
    defaultValue?: any;
  };

  type ExtensionPropertyFeaturePolicyDto = {
    features?: string[];
    requiresAll?: boolean;
  };

  type ExtensionPropertyGlobalFeaturePolicyDto = {
    features?: string[];
    requiresAll?: boolean;
  };

  type ExtensionPropertyPermissionPolicyDto = {
    permissionNames?: string[];
    requiresAll?: boolean;
  };

  type ExtensionPropertyPolicyDto = {
    globalFeatures?: ExtensionPropertyGlobalFeaturePolicyDto;
    features?: ExtensionPropertyFeaturePolicyDto;
    permissions?: ExtensionPropertyPermissionPolicyDto;
  };

  type ExtensionPropertyUiDto = {
    onTable?: ExtensionPropertyUiTableDto;
    onCreateForm?: ExtensionPropertyUiFormDto;
    onEditForm?: ExtensionPropertyUiFormDto;
    lookup?: ExtensionPropertyUiLookupDto;
  };

  type ExtensionPropertyUiFormDto = {
    isVisible?: boolean;
  };

  type ExtensionPropertyUiLookupDto = {
    url?: string;
    resultListPropertyName?: string;
    displayPropertyName?: string;
    valuePropertyName?: string;
    filterParamName?: string;
  };

  type ExtensionPropertyUiTableDto = {
    isVisible?: boolean;
  };

  type FeatureDto = {
    name?: string;
    displayName?: string;
    value?: string;
    provider?: FeatureProviderDto;
    description?: string;
    valueType?: IStringValueType;
    depth?: number;
    parentName?: string;
  };

  type FeatureGroupDto = {
    name?: string;
    displayName?: string;
    features?: FeatureDto[];
  };

  type FeatureProviderDto = {
    name?: string;
    key?: string;
  };

  type FileContainerType = 0 | 1;

  type FileDownloadInfoModel = {
    extraProperties?: Record<string, any>;
    downloadMethod?: string;
    downloadUrl?: string;
    expectedFileName?: string;
    token?: string;
  };

  type FileDownloadOutput = {
    extraProperties?: Record<string, any>;
    fileName?: string;
    mimeType?: string;
    content?: string;
  };

  type FileInfoDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    parentId?: string;
    fileContainerName?: string;
    fileName?: string;
    mimeType?: string;
    fileType?: FileType;
    subFilesQuantity?: number;
    hasSubdirectories?: boolean;
    byteSize?: number;
    hash?: string;
    ownerUserId?: string;
    owner?: BriefFileUserInfoModel;
    creator?: BriefFileUserInfoModel;
    lastModifier?: BriefFileUserInfoModel;
  };

  type FileLocationDto = {
    id?: string;
    location?: FileLocationModel;
  };

  type FileLocationModel = {
    isDirectory?: boolean;
    parentPath?: string;
    fileName?: string;
    filePath?: string;
  };

  type FileShareLinkDto = {
    id?: string;
    fileId?: string;
    token?: string;
    expireTime?: string;
    isPublic?: boolean;
    downloadCount?: number;
    maxDownloads?: number;
    downloadUrl?: string;
  };

  type FileType = 1 | 2;

  type FindTenantResultDto = {
    success?: boolean;
    tenantId?: string;
    name?: string;
    normalizedName?: string;
    isActive?: boolean;
  };

  type GdprRequestDto = {
    id?: string;
    creationTime?: string;
    readyTime?: string;
  };

  type GenerateAccessTokenInput = {
    /** 必填：已存 secret 是哈希读不回来，必须由调用者输入。 */
    clientSecret: string;
    /** 请求的 scopes；每一个都必须已分配给该应用。 */
    scopes?: string[];
  };

  type GenerateAccessTokenResultDto = {
    accessToken?: string;
    tokenType?: string;
    expiresInSeconds?: number;
    grantedScopes?: string[];
  };

  type getApiAbpApiDefinitionParams = {
    IncludeTypes?: boolean;
    IncludeDescriptions?: boolean;
  };

  type getApiAbpApplicationConfigurationParams = {
    IncludeLocalizationResources?: boolean;
  };

  type getApiAbpApplicationLocalizationParams = {
    CultureName: string;
    OnlyDynamics?: boolean;
  };

  type getApiAbpMultiTenancyTenantsByIdIdParams = {
    id: string;
  };

  type getApiAbpMultiTenancyTenantsByNameNameParams = {
    name: string;
  };

  type getApiAppAuditLog_openAPI_exportParams = {
    StartTime?: string;
    EndTime?: string;
    HttpMethod?: string;
    Url?: string;
    UserName?: string;
    ApplicationName?: string;
    CorrelationId?: string;
    HttpStatusCode?: HttpStatusCode;
    HasException?: boolean;
    /** 仅看「未处理」的错误日志（HttpStatusCode≥400 或有异常），用于错误认领工作流。 */
    UnhandledErrorOnly?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppAuditLogAverageExecutionDurationPerDayParams = {
    StartTime?: string;
    EndTime?: string;
  };

  type getApiAppAuditLogEntityChangeHistoryParams = {
    EntityTypeFullName?: string;
    EntityId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppAuditLogErrorRateParams = {
    StartTime?: string;
    EndTime?: string;
  };

  type getApiAppAuditLogExportFileIdParams = {
    id: string;
  };

  type getApiAppAuditLogIdParams = {
    id: string;
  };

  type getApiAppAuditLogParams = {
    StartTime?: string;
    EndTime?: string;
    HttpMethod?: string;
    Url?: string;
    UserName?: string;
    ApplicationName?: string;
    CorrelationId?: string;
    HttpStatusCode?: HttpStatusCode;
    HasException?: boolean;
    /** 仅看「未处理」的错误日志（HttpStatusCode≥400 或有异常），用于错误认领工作流。 */
    UnhandledErrorOnly?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppBackgroundJobIdParams = {
    id: string;
  };

  type getApiAppBackgroundJobParams = {
    JobName?: string;
    IsAbandoned?: boolean;
    HasCompleted?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppCacheMonitorKeysParams = {
    prefix?: string;
    cursor?: number;
    maxResultCount?: number;
  };

  type getApiAppCacheMonitorValueParams = {
    key?: string;
  };

  type getApiAppClaimTypeParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppDataDictionaryViewCodeParams = {
    code: string;
  };

  type getApiAppDataScopeDemoParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppEditionIdTenantCountParams = {
    id: string;
  };

  type getApiAppEditionParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppFileShareByTokenTokenParams = {
    token: string;
  };

  type getApiAppFileShareParams = {
    fileId?: string;
  };

  type getApiAppFileThumbnailIdParams = {
    id: string;
  };

  type getApiAppFileThumbnailWithThumbnailsParams = {
    ParentId?: string;
    FileContainerName: string;
    OwnerUserId?: string;
    DirectoryOnly?: boolean;
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppGdprRequestDownloadParams = {
    requestId?: string;
    token?: string;
  };

  type getApiAppGdprRequestDownloadTokenRequestIdParams = {
    requestId: string;
  };

  type getApiAppGdprRequestParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppIdentityClaimRoleClaimsRoleIdParams = {
    roleId: string;
  };

  type getApiAppIdentityClaimUserClaimsUserIdParams = {
    userId: string;
  };

  type getApiAppIdentitySessionParams = {
    UserId?: string;
    Device?: string;
    ClientId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppIdentityUserAdmin_openAPI_exportParams = {
    filter?: string;
  };

  type getApiAppIdentityUserAdminExportFileIdParams = {
    id: string;
  };

  type getApiAppIdentityUserAdminImportFailureReportIdParams = {
    id: string;
  };

  type getApiAppIdentityUserAdminTwoFactorStatusesParams = {
    UserIds?: string[];
  };

  type getApiAppLanguageIdParams = {
    id: string;
  };

  type getApiAppLanguageTextParams = {
    ResourceName?: string;
    CultureName?: string;
    BaseCultureName?: string;
    Filter?: string;
    OnlyEmpty?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppMenuIdParams = {
    id: string;
  };

  type getApiAppMenuRoleGrantsMenuIdParams = {
    menuId: string;
  };

  type getApiAppMyNotificationParams = {
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppNotificationManagementIdBroadcastParams = {
    id: string;
  };

  type getApiAppNotificationManagementIdParams = {
    id: string;
  };

  type getApiAppNotificationManagementParams = {
    NotificationMethod?: string;
    Success?: boolean;
    /** 只看待发送（Success == null）。与 Success 二选一（Success 优先）。 */
    PendingOnly?: boolean;
    UserName?: string;
    CreationTimeStart?: string;
    CreationTimeEnd?: string;
    /** 是否包含重试记录（RetryForNotificationId != null 的行）。默认 false——
            列表默认只显示原始通知，避免重试记录把列表刷满（规格第 12 步）。 */
    IncludeRetries?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOpenIddictApplicationIdParams = {
    id: string;
  };

  type getApiAppOpenIddictApplicationIdTokenLifetimeParams = {
    id: string;
  };

  type getApiAppOpenIddictApplicationParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOpenIddictScopeParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOpenIddictTokenAdminAuthorizationsParams = {
    Subject?: string;
    ApplicationId?: string;
    Status?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOpenIddictTokenAdminTokensParams = {
    /** 用户 Id（subject）。 */
    Subject?: string;
    /** 应用 Id。 */
    ApplicationId?: string;
    /** 令牌类型：access_token / refresh_token / ...；空=全部。 */
    Type?: string;
    /** 状态：valid / revoked / redeemed / expired；空=全部。 */
    Status?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOperationLogParams = {
    /** 模糊过滤：匹配 SubType / Action / BizId / UserName。 */
    Filter?: string;
    Type?: string;
    SubType?: string;
    Success?: boolean;
    UserId?: string;
    /** 按关联 ID 精确过滤（与审计日志的 CorrelationId 筛选同口径）。 */
    CorrelationId?: string;
    StartTime?: string;
    EndTime?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOrganizationUnitIdMembersParams = {
    id: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppOrganizationUnitIdRolesParams = {
    id: string;
  };

  type getApiAppPostIdMembersParams = {
    id: string;
    Filter?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppPostParams = {
    /** 按名称/编码模糊过滤。 */
    Filter?: string;
    /** 按状态过滤；null 为全部。 */
    Status?: PostStatusEnum;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppPostPostsByUserUserIdParams = {
    userId: string;
  };

  type getApiAppProfileAvatarIdParams = {
    id: string;
  };

  type getApiAppRoleDataScopeByRoleNameParams = {
    roleName?: string;
  };

  type getApiAppRoleDataScopeIdParams = {
    id: string;
  };

  type getApiAppRoleDataScopeParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppScheduledJobIdExecutionsParams = {
    id: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppScheduledJobIdParams = {
    id: string;
  };

  type getApiAppScheduledJobParams = {
    Name?: string;
    JobType?: string;
    IsEnabled?: boolean;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppSecurityLogParams = {
    StartTime?: string;
    EndTime?: string;
    ApplicationName?: string;
    Identity?: string;
    Action?: string;
    UserName?: string;
    ClientId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppTenantIdConnectionStringsParams = {
    id: string;
  };

  type getApiAppTenantIdDefaultConnectionStringParams = {
    id: string;
  };

  type getApiAppTenantIdParams = {
    id: string;
  };

  type getApiAppTenantPackageIdMenuSelectionParams = {
    id: string;
  };

  type getApiAppTenantPackageIdParams = {
    id: string;
  };

  type getApiAppTenantPackageParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppTenantParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiAppTextTemplateContentParams = {
    name?: string;
    cultureName?: string;
  };

  type getApiAppTextTemplateParams = {
    filter?: string;
  };

  type getApiAppVirtualFileExplorerContentParams = {
    Path?: string;
  };

  type getApiAppVirtualFileExplorerParams = {
    Path?: string;
  };

  type getApiDataDictionaryDataDictionaryByCodeCodeParams = {
    code: string;
  };

  type getApiDataDictionaryDataDictionaryIdParams = {
    id: string;
  };

  type getApiDataDictionaryDataDictionaryParams = {
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiFeatureManagementFeaturesParams = {
    providerName?: string;
    providerKey?: string;
  };

  type getApiFileManagementFileByPathParams = {
    path?: string;
    fileContainerName?: string;
    ownerUserId?: string;
  };

  type getApiFileManagementFileConfigurationParams = {
    fileContainerName?: string;
    ownerUserId?: string;
  };

  type getApiFileManagementFileIdDownloadInfoParams = {
    id: string;
  };

  type getApiFileManagementFileIdDownloadParams = {
    id: string;
    token?: string;
  };

  type getApiFileManagementFileIdDownloadWithBytesParams = {
    id: string;
    token?: string;
  };

  type getApiFileManagementFileIdDownloadWithStreamParams = {
    id: string;
    token?: string;
  };

  type getApiFileManagementFileIdParams = {
    id: string;
  };

  type getApiFileManagementFileLocationParams = {
    id?: string;
  };

  type getApiFileManagementFileParams = {
    ParentId?: string;
    FileContainerName: string;
    OwnerUserId?: string;
    DirectoryOnly?: boolean;
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiIdentityRolesIdParams = {
    id: string;
  };

  type getApiIdentityRolesParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
    ExtraProperties?: Record<string, any>;
  };

  type getApiIdentityUsersByEmailEmailParams = {
    email: string;
  };

  type getApiIdentityUsersByIdIdParams = {
    id: string;
  };

  type getApiIdentityUsersByUsernameUserNameParams = {
    userName: string;
  };

  type getApiIdentityUsersIdParams = {
    id: string;
  };

  type getApiIdentityUsersIdRolesParams = {
    id: string;
  };

  type getApiIdentityUsersLookupByUsernameUserNameParams = {
    userName: string;
  };

  type getApiIdentityUsersLookupCountParams = {
    Filter?: string;
  };

  type getApiIdentityUsersLookupIdParams = {
    id: string;
  };

  type getApiIdentityUsersLookupSearchParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
    ExtraProperties?: Record<string, any>;
  };

  type getApiIdentityUsersParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
    ExtraProperties?: Record<string, any>;
  };

  type getApiMultiTenancyTenantsIdDefaultConnectionStringParams = {
    id: string;
  };

  type getApiMultiTenancyTenantsIdParams = {
    id: string;
  };

  type getApiMultiTenancyTenantsParams = {
    Filter?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiNotificationServiceNotificationIdParams = {
    id: string;
  };

  type getApiNotificationServiceNotificationInfoIdParams = {
    id: string;
  };

  type getApiNotificationServiceNotificationInfoParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiNotificationServiceNotificationParams = {
    UserId?: string;
    UserName?: string;
    NotificationInfoId?: string;
    NotificationMethod?: string;
    Success?: boolean;
    CreationTime?: string;
    CreatorId?: string;
    CompletionTime?: string;
    FailureReason?: string;
    RetryForNotificationId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServicePaymentIdParams = {
    id: string;
  };

  type getApiPaymentServicePaymentParams = {
    UserId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServicePrepaymentAccountIdParams = {
    id: string;
  };

  type getApiPaymentServicePrepaymentAccountParams = {
    UserId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServicePrepaymentTransactionIdParams = {
    id: string;
  };

  type getApiPaymentServicePrepaymentTransactionParams = {
    AccountId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServicePrepaymentWithdrawalRecordIdParams = {
    id: string;
  };

  type getApiPaymentServicePrepaymentWithdrawalRecordParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServicePrepaymentWithdrawalRequestIdParams = {
    id: string;
  };

  type getApiPaymentServicePrepaymentWithdrawalRequestParams = {
    PendingOnly?: boolean;
    AccountId?: string;
    AccountUserId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServiceRefundIdParams = {
    id: string;
  };

  type getApiPaymentServiceRefundParams = {
    PaymentId?: string;
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServiceWechatPayPaymentRecordIdParams = {
    id: string;
  };

  type getApiPaymentServiceWechatPayPaymentRecordParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPaymentServiceWechatPayRefundRecordIdParams = {
    id: string;
  };

  type getApiPaymentServiceWechatPayRefundRecordParams = {
    Sorting?: string;
    SkipCount?: number;
    MaxResultCount?: number;
  };

  type getApiPermissionManagementPermissionsByGroupParams = {
    groupName?: string;
    providerName?: string;
    providerKey?: string;
  };

  type getApiPermissionManagementPermissionsParams = {
    providerName?: string;
    providerKey?: string;
  };

  type getApiPermissionManagementPermissionsResourceByProviderParams = {
    resourceName?: string;
    resourceKey?: string;
    providerName?: string;
    providerKey?: string;
  };

  type getApiPermissionManagementPermissionsResourceDefinitionsParams = {
    resourceName?: string;
  };

  type getApiPermissionManagementPermissionsResourceParams = {
    resourceName?: string;
    resourceKey?: string;
  };

  type getApiPermissionManagementPermissionsResourceProviderKeyLookupServicesParams =
    {
      resourceName?: string;
    };

  type getApiPermissionManagementPermissionsSearchResourceProviderKeysParams = {
    resourceName?: string;
    serviceName?: string;
    filter?: string;
    page?: number;
  };

  type GetAuditLogListInput = {
    maxResultCount?: number;
    skipCount?: number;
    sorting?: string;
    startTime?: string;
    endTime?: string;
    httpMethod?: string;
    url?: string;
    userName?: string;
    applicationName?: string;
    correlationId?: string;
    httpStatusCode?: HttpStatusCode;
    hasException?: boolean;
    /** 仅看「未处理」的错误日志（HttpStatusCode≥400 或有异常），用于错误认领工作流。 */
    unhandledErrorOnly?: boolean;
  };

  type GetFeatureListResultDto = {
    groups?: FeatureGroupDto[];
  };

  type GetFileByPathOutputDto = {
    found?: boolean;
    fileInfo?: FileInfoDto;
  };

  type GetFileListWithThumbnailsOutput = {
    items?: FileInfoDto[];
    totalCount?: number;
    /** FileId → 缩略图 URL（/api/app/file-thumbnail/{fileId}）。 */
    thumbnailUrls?: Record<string, any>;
  };

  type GetPermissionListResultDto = {
    entityDisplayName?: string;
    groups?: PermissionGroupDto[];
  };

  type GetResourcePermissionDefinitionListResultDto = {
    permissions?: ResourcePermissionDefinitionDto[];
  };

  type GetResourcePermissionListResultDto = {
    permissions?: ResourcePermissionGrantInfoDto[];
  };

  type GetResourcePermissionWithProviderListResultDto = {
    permissions?: ResourcePermissionWithProdiverGrantInfoDto[];
  };

  type GetResourceProviderListResultDto = {
    providers?: ResourceProviderDto[];
  };

  type getWechatPayJsSdkConfigParametersParams = {
    mchId?: string;
    appId?: string;
    prepayId?: string;
  };

  type GrantedResourcePermissionDto = {
    name?: string;
    displayName?: string;
  };

  type HttpStatusCode =
    | 100
    | 101
    | 102
    | 103
    | 200
    | 201
    | 202
    | 203
    | 204
    | 205
    | 206
    | 207
    | 208
    | 226
    | 300
    | 301
    | 302
    | 303
    | 304
    | 305
    | 306
    | 307
    | 308
    | 400
    | 401
    | 402
    | 403
    | 404
    | 405
    | 406
    | 407
    | 408
    | 409
    | 410
    | 411
    | 412
    | 413
    | 414
    | 415
    | 416
    | 417
    | 421
    | 422
    | 423
    | 424
    | 426
    | 428
    | 429
    | 431
    | 451
    | 500
    | 501
    | 502
    | 503
    | 504
    | 505
    | 506
    | 507
    | 508
    | 510
    | 511;

  type IanaTimeZone = {
    timeZoneName?: string;
  };

  type IdentityClaimValueType = 0 | 1 | 2 | 3;

  type IdentityRoleCreateDto = {
    extraProperties?: Record<string, any>;
    name: string;
    isDefault?: boolean;
    isPublic?: boolean;
  };

  type IdentityRoleDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    name?: string;
    isDefault?: boolean;
    isStatic?: boolean;
    isPublic?: boolean;
    concurrencyStamp?: string;
    creationTime?: string;
  };

  type IdentityRoleUpdateDto = {
    extraProperties?: Record<string, any>;
    name: string;
    isDefault?: boolean;
    isPublic?: boolean;
    concurrencyStamp?: string;
  };

  type IdentitySessionDto = {
    id?: string;
    sessionId?: string;
    userId?: string;
    device?: string;
    deviceInfo?: string;
    clientId?: string;
    ipAddresses?: string;
    signedIn?: string;
    lastAccessed?: string;
  };

  type IdentityUserCreateDto = {
    extraProperties?: Record<string, any>;
    userName: string;
    name?: string;
    surname?: string;
    email: string;
    phoneNumber?: string;
    isActive?: boolean;
    lockoutEnabled?: boolean;
    roleNames?: string[];
    password: string;
  };

  type IdentityUserDelegationDto = {
    id?: string;
    sourceUserId?: string;
    targetUserId?: string;
    sourceUserName?: string;
    targetUserName?: string;
    startTime?: string;
    endTime?: string;
    isActive?: boolean;
  };

  type IdentityUserDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    tenantId?: string;
    userName?: string;
    name?: string;
    surname?: string;
    email?: string;
    emailConfirmed?: boolean;
    phoneNumber?: string;
    phoneNumberConfirmed?: boolean;
    isActive?: boolean;
    lockoutEnabled?: boolean;
    accessFailedCount?: number;
    lockoutEnd?: string;
    concurrencyStamp?: string;
    entityVersion?: number;
    lastPasswordChangeTime?: string;
  };

  type IdentityUserUpdateDto = {
    extraProperties?: Record<string, any>;
    userName: string;
    name?: string;
    surname?: string;
    email: string;
    phoneNumber?: string;
    isActive?: boolean;
    lockoutEnabled?: boolean;
    roleNames?: string[];
    password?: string;
    concurrencyStamp?: string;
  };

  type IdentityUserUpdateRolesDto = {
    roleNames: string[];
  };

  type ImpersonateTenantInput = {
    tenantId?: string;
  };

  type ImpersonateUserInput = {
    userId?: string;
  };

  type ImpersonationResultDto = {
    accessToken?: string;
    tokenType?: string;
    expiresIn?: number;
    refreshToken?: string;
  };

  type InterfaceMethodApiDescriptionModel = {
    name?: string;
    parametersOnMethod?: MethodParameterApiDescriptionModel[];
    returnValue?: ReturnValueApiDescriptionModel;
  };

  type IStringValueType = {
    name?: string;
    properties?: Record<string, any>;
    validator?: IValueValidator;
  };

  type IValueValidator = {
    name?: string;
    properties?: Record<string, any>;
  };

  type LanguageDto = {
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    cultureName?: string;
    uiCultureName?: string;
    displayName?: string;
    flagIcon?: string;
    isEnabled?: boolean;
    isDefault?: boolean;
  };

  type LanguageInfo = {
    cultureName?: string;
    uiCultureName?: string;
    displayName?: string;
    twoLetterISOLanguageName?: string;
  };

  type LanguageTextDto = {
    /** 覆盖行的实体 Id；静态基线行（无覆盖）为 null。 */
    id?: string;
    resourceName?: string;
    cultureName?: string;
    name?: string;
    /** 目标文化生效值（合并口径）：有生效覆盖（本层或 host——host 覆盖对租户生效）取覆盖值，
（空串=显式未翻译），否则取静态基线值，两者都没有为空串。
静态基线沿目标文化父链展开，刻意不含框架 DefaultCulture 回退（与运行时管线的唯一差异，
见 GetListAsync / StaticLocalizationTextProvider——链上缺失的 key 显示空串以便漏译可见）；
与 IsOverridden（仅本层）刻意区分，见该属性注释。 */
    value?: string;
    /** 基准文化对照值（含父文化回退），对应 Pro 语言文本页的 BaseCulture 列。
与 Value 同一合并口径（本层覆盖 > host 覆盖 > 静态基线）——即基准文化的生效值，
基准文化被覆盖时对照列不失真。与目标列的唯一差异：基准列的静态回退链并入
资源默认文化（Pro 同款），目标列不并入。
BaseCultureName 未传、key 不在基准文化里时为 null（前端显示为空）。 */
    baseValue?: string;
    /** 当前上下文（host 或本租户）是否存在数据库覆盖行。
「恢复默认」只删除当前上下文那一层覆盖（RestoreToDefaultAsync / Pro 的 per-context 语义），
前端据此决定按钮是否可点：只有本字段为 true 时，恢复才会真正删除覆盖行。
注意两点边界：①与 Value 的口径区分——Value 是合并口径（本层覆盖 > host 覆盖 > 静态 json），
租户看到 host 覆盖的值时本字段为 false；②本字段为 true 时恢复删行必然发生，
但若覆盖值恰好等于下层提供值，显示值可能不变（存储变化 ≠ 显示变化）。 */
    isOverridden?: boolean;
  };

  type LinkAccountInput = {
    userNameOrEmail: string;
    password: string;
  };

  type LinkedAccountDto = {
    /** 关联记录 Id（UnlinkAsync / SwitchAsync 的入参）。 */
    linkId?: string;
    /** 对端账号 Id。 */
    userId?: string;
    userName?: string;
    emailAddress?: string;
    tenantId?: string;
  };

  type ListResultDto1EditionDto = {
    items?: EditionDto[];
  };

  type ListResultDto1FileShareLinkDto = {
    items?: FileShareLinkDto[];
  };

  type ListResultDto1IdentityRoleDto = {
    items?: IdentityRoleDto[];
  };

  type ListResultDto1LanguageDto = {
    items?: LanguageDto[];
  };

  type ListResultDto1MenuTreeDto = {
    items?: MenuTreeDto[];
  };

  type ListResultDto1MyMenuItemDto = {
    items?: MyMenuItemDto[];
  };

  type ListResultDto1OpenIddictScopeLookupDto = {
    items?: OpenIddictScopeLookupDto[];
  };

  type ListResultDto1OrganizationUnitRoleDto = {
    items?: OrganizationUnitRoleDto[];
  };

  type ListResultDto1PaymentMethodDto = {
    items?: PaymentMethodDto[];
  };

  type ListResultDto1PermissionOptionDto = {
    items?: PermissionOptionDto[];
  };

  type ListResultDto1PostDto = {
    items?: PostDto[];
  };

  type ListResultDto1ScheduledJobTypeDto = {
    items?: ScheduledJobTypeDto[];
  };

  type ListResultDto1String = {
    items?: string[];
  };

  type ListResultDto1UserData = {
    items?: UserData[];
  };

  type ListResultDto1UserPasskeyDto = {
    items?: UserPasskeyDto[];
  };

  type LocalizableStringDto = {
    name?: string;
    resource?: string;
  };

  type LoginResultType = 1 | 2 | 3 | 4 | 5;

  type LoginWithMagicLinkInput = {
    email: string;
    /** 邮件链接携带的一次性 magic link token，与 Code 二选一（缺省校验在服务端方法内，
            复用 InvalidMagicLink 错误码，避免 Contracts 层硬编码校验文案绕开本地化）。 */
    magicLinkToken?: string;
    /** 邮件正文中的 6 位验证码，与 MagicLinkToken 二选一（链接过期/换设备时的兜底路径）。 */
    code?: string;
    /** 链接自带的租户名（host 邮件为空）。服务端据此建立租户上下文，链接自包含。 */
    tenantName?: string;
  };

  type MarkAuditLogHandledInput = {
    /** 处置备注（结论/根因/工单号等，可选）。
DTO 层注解把超长输入转成 400（AppService 里 Check.Length 兜底抛的是 500 级异常，
兜的是绕过 DTO 注解的直接服务调用）。 */
    note?: string;
  };

  type MenuCreateDto = {
    parentId?: string;
    type?: MenuTypeEnum;
    title: string;
    name?: string;
    path?: string;
    icon?: string;
    /** 排序值（前端限制 0-9999）。 */
    orderNo?: number;
    isHide?: boolean;
    isEnabled?: boolean;
    permissionName?: string;
    remark?: string;
  };

  type MenuDto = {
    id?: string;
    tenantId?: string;
    parentId?: string;
    type?: MenuTypeEnum;
    /** 默认显示名。 */
    title?: string;
    /** 国际化 key 尾段，可空。 */
    name?: string;
    path?: string;
    icon?: string;
    orderNo?: number;
    isHide?: boolean;
    isEnabled?: boolean;
    permissionName?: string;
    remark?: string;
    /** 并发戳（乐观并发）：编辑表单携带加载时的值回传，服务端与库内当前值不一致即 409。
实体上有该列（FullAuditedAggregateRoot），此前未下发导致编辑冲突被静默覆盖。 */
    concurrencyStamp?: string;
  };

  type MenuTreeDto = {
    id?: string;
    tenantId?: string;
    parentId?: string;
    type?: MenuTypeEnum;
    /** 默认显示名。 */
    title?: string;
    /** 国际化 key 尾段，可空。 */
    name?: string;
    path?: string;
    icon?: string;
    orderNo?: number;
    isHide?: boolean;
    isEnabled?: boolean;
    permissionName?: string;
    remark?: string;
    /** 并发戳（乐观并发）：编辑表单携带加载时的值回传，服务端与库内当前值不一致即 409。
实体上有该列（FullAuditedAggregateRoot），此前未下发导致编辑冲突被静默覆盖。 */
    concurrencyStamp?: string;
    grantedRoles?: string[];
    children?: MenuTreeDto[];
  };

  type MenuTypeEnum = 1 | 2;

  type MenuUpdateDto = {
    parentId?: string;
    type?: MenuTypeEnum;
    title: string;
    name?: string;
    path?: string;
    icon?: string;
    /** 排序值（前端限制 0-9999）。 */
    orderNo?: number;
    isHide?: boolean;
    isEnabled?: boolean;
    permissionName?: string;
    remark?: string;
    /** 并发戳：传了就校验（与库内当前值不一致抛 AbpDbConcurrencyException→409），不传跳过（兼容旧调用方）。 */
    concurrencyStamp?: string;
  };

  type MethodParameterApiDescriptionModel = {
    name?: string;
    typeAsString?: string;
    type?: string;
    typeSimple?: string;
    isOptional?: boolean;
    defaultValue?: any;
    summary?: string;
    description?: string;
    displayName?: string;
  };

  type ModuleApiDescriptionModel = {
    rootPath?: string;
    remoteServiceName?: string;
    controllers?: Record<string, any>;
  };

  type ModuleExtensionDto = {
    entities?: Record<string, any>;
    configuration?: Record<string, any>;
  };

  type MoveFileInput = {
    extraProperties?: Record<string, any>;
    newParentId?: string;
    newFileName: string;
  };

  type MoveOrganizationUnitInput = {
    parentId?: string;
  };

  type MultiTenancyInfoDto = {
    isEnabled?: boolean;
    userSharingStrategy?: TenantUserSharingStrategy;
  };

  type MyMenuItemDto = {
    title?: string;
    name?: string;
    path?: string;
    icon?: string;
    children?: MyMenuItemDto[];
  };

  type MyNotificationDto = {
    id?: string;
    title?: string;
    body?: string;
    creationTime?: string;
    /** CreationTime <= 上次"全部已读"时间戳。 */
    isRead?: boolean;
  };

  type NameValue = {
    name?: string;
    value?: string;
  };

  type NotificationAttemptDto = {
    id?: string;
    success?: boolean;
    completionTime?: string;
    failureReason?: string;
    creationTime?: string;
  };

  type NotificationBroadcastDto = {
    id?: string;
    targetType?: string;
    targetId?: string;
    notificationMethods?: string;
    title?: string;
    body?: string;
    totalCount?: number;
    sentCount?: number;
    failedCount?: number;
    state?: string;
    creationTime?: string;
    completionTime?: string;
  };

  type NotificationDetailDto = {
    id?: string;
    userId?: string;
    userName?: string;
    notificationInfoId?: string;
    notificationMethod?: string;
    /** null = 待发送。 */
    success?: boolean;
    completionTime?: string;
    failureReason?: string;
    creationTime?: string;
    /** 该原始通知的重试次数。 */
    retryCount?: number;
    /** 最终状态：有重试时取最后一次尝试的 Success，否则取本行的 Success。 */
    finalSuccess?: boolean;
    retryForNotificationId?: string;
    /** NotificationInfo 的 ExtraProperties（标题/正文等都在里面——模块没有结构化正文列）。 */
    notificationInfoProperties?: Record<string, any>;
    /** 完整尝试链（含本记录；原记录在前，重试按时间升序）。 */
    attempts?: NotificationAttemptDto[];
  };

  type NotificationDto = {
    id?: string;
    creationTime?: string;
    creatorId?: string;
    userId?: string;
    userName?: string;
    notificationInfoId?: string;
    notificationMethod?: string;
    success?: boolean;
    completionTime?: string;
    failureReason?: string;
    retryForNotificationId?: string;
  };

  type NotificationInfoDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
  };

  type NotificationListItemDto = {
    id?: string;
    userId?: string;
    userName?: string;
    notificationInfoId?: string;
    notificationMethod?: string;
    /** null = 待发送。 */
    success?: boolean;
    completionTime?: string;
    failureReason?: string;
    creationTime?: string;
    /** 该原始通知的重试次数。 */
    retryCount?: number;
    /** 最终状态：有重试时取最后一次尝试的 Success，否则取本行的 Success。 */
    finalSuccess?: boolean;
  };

  type ObjectExtensionsDto = {
    modules?: Record<string, any>;
    enums?: Record<string, any>;
  };

  type OpenIddictApplicationDto = {
    id?: string;
    clientId?: string;
    displayName?: string;
    clientType?: string;
    applicationType?: string;
    consentType?: string;
    clientUri?: string;
    logoUri?: string;
    redirectUris?: string;
    postLogoutRedirectUris?: string;
    frontChannelLogoutUri?: string;
    allowAuthorizationCodeFlow?: boolean;
    allowImplicitFlow?: boolean;
    allowHybridFlow?: boolean;
    allowPasswordFlow?: boolean;
    allowClientCredentialsFlow?: boolean;
    allowRefreshTokenFlow?: boolean;
    allowTokenExchangeFlow?: boolean;
    allowDeviceAuthorizationFlow?: boolean;
    enableEndSessionEndpoint?: boolean;
    enablePushedAuthorizationEndpoint?: boolean;
    requirePkce?: boolean;
    requirePushedAuthorization?: boolean;
    /** 已授予的 scope 权限（scp: 前缀已剥离）。 */
    scopes?: string[];
    /** 已授予的 extension grant types（gt: 前缀已剥离，不含 8 种标准 flow）。 */
    extensionGrantTypes?: string[];
  };

  type OpenIddictApplicationTokenLifetimeDto = {
    accessTokenLifetime?: number;
    authorizationCodeLifetime?: number;
    deviceCodeLifetime?: number;
    identityTokenLifetime?: number;
    refreshTokenLifetime?: number;
    userCodeLifetime?: number;
    /** Pushed Authorization Request 的 request token 生命周期。 */
    requestTokenLifetime?: number;
    /** token exchange 等场景签发的「其它」token 生命周期。 */
    issuedTokenLifetime?: number;
  };

  type OpenIddictAuthorizationDto = {
    id?: string;
    applicationId?: string;
    applicationClientId?: string;
    subject?: string;
    status?: string;
    type?: string;
    /** 授权的 scopes，逗号分隔。 */
    scopes?: string;
    creationDate?: string;
  };

  type OpenIddictPruneResultDto = {
    prunedTokens?: number;
    prunedAuthorizations?: number;
  };

  type OpenIddictScopeDto = {
    id?: string;
    name?: string;
    displayName?: string;
    description?: string;
    resources?: string;
  };

  type OpenIddictScopeLookupDto = {
    name?: string;
    displayName?: string;
    /** 内置 scope 不是数据库记录，不可编辑/删除。 */
    isBuiltIn?: boolean;
  };

  type OpenIddictTokenDto = {
    id?: string;
    applicationId?: string;
    /** 应用 ClientId（冗余便于展示）。 */
    applicationClientId?: string;
    authorizationId?: string;
    subject?: string;
    referenceId?: string;
    status?: string;
    type?: string;
    creationDate?: string;
    expirationDate?: string;
    redemptionDate?: string;
  };

  type OperationLogDto = {
    id?: string;
    userId?: string;
    userName?: string;
    type?: string;
    subType?: string;
    bizId?: string;
    action?: string;
    extra?: string;
    success?: boolean;
    errorMessage?: string;
    requestMethod?: string;
    requestUrl?: string;
    clientIpAddress?: string;
    /** IP 归属地（展示字段，由应用服务按当页 IP 批量解析后填充）。 */
    ipLocation?: string;
    userAgent?: string;
    /** 请求关联 ID（与审计日志/安全日志同源，可互相串联）。 */
    correlationId?: string;
    duration?: number;
    executionTime?: string;
  };

  type OrganizationUnitDto = {
    id?: string;
    parentId?: string;
    code?: string;
    displayName?: string;
  };

  type OrganizationUnitRoleDto = {
    id?: string;
    name?: string;
  };

  type OrganizationUnitUserDto = {
    id?: string;
    userName?: string;
    email?: string;
  };

  type PagedResultDto1AccountDto = {
    items?: AccountDto[];
    totalCount?: number;
  };

  type PagedResultDto1AuditLogDto = {
    items?: AuditLogDto[];
    totalCount?: number;
  };

  type PagedResultDto1BackgroundJobDto = {
    items?: BackgroundJobDto[];
    totalCount?: number;
  };

  type PagedResultDto1ClaimTypeDto = {
    items?: ClaimTypeDto[];
    totalCount?: number;
  };

  type PagedResultDto1DataDictionaryDto = {
    items?: DataDictionaryDto[];
    totalCount?: number;
  };

  type PagedResultDto1DataScopeDemoDto = {
    items?: DataScopeDemoDto[];
    totalCount?: number;
  };

  type PagedResultDto1EditionDto = {
    items?: EditionDto[];
    totalCount?: number;
  };

  type PagedResultDto1EntityChangeHistoryDto = {
    items?: EntityChangeHistoryDto[];
    totalCount?: number;
  };

  type PagedResultDto1FileInfoDto = {
    items?: FileInfoDto[];
    totalCount?: number;
  };

  type PagedResultDto1GdprRequestDto = {
    items?: GdprRequestDto[];
    totalCount?: number;
  };

  type PagedResultDto1IdentityRoleDto = {
    items?: IdentityRoleDto[];
    totalCount?: number;
  };

  type PagedResultDto1IdentitySessionDto = {
    items?: IdentitySessionDto[];
    totalCount?: number;
  };

  type PagedResultDto1IdentityUserDto = {
    items?: IdentityUserDto[];
    totalCount?: number;
  };

  type PagedResultDto1LanguageTextDto = {
    items?: LanguageTextDto[];
    totalCount?: number;
  };

  type PagedResultDto1MyNotificationDto = {
    items?: MyNotificationDto[];
    totalCount?: number;
  };

  type PagedResultDto1NotificationDto = {
    items?: NotificationDto[];
    totalCount?: number;
  };

  type PagedResultDto1NotificationInfoDto = {
    items?: NotificationInfoDto[];
    totalCount?: number;
  };

  type PagedResultDto1NotificationListItemDto = {
    items?: NotificationListItemDto[];
    totalCount?: number;
  };

  type PagedResultDto1OpenIddictApplicationDto = {
    items?: OpenIddictApplicationDto[];
    totalCount?: number;
  };

  type PagedResultDto1OpenIddictAuthorizationDto = {
    items?: OpenIddictAuthorizationDto[];
    totalCount?: number;
  };

  type PagedResultDto1OpenIddictScopeDto = {
    items?: OpenIddictScopeDto[];
    totalCount?: number;
  };

  type PagedResultDto1OpenIddictTokenDto = {
    items?: OpenIddictTokenDto[];
    totalCount?: number;
  };

  type PagedResultDto1OperationLogDto = {
    items?: OperationLogDto[];
    totalCount?: number;
  };

  type PagedResultDto1OrganizationUnitUserDto = {
    items?: OrganizationUnitUserDto[];
    totalCount?: number;
  };

  type PagedResultDto1PaymentDto = {
    items?: PaymentDto[];
    totalCount?: number;
  };

  type PagedResultDto1PaymentRecordDto = {
    items?: PaymentRecordDto[];
    totalCount?: number;
  };

  type PagedResultDto1PostDto = {
    items?: PostDto[];
    totalCount?: number;
  };

  type PagedResultDto1PostUserDto = {
    items?: PostUserDto[];
    totalCount?: number;
  };

  type PagedResultDto1RefundDto = {
    items?: RefundDto[];
    totalCount?: number;
  };

  type PagedResultDto1RefundRecordDto = {
    items?: RefundRecordDto[];
    totalCount?: number;
  };

  type PagedResultDto1RoleDataScopeDto = {
    items?: RoleDataScopeDto[];
    totalCount?: number;
  };

  type PagedResultDto1ScheduledJobDto = {
    items?: ScheduledJobDto[];
    totalCount?: number;
  };

  type PagedResultDto1ScheduledJobExecutionDto = {
    items?: ScheduledJobExecutionDto[];
    totalCount?: number;
  };

  type PagedResultDto1SecurityLogDto = {
    items?: SecurityLogDto[];
    totalCount?: number;
  };

  type PagedResultDto1TenantDto = {
    items?: TenantDto[];
    totalCount?: number;
  };

  type PagedResultDto1TenantPackageDto = {
    items?: TenantPackageDto[];
    totalCount?: number;
  };

  type PagedResultDto1TransactionDto = {
    items?: TransactionDto[];
    totalCount?: number;
  };

  type PagedResultDto1WithdrawalRecordDto = {
    items?: WithdrawalRecordDto[];
    totalCount?: number;
  };

  type PagedResultDto1WithdrawalRequestDto = {
    items?: WithdrawalRequestDto[];
    totalCount?: number;
  };

  type ParameterApiDescriptionModel = {
    nameOnMethod?: string;
    name?: string;
    jsonName?: string;
    type?: string;
    typeSimple?: string;
    isOptional?: boolean;
    defaultValue?: any;
    constraintTypes?: string[];
    bindingSourceId?: string;
    descriptorName?: string;
    summary?: string;
    description?: string;
    displayName?: string;
  };

  type PasskeyCredentialInput = {
    credentialJson: string;
    name?: string;
  };

  type PasskeyJsonDto = {
    json?: string;
  };

  type PayInput = {
    extraProperties?: Record<string, any>;
  };

  type PaymentDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    userId?: string;
    paymentMethod?: string;
    payeeAccount?: string;
    externalTradingCode?: string;
    currency?: string;
    originalPaymentAmount?: number;
    paymentDiscount?: number;
    actualPaymentAmount?: number;
    refundAmount?: number;
    pendingRefundAmount?: number;
    completionTime?: string;
    canceledTime?: string;
    paymentItems?: PaymentItemDto[];
  };

  type PaymentItemDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    itemType?: string;
    itemKey?: string;
    originalPaymentAmount?: number;
    paymentDiscount?: number;
    actualPaymentAmount?: number;
    refundAmount?: number;
    pendingRefundAmount?: number;
  };

  type PaymentMethodDto = {
    paymentMethod?: string;
    name?: string;
  };

  type PaymentRecordDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    paymentId?: string;
    appId?: string;
    mchId?: string;
    outTradeNo?: string;
    transactionId?: string;
    tradeType?: string;
    bankType?: string;
    attach?: string;
    returnCode?: string;
    returnMsg?: string;
    deviceInfo?: string;
    resultCode?: string;
    errCode?: string;
    errCodeDes?: string;
    openid?: string;
    isSubscribe?: string;
    totalFee?: number;
    settlementTotalFee?: number;
    feeType?: string;
    cashFee?: number;
    cashFeeType?: string;
    couponFee?: number;
    couponCount?: number;
    couponTypes?: string;
    couponIds?: string;
    couponFees?: string;
    timeEnd?: string;
    tradeState?: string;
    tradeStateDesc?: string;
    successTime?: string;
    payer?: string;
    amount?: string;
    sceneInfo?: string;
    promotionDetail?: string;
  };

  type PermissionGrantInfoDto = {
    name?: string;
    displayName?: string;
    parentName?: string;
    isGranted?: boolean;
    allowedProviders?: string[];
    grantedProviders?: ProviderInfoDto[];
    isEditable?: boolean;
  };

  type PermissionGroupDto = {
    name?: string;
    displayName?: string;
    displayNameKey?: string;
    displayNameResource?: string;
    permissions?: PermissionGrantInfoDto[];
  };

  type PermissionOptionDto = {
    name?: string;
    displayName?: string;
    parentName?: string;
  };

  type PersonalDataDto = {
    userName?: string;
    email?: string;
    name?: string;
    surname?: string;
    phoneNumber?: string;
    roles?: string[];
    tenantName?: string;
  };

  type postApiAppAccountLinkId_openAPI_switchParams = {
    id: string;
  };

  type postApiAppAccountProStartDelegationDelegationIdParams = {
    delegationId: string;
  };

  type postApiAppAuditLogIdMarkHandledParams = {
    id: string;
  };

  type postApiAppAuditLogIdUnmarkHandledParams = {
    id: string;
  };

  type postApiAppBackgroundJobIdAbandonParams = {
    id: string;
  };

  type postApiAppBackgroundJobIdRetryParams = {
    id: string;
  };

  type postApiAppEditionSetTenantEditionParams = {
    tenantId?: string;
    editionId?: string;
  };

  type postApiAppIdentitySessionIdRevokeParams = {
    id: string;
  };

  type postApiAppIdentitySessionRevokeAllByUserUserIdParams = {
    userId: string;
  };

  type postApiAppIdentityUserAdminEnqueueExportParams = {
    filter?: string;
  };

  type postApiAppIdentityUserAdminIdLockParams = {
    id: string;
  };

  type postApiAppIdentityUserAdminIdRequireChangePasswordOnNextLoginParams = {
    id: string;
  };

  type postApiAppIdentityUserAdminIdSetTwoFactorEnabledParams = {
    id: string;
  };

  type postApiAppIdentityUserAdminIdUnlockParams = {
    id: string;
  };

  type postApiAppIdentityUserDelegationIdStartParams = {
    id: string;
  };

  type postApiAppLanguageIdSetAsDefaultParams = {
    id: string;
  };

  type postApiAppLanguageTextRestoreToDefaultParams = {
    resourceName?: string;
    cultureName?: string;
    name?: string;
  };

  type postApiAppNotificationManagementIdRetryParams = {
    id: string;
  };

  type postApiAppOpenIddictApplicationIdGenerateAccessTokenParams = {
    id: string;
  };

  type postApiAppOpenIddictTokenAdminIdRevokeAuthorizationParams = {
    id: string;
  };

  type postApiAppOpenIddictTokenAdminIdRevokeTokenParams = {
    id: string;
  };

  type postApiAppOpenIddictTokenAdminRevokeBySubjectParams = {
    subject?: string;
  };

  type postApiAppOrganizationUnitIdMembersParams = {
    id: string;
  };

  type postApiAppOrganizationUnitIdMoveParams = {
    id: string;
  };

  type postApiAppOrganizationUnitIdRolesParams = {
    id: string;
  };

  type postApiAppPaymentAdminIdRefundParams = {
    id: string;
  };

  type postApiAppPostIdMembersParams = {
    id: string;
  };

  type postApiAppScheduledJobIdSetEnabledParams = {
    id: string;
    isEnabled?: boolean;
  };

  type postApiAppScheduledJobIdTriggerParams = {
    id: string;
  };

  type postApiAppScheduledJobPreviewCronParams = {
    cronExpression?: string;
  };

  type postApiAppTenantIdApplyPackageParams = {
    id: string;
  };

  type postApiAppTenantIdCheckConnectionStringParams = {
    id: string;
  };

  type postApiFileManagementFileManyWithStreamParams = {
    GenerateUniqueFileName?: boolean;
    FileContainerName: string;
    ParentId?: string;
    OwnerUserId?: string;
    ExtraProperties?: Record<string, any>;
  };

  type postApiFileManagementFileWithStreamParams = {
    GenerateUniqueFileName?: boolean;
    FileContainerName: string;
    ParentId?: string;
    OwnerUserId?: string;
    ExtraProperties?: Record<string, any>;
  };

  type postApiPaymentServicePaymentIdCancelParams = {
    id: string;
  };

  type postApiPaymentServicePaymentIdPayParams = {
    id: string;
  };

  type postApiPaymentServicePaymentIdRefundRollbackParams = {
    id: string;
  };

  type postApiPaymentServicePrepaymentAccountIdChangeBalanceParams = {
    id: string;
  };

  type postApiPaymentServicePrepaymentAccountIdChangeLockedBalanceParams = {
    id: string;
  };

  type postApiPaymentServicePrepaymentAccountIdTopUpParams = {
    id: string;
  };

  type postApiPaymentServicePrepaymentAccountIdWithdrawParams = {
    id: string;
  };

  type postApiPaymentServicePrepaymentWithdrawalRequestIdReviewParams = {
    id: string;
  };

  type postApiSettingManagementTimezoneParams = {
    timezone?: string;
  };

  type PostDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    name?: string;
    code?: string;
    sortOrder?: number;
    status?: PostStatusEnum;
    remark?: string;
    memberCount?: number;
  };

  type PostStatusEnum = 0 | 1;

  type PostUserDto = {
    id?: string;
    userName?: string;
    name?: string;
    email?: string;
    isActive?: boolean;
  };

  type postWechatPayNotifyMchIdMchIdParams = {
    tenantId?: string;
    mchId: string;
  };

  type postWechatPayNotifyParams = {
    tenantId?: string;
    mchId?: string;
  };

  type postWechatPayNotifyTenantIdTenantIdMchIdMchIdParams = {
    tenantId: string;
    mchId: string;
  };

  type postWechatPayNotifyTenantIdTenantIdParams = {
    tenantId: string;
    mchId?: string;
  };

  type postWechatPayRefundNotifyMchIdMchIdParams = {
    tenantId?: string;
    mchId: string;
  };

  type postWechatPayRefundNotifyParams = {
    tenantId?: string;
    mchId?: string;
  };

  type postWechatPayRefundNotifyTenantIdTenantIdMchIdMchIdParams = {
    tenantId: string;
    mchId: string;
  };

  type postWechatPayRefundNotifyTenantIdTenantIdParams = {
    tenantId: string;
    mchId?: string;
  };

  type ProfileDto = {
    extraProperties?: Record<string, any>;
    userName?: string;
    email?: string;
    name?: string;
    surname?: string;
    phoneNumber?: string;
    isExternal?: boolean;
    hasPassword?: boolean;
    concurrencyStamp?: string;
  };

  type PropertyApiDescriptionModel = {
    name?: string;
    jsonName?: string;
    type?: string;
    typeSimple?: string;
    isRequired?: boolean;
    minLength?: number;
    maxLength?: number;
    minimum?: string;
    maximum?: string;
    regex?: string;
    isNullable?: boolean;
    summary?: string;
    description?: string;
    displayName?: string;
  };

  type ProviderInfoDto = {
    providerName?: string;
    providerKey?: string;
  };

  type PublicFileContainerConfiguration = {
    fileContainerType?: FileContainerType;
    enableAutoRename?: boolean;
    maxByteSizeForEachFile?: number;
    maxByteSizeForEachUpload?: number;
    maxFileQuantityForEachUpload?: number;
    allowOnlyConfiguredFileExtensions?: boolean;
    fileExtensionsConfiguration?: Record<string, any>;
    getDownloadInfoTimesLimitEachUserPerMinute?: number;
  };

  type putApiAppClaimTypeIdParams = {
    id: string;
  };

  type putApiAppDataDictionaryViewDictionaryCodeItemsItemCodeMetaParams = {
    dictionaryCode: string;
    itemCode: string;
  };

  type putApiAppDataDictionaryViewDictionaryCodeItemsMetaParams = {
    dictionaryCode: string;
  };

  type putApiAppDataDictionaryViewDictionaryCodeItemsParams = {
    dictionaryCode: string;
  };

  type putApiAppEditionIdParams = {
    id: string;
  };

  type putApiAppIdentityClaimRoleClaimsRoleIdParams = {
    roleId: string;
  };

  type putApiAppIdentityClaimUserClaimsUserIdParams = {
    userId: string;
  };

  type putApiAppLanguageIdParams = {
    id: string;
  };

  type putApiAppMenuIdParams = {
    id: string;
  };

  type putApiAppMenuRoleGrantsMenuIdParams = {
    menuId: string;
  };

  type putApiAppOpenIddictApplicationIdParams = {
    id: string;
  };

  type putApiAppOpenIddictApplicationIdTokenLifetimeParams = {
    id: string;
  };

  type putApiAppOpenIddictScopeIdParams = {
    id: string;
  };

  type putApiAppOrganizationUnitIdParams = {
    id: string;
  };

  type putApiAppPostIdParams = {
    id: string;
  };

  type putApiAppRoleDataScopeIdParams = {
    id: string;
  };

  type putApiAppScheduledJobIdParams = {
    id: string;
  };

  type putApiAppTenantIdConnectionStringsParams = {
    id: string;
  };

  type putApiAppTenantIdDefaultConnectionStringParams = {
    id: string;
    defaultConnectionString?: string;
  };

  type putApiAppTenantIdParams = {
    id: string;
  };

  type putApiAppTenantPackageIdMenuSelectionParams = {
    id: string;
  };

  type putApiAppTenantPackageIdParams = {
    id: string;
  };

  type putApiDataDictionaryDataDictionaryIdParams = {
    id: string;
  };

  type putApiFeatureManagementFeaturesParams = {
    providerName?: string;
    providerKey?: string;
  };

  type putApiFileManagementFileIdInfoParams = {
    id: string;
  };

  type putApiFileManagementFileIdMoveParams = {
    id: string;
  };

  type putApiIdentityRolesIdParams = {
    id: string;
  };

  type putApiIdentityUsersIdParams = {
    id: string;
  };

  type putApiIdentityUsersIdRolesParams = {
    id: string;
  };

  type putApiMultiTenancyTenantsIdDefaultConnectionStringParams = {
    id: string;
    defaultConnectionString?: string;
  };

  type putApiMultiTenancyTenantsIdParams = {
    id: string;
  };

  type putApiPermissionManagementPermissionsParams = {
    providerName?: string;
    providerKey?: string;
  };

  type putApiPermissionManagementPermissionsResourceParams = {
    resourceName?: string;
    resourceKey?: string;
  };

  type RefundDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    paymentId?: string;
    refundPaymentMethod?: string;
    externalTradingCode?: string;
    currency?: string;
    refundAmount?: number;
    displayReason?: string;
    customerRemark?: string;
    staffRemark?: string;
    completedTime?: string;
    canceledTime?: string;
    refundItems?: RefundItemDto[];
  };

  type RefundItemDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    paymentItemId?: string;
    refundAmount?: number;
    customerRemark?: string;
    staffRemark?: string;
  };

  type RefundPaymentInput = {
    /** 退款金额。null 表示全额退款：按行项剩余可退总额由服务端计算
（前端不做 decimal 运算，避免 IEEE-754 精度尾差导致"超额 0.0000…1"被拒）。 */
    refundAmount?: number;
    displayReason?: string;
  };

  type RefundRecordDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    paymentId?: string;
    outRefundNo?: string;
    transactionId?: string;
    outTradeNo?: string;
    successTime?: string;
    returnCode?: string;
    returnMsg?: string;
    appId?: string;
    mchId?: string;
    refundId?: string;
    totalFee?: number;
    settlementTotalFee?: number;
    refundFee?: number;
    settlementRefundFee?: number;
    feeType?: string;
    cashFee?: number;
    cashFeeType?: string;
    cashRefundFee?: number;
    couponRefundFee?: number;
    couponRefundCount?: number;
    couponTypes?: string;
    couponIds?: string;
    couponRefundFees?: string;
    refundStatus?: string;
    refundRecvAccout?: string;
    refundAccount?: string;
    refundRequestSource?: string;
    channel?: string;
    userReceivedAccount?: string;
    createTime?: string;
    status?: string;
    fundsAccount?: string;
    amount?: string;
    promotionDetail?: string;
  };

  type RegisterDto = {
    extraProperties?: Record<string, any>;
    userName: string;
    emailAddress: string;
    password: string;
    appName: string;
  };

  type RemoteServiceErrorInfo = {
    code?: string;
    message?: string;
    details?: string;
    data?: Record<string, any>;
    validationErrors?: RemoteServiceValidationErrorInfo[];
  };

  type RemoteServiceErrorResponse = {
    error?: RemoteServiceErrorInfo;
  };

  type RemoteServiceValidationErrorInfo = {
    message?: string;
    members?: string[];
  };

  type ResetPasswordDto = {
    userId?: string;
    resetToken: string;
    password: string;
  };

  type ResourcePermissionDefinitionDto = {
    name?: string;
    displayName?: string;
  };

  type ResourcePermissionGrantInfoDto = {
    providerName?: string;
    providerKey?: string;
    providerDisplayName?: string;
    providerNameDisplayName?: string;
    permissions?: GrantedResourcePermissionDto[];
  };

  type ResourcePermissionWithProdiverGrantInfoDto = {
    name?: string;
    displayName?: string;
    providers?: string[];
    isGranted?: boolean;
  };

  type ResourceProviderDto = {
    name?: string;
    displayName?: string;
  };

  type RestoreTextTemplateToDefaultInput = {
    name: string;
    cultureName?: string;
  };

  type ReturnValueApiDescriptionModel = {
    type?: string;
    typeSimple?: string;
    summary?: string;
    contentTypes?: string[];
    isRemoteStream?: boolean;
  };

  type ReviewWithdrawalRequestInput = {
    isApproved?: boolean;
  };

  type RoleDataScopeDto = {
    id?: string;
    tenantId?: string;
    roleName?: string;
    scopeType?: DataScopeTypeEnum;
    customOrganizationUnitIds?: string[];
  };

  type SaveDataDictionaryItemInput = {
    /** 字典项编码；静态项编码不可改（服务端按编码集合校验）。 */
    code: string;
    displayText: string;
    description?: string;
    /** antd 标签色标识，可选值见 DataDictionaryTagTypes；null 表示无颜色。 */
    tagType?: string;
  };

  type SaveDataDictionaryItemsInput = {
    displayText: string;
    description?: string;
    items: SaveDataDictionaryItemInput[];
  };

  type ScheduledJobDto = {
    id?: string;
    name?: string;
    jobType?: string;
    cronExpression?: string;
    isEnabled?: boolean;
    description?: string;
    payload?: string;
    lastRunTime?: string;
    lastRunSuccess?: boolean;
    lastRunMessage?: string;
    nextRunTime?: string;
  };

  type ScheduledJobExecutionDto = {
    id?: string;
    scheduledJobId?: string;
    startTime?: string;
    endTime?: string;
    success?: boolean;
    message?: string;
    durationMs?: number;
    creationTime?: string;
  };

  type ScheduledJobTypeDto = {
    jobType?: string;
    /** 本地化后的展示名（服务端已按当前 UI 文化解析）。 */
    displayName?: string;
  };

  type SearchProviderKeyInfo = {
    providerKey?: string;
    providerDisplayName?: string;
  };

  type SearchProviderKeyListResultDto = {
    keys?: SearchProviderKeyInfo[];
  };

  type SecurityLogDto = {
    id?: string;
    creationTime?: string;
    applicationName?: string;
    identity?: string;
    action?: string;
    userName?: string;
    clientId?: string;
    clientIpAddress?: string;
    browserInfo?: string;
    correlationId?: string;
  };

  type SendEmailConfirmationCodeInput = {
    email: string;
  };

  type SendPasswordlessLoginCodeInput = {
    email: string;
  };

  type SendPasswordResetCodeDto = {
    email: string;
    appName: string;
    returnUrl?: string;
    returnUrlHash?: string;
  };

  type SendPhoneNumberConfirmationCodeInput = {
    phoneNumber: string;
  };

  type SendTestEmailInput = {
    senderEmailAddress: string;
    targetEmailAddress: string;
    subject: string;
    body?: string;
  };

  type SendToUsersNotificationInput = {
    userIds: string[];
    /** 渠道集合：Mailing / Sms / InApp，至少一个。 */
    notificationMethods: string[];
    title: string;
    body: string;
    smsText?: string;
    smsProperties?: Record<string, any>;
  };

  type SendTwoFactorCodeInput = {
    /** 发送目标：Email 或 Phone。为空时优先 Email，其次 Phone。
收件人固定为当前登录用户（服务端取 CurrentUser），入参不携带 UserId，防止越权给他人发码。 */
    provider?: string;
  };

  type ServerMonitorDto = {
    /** 机器名。 */
    machineName?: string;
    /** 操作系统描述（如 Microsoft Windows 10.0.26100 / Linux 5.x）。 */
    osDescription?: string;
    /** 操作系统架构（X64/ARM64）。 */
    osArchitecture?: string;
    /** 进程架构。 */
    processArchitecture?: string;
    /** CPU 逻辑核心数。 */
    processorCount?: number;
    /** .NET 运行时版本。 */
    dotNetVersion?: string;
    /** 进程启动时间（UTC）。 */
    processStartTimeUtc?: string;
    /** 进程已运行秒数。 */
    uptimeSeconds?: number;
    /** 进程 CPU 占用百分比（按全部逻辑核心归一，请求内采样约 300ms）。null 表示采样失败。 */
    processCpuUsagePercent?: number;
    /** 进程工作集（字节）。 */
    workingSetBytes?: number;
    /** 进程私有内存（字节）。 */
    privateMemoryBytes?: number;
    /** GC 托管堆大小（字节）。 */
    gcHeapSizeBytes?: number;
    /** GC 视角的可用物理内存上限（字节；容器内为配额，非整机内存）。 */
    gcTotalMemoryLimitBytes?: number;
    /** 是否 Server GC。 */
    isServerGc?: boolean;
    /** 第 0/1/2 代 GC 次数。 */
    gen0Collections?: number;
    gen1Collections?: number;
    gen2Collections?: number;
    /** 进程托管线程数。 */
    threadCount?: number;
    /** 线程池可用工作线程数（瞬时值）。 */
    threadPoolAvailableWorkerThreads?: number;
    /** 线程池工作线程最小数（配置值）。 */
    threadPoolMinWorkerThreads?: number;
    /** 已就绪的磁盘分区列表。 */
    disks?: DiskInfoDto[];
  };

  type SettingGroup = {
    groupName?: string;
    groupDisplayName?: string;
    settingInfos?: SettingInfo[];
    permission?: string;
  };

  type SettingInfo = {
    name?: string;
    displayName?: string;
    description?: string;
    value?: string;
    properties?: Record<string, any>;
    permission?: string;
  };

  type SetTwoFactorEnabledInput = {
    enabled?: boolean;
    /** 关闭 2FA 时必填：当前有效的双因素验证码（与 DisableAuthenticatorAsync 验码语义对齐，
防止会话被劫持后保护被直接关闭）。启用时不需要。 */
    code?: string;
    /** 验证码通道：Email 或 Phone。为空时优先 Email，其次 Phone。 */
    provider?: string;
  };

  type SetUserTwoFactorEnabledDto = {
    enabled?: boolean;
  };

  type TenantConnectionStringItemDto = {
    /** "Default" 或 IsUsedByTenants = true 的数据库名（如 EasyAbpFileManagement）。 */
    name?: string;
    /** 固定掩码 "********"（已设值时）；永不返回明文。 */
    value?: string;
  };

  type TenantConnectionStringItemInput = {
    name: string;
    /** 新的连接串明文；留空或回传掩码表示保持原值。存储前会在服务端加密。 */
    value?: string;
  };

  type TenantConnectionStringManagementDto = {
    /** 设置项 AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement 的当前值，
便于前端在无权限读取设置接口时也能判断。 */
    isManagementEnabled?: boolean;
    /** 「使用共享数据库」：当前无任何连接串记录时为 true，租户回退到 host 的连接串。 */
    useSharedDatabase?: boolean;
    /** 已有的连接串记录（Default 与模块特定）。 */
    items?: TenantConnectionStringItemDto[];
  };

  type TenantCreateDto = {
    extraProperties?: Record<string, any>;
    name: string;
    adminEmailAddress: string;
    adminPassword: string;
  };

  type TenantDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    name?: string;
    concurrencyStamp?: string;
  };

  type TenantPackageCreateDto = {
    name: string;
    remark?: string;
  };

  type TenantPackageDto = {
    id?: string;
    name?: string;
    remark?: string;
    menuCount?: number;
  };

  type TenantPackageMenuSelectionDto = {
    /** Host 模板树（管理端全量，含停用/隐藏），结构同菜单管理的 MenuTreeDto。 */
    tree?: MenuTreeDto[];
    /** 已勾选的模板菜单 Id。 */
    checkedMenuIds?: string[];
  };

  type TenantPackageUpdateDto = {
    name: string;
    remark?: string;
  };

  type TenantUpdateDto = {
    extraProperties?: Record<string, any>;
    name: string;
    concurrencyStamp?: string;
  };

  type TenantUserSharingStrategy = 0 | 1;

  type TextTemplateDto = {
    name?: string;
    displayName?: string;
    isLayout?: boolean;
    layout?: string;
    cultureName?: string;
    content?: string;
    /** 是否为沙箱引擎（如 Scriban）。非沙箱引擎（如 Razor）可以执行任意代码，需要额外权限。 */
    isSandboxed?: boolean;
  };

  type TimeZone = {
    iana?: IanaTimeZone;
    windows?: WindowsTimeZone;
  };

  type TimingDto = {
    timeZone?: TimeZone;
  };

  type TopUpInput = {
    paymentMethod?: string;
    amount?: number;
  };

  type TransactionDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    accountId?: string;
    accountUserId?: string;
    paymentId?: string;
    transactionType?: TransactionType;
    actionName?: string;
    paymentMethod?: string;
    externalTradingCode?: string;
    currency?: string;
    changedBalance?: number;
    originalBalance?: number;
  };

  type TransactionType = 1 | 2;

  type TwoFactorStatusDto = {
    twoFactorEnabled?: boolean;
    emailConfirmed?: boolean;
    phoneNumberConfirmed?: boolean;
  };

  type TypeApiDescriptionModel = {
    baseType?: string;
    isEnum?: boolean;
    enumNames?: string[];
    enumValues?: any[];
    genericArguments?: string[];
    properties?: PropertyApiDescriptionModel[];
    summary?: string;
    remarks?: string;
    description?: string;
    displayName?: string;
  };

  type UnreadCountDto = {
    count?: number;
  };

  type UpdateClaimTypeDto = {
    name: string;
    required?: boolean;
    regex?: string;
    regexDescription?: string;
    description?: string;
    valueType?: IdentityClaimValueType;
  };

  type UpdateDataDictionaryItemMetaInput = {
    /** antd 标签色标识，可选值见 DataDictionaryTagTypes；null 表示无颜色。 */
    tagType?: string;
    order?: number;
  };

  type UpdateDataDictionaryItemMetaItemInput = {
    /** 字典项编码。 */
    itemCode: string;
    /** antd 标签色标识，可选值见 DataDictionaryTagTypes；null 表示无颜色。 */
    tagType?: string;
    order?: number;
  };

  type UpdateEditionDto = {
    displayName: string;
  };

  type UpdateEmailSettingsDto = {
    smtpHost?: string;
    smtpPort?: number;
    smtpUserName?: string;
    smtpPassword?: string;
    smtpDomain?: string;
    smtpEnableSsl?: boolean;
    smtpUseDefaultCredentials?: boolean;
    defaultFromAddress: string;
    defaultFromDisplayName: string;
  };

  type UpdateFeatureDto = {
    name?: string;
    value?: string;
  };

  type UpdateFeaturesDto = {
    features?: UpdateFeatureDto[];
  };

  type UpdateFileInfoInput = {
    extraProperties?: Record<string, any>;
    fileName: string;
  };

  type UpdateLanguageDto = {
    displayName: string;
    flagIcon?: string;
    isEnabled?: boolean;
  };

  type UpdateLanguageTextDto = {
    resourceName: string;
    cultureName: string;
    name: string;
    /** 本地化值。允许空串（AllowEmptyStrings）——与实体 LanguageText 的"空串 = 显式未翻译"
语义对齐（用户清空文本框保存 = 标记未翻译，而非参数错误）。 */
    value: string;
  };

  type UpdateMenuGrantsDto = {
    roleNames?: string[];
  };

  type UpdateOpenIddictApplicationDto = {
    clientId: string;
    displayName: string;
    /** public / confidential，见 OpenIddictConstants.ClientTypes。 */
    clientType: string;
    /** web / native，见 OpenIddictConstants.ApplicationTypes。 */
    applicationType: string;
    /** explicit / external / implicit / systematic，见 OpenIddictConstants.ConsentTypes。 */
    consentType: string;
    /** 只写：更新时留空表示保持原值。管理 API 永不回读该值。 */
    clientSecret?: string;
    /** 只写：JWKS（JSON）。更新时 null=保持原值；空字符串=显式移除
（仅在 secret 仍存在时才允许移除，见 secret 只写语义规则 4）。 */
    jsonWebKeySet?: string;
    clientUri?: string;
    logoUri?: string;
    /** 每行一个，必须是绝对 URI。 */
    redirectUris?: string;
    /** 每行一个，必须是绝对 URI。 */
    postLogoutRedirectUris?: string;
    /** 必须是绝对 URI。 */
    frontChannelLogoutUri?: string;
    allowAuthorizationCodeFlow?: boolean;
    allowImplicitFlow?: boolean;
    /** 勾选后服务端派生：同时启用 Authorization Code 与 Implicit。 */
    allowHybridFlow?: boolean;
    allowPasswordFlow?: boolean;
    allowClientCredentialsFlow?: boolean;
    allowRefreshTokenFlow?: boolean;
    allowTokenExchangeFlow?: boolean;
    allowDeviceAuthorizationFlow?: boolean;
    enableEndSessionEndpoint?: boolean;
    enablePushedAuthorizationEndpoint?: boolean;
    requirePkce?: boolean;
    /** 勾选后服务端派生：同时启用 Pushed Authorization 端点。 */
    requirePushedAuthorization?: boolean;
    /** 允许的 scopes（授予 scp: 权限）。 */
    scopes?: string[];
    /** extension grant types（授予 gt: 权限）。 */
    extensionGrantTypes?: string[];
  };

  type UpdateOpenIddictApplicationTokenLifetimeDto = {
    accessTokenLifetime?: number;
    authorizationCodeLifetime?: number;
    deviceCodeLifetime?: number;
    identityTokenLifetime?: number;
    refreshTokenLifetime?: number;
    userCodeLifetime?: number;
    /** Pushed Authorization Request 的 request token 生命周期。 */
    requestTokenLifetime?: number;
    /** token exchange 等场景签发的「其它」token 生命周期。 */
    issuedTokenLifetime?: number;
  };

  type UpdateOpenIddictScopeDto = {
    name: string;
    displayName?: string;
    description?: string;
    resources?: string;
  };

  type UpdateOrganizationUnitDto = {
    displayName: string;
  };

  type UpdatePermissionDto = {
    name?: string;
    isGranted?: boolean;
  };

  type UpdatePermissionsDto = {
    permissions?: UpdatePermissionDto[];
  };

  type UpdatePostDto = {
    /** 显示名同时服务操作日志 _diff 输出（【岗位名称】xx → yy）。 */
    name: string;
    code: string;
    sortOrder?: number;
    status?: PostStatusEnum;
    remark?: string;
  };

  type UpdateProfileDto = {
    extraProperties?: Record<string, any>;
    userName?: string;
    email?: string;
    name?: string;
    surname?: string;
    phoneNumber?: string;
    concurrencyStamp?: string;
  };

  type UpdateResourcePermissionsDto = {
    providerName?: string;
    providerKey?: string;
    permissions?: string[];
  };

  type UpdateScheduledJobDto = {
    name: string;
    cronExpression: string;
    description?: string;
    payload?: string;
  };

  type UpdateTenantConnectionStringsInput = {
    /** 勾选「使用共享数据库」：移除该租户的所有连接串记录，回退到 host 的连接串。
为 true 时忽略 Items。 */
    useSharedDatabase?: boolean;
    /** 期望的最终连接串集合。Value 留空或回传掩码表示保持原值；
已存在但未出现在本列表中的记录会被删除。 */
    items?: TenantConnectionStringItemInput[];
  };

  type UpdateTenantPackageMenusDto = {
    menuIds?: string[];
  };

  type UpdateTextTemplateDto = {
    /** 模板名（与模板定义对齐，最大 128）。 */
    name: string;
    /** 文化名；null 表示文化无关。 */
    cultureName?: string;
    content: string;
  };

  type UserData = {
    id?: string;
    tenantId?: string;
    userName?: string;
    name?: string;
    surname?: string;
    isActive?: boolean;
    email?: string;
    emailConfirmed?: boolean;
    phoneNumber?: string;
    phoneNumberConfirmed?: boolean;
    extraProperties?: Record<string, any>;
  };

  type UserExportResultDto = {
    isQueued?: boolean;
    message?: string;
  };

  type UserImportResultDto = {
    totalCount?: number;
    succeededCount?: number;
    failedCount?: number;
    /** 失败明细文件的 BLOB 记录 id，无失败时为 null。 */
    failureReportId?: string;
    /** 只返回前 50 条错误，完整明细走下载。 */
    errors?: UserImportRowErrorDto[];
  };

  type UserImportRowErrorDto = {
    rowNumber?: number;
    userName?: string;
    errorMessage?: string;
  };

  type UserLoginDto = {
    loginProvider?: string;
    providerKey?: string;
    providerDisplayName?: string;
  };

  type UserLoginInfo = {
    userNameOrEmailAddress: string;
    password: string;
    rememberMe?: boolean;
  };

  type UserPasskeyDto = {
    credentialId?: string;
    name?: string;
    createdAt?: string;
  };

  type UserTwoFactorStatusDto = {
    userId?: string;
    twoFactorEnabled?: boolean;
  };

  type VerifyPasswordResetTokenInput = {
    userId?: string;
    resetToken: string;
  };

  type VerifyTwoFactorCodeInput = {
    code: string;
    /** 发送目标：Email 或 Phone。为空时优先 Email，其次 Phone。
校验对象固定为当前登录用户（服务端取 CurrentUser），入参不携带 UserId。 */
    provider?: string;
  };

  type VirtualFileContentDto = {
    name?: string;
    path?: string;
    length?: number;
    isBinary?: boolean;
    truncated?: boolean;
    content?: string;
  };

  type VirtualFileDirectoryDto = {
    path?: string;
    parentPath?: string;
    items?: VirtualFileItemDto[];
  };

  type VirtualFileItemDto = {
    name?: string;
    path?: string;
    isDirectory?: boolean;
    exists?: boolean;
    length?: number;
  };

  type WindowsTimeZone = {
    timeZoneId?: string;
  };

  type WithdrawalRecordDto = {
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    accountId?: string;
    withdrawalMethod?: string;
    amount?: number;
    completionTime?: string;
    cancellationTime?: string;
    resultErrorCode?: string;
    resultErrorMessage?: string;
  };

  type WithdrawalRequestDto = {
    extraProperties?: Record<string, any>;
    id?: string;
    creationTime?: string;
    creatorId?: string;
    lastModificationTime?: string;
    lastModifierId?: string;
    isDeleted?: boolean;
    deleterId?: string;
    deletionTime?: string;
    accountId?: string;
    accountUserId?: string;
    amount?: number;
    reviewTime?: string;
    reviewerUserId?: string;
    isApproved?: boolean;
  };

  type WithdrawInput = {
    extraProperties?: Record<string, any>;
    withdrawalMethod?: string;
    amount?: number;
  };
}
