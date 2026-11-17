import { STORAGE_KEYS } from '@/abp/storageKeys';
import type { AppCurrentUser } from './app';

/**
 * Umi access 插件：把 ABP `grantedPolicies` 映射成路由/按钮用的布尔开关。
 * - 权限字符串 = 后端权限名，禁止「前端自创码」。
 * - isHost：无可用租户时才为 true；Host-only 功能（OpenIddict、Editions…）必须带 isHost。
 * - 财务壳开关见返回值旁注释；与 `src/abp/finance.ts` 文件头 localStorage 表一致。
 * @see https://umijs.org/docs/max/access#access
 */
export default function access(
  initialState:
    | {
        currentUser?: AppCurrentUser;
        grantedPolicies?: Record<string, boolean>;
        currentTenant?: { isAvailable?: boolean };
      }
    | undefined,
) {
  const policies = initialState?.grantedPolicies ?? {};
  const financeShell =
    typeof localStorage !== 'undefined' &&
    localStorage.getItem(STORAGE_KEYS.financeShellAccess) === '1';
  const isHost = !initialState?.currentTenant?.isAvailable;
  const canViewOwnOrders = isHost && !!policies['AbpAdmin.Ordering.Orders'];
  const canViewAllOrders =
    isHost && !!policies['AbpAdmin.Ordering.Orders.ViewAll'];
  const canManagePaymentSettings =
    isHost && !!policies['SettingUi.ShowSettingPage'];
  const canViewDepositRecords =
    isHost &&
    (!!policies['AbpAdmin.Collection.Collections'] ||
      !!policies['AbpAdmin.Collection.Network.Admin']);
  const canOpenWithdrawalWorkspace =
    canManagePaymentSettings ||
    (isHost &&
      (!!policies['AbpAdmin.Collection.WithdrawAdmin'] ||
        !!policies['AbpAdmin.Collection.Network.Admin']));
  const canViewWeb3Operations =
    isHost &&
    (!!policies['AbpAdmin.Collection.Collections'] ||
      !!policies['AbpAdmin.Collection.WithdrawAdmin'] ||
      !!policies['AbpAdmin.Collection.Network.Admin']);
  const canManageWeb3Configuration =
    canManagePaymentSettings ||
    (isHost &&
      (!!policies['AbpAdmin.Collection.Network.Admin'] ||
        !!policies['AbpAdmin.Collection.WithdrawAdmin']));
  const canViewBalanceAdjustments =
    isHost && !!policies['AbpAdmin.Collection.BalanceAdjustments.View'];
  const canManageSettings = !!policies['SettingUi.ShowSettingPage'];
  const canManageEmailing = !!policies['SettingManagement.Emailing'];
  const canManageTimezone = !!policies['SettingManagement.TimeZone'];
  const canManageNotificationChannels = canManageEmailing || canManageSettings;
  const canManageTenantPolicy = isHost && canManageSettings;
  const canManageTenants = isHost && !!policies['AbpTenantManagement.Tenants'];
  const canManageEditions = isHost && !!policies['AbpAdmin.Editions'];
  const canManageLanguages = !!policies['AbpAdmin.Languages'];
  const canManageTextTemplates = !!policies['AbpAdmin.TextTemplates'];
  const canManageWebhooks = !!policies['AbpAdmin.Webhooks.Subscriptions'];
  const canManageWebhookSendRecords =
    !!policies['AbpAdmin.Webhooks.SendRecords'];
  const canViewFinanceLedger =
    !!policies['Finance.Ledger.View'] || financeShell;
  const financeAgentScoped =
    !!policies['Finance.Scope.Agent'] && !policies['Finance.Scope.All'];
  const canViewFinanceOverviewStats =
    canViewFinanceLedger && !financeAgentScoped;
  const canViewFinanceAccounts =
    canViewFinanceLedger || !!policies['Finance.Account.Manage'];
  const canViewFinanceRules = !!policies['Finance.Rule.View'] || financeShell;
  const canViewFinancePeriod =
    !!policies['Finance.Period.View'] || financeShell;
  const canViewFinanceConfiguration =
    canViewFinanceAccounts ||
    canViewFinanceRules ||
    canViewFinancePeriod ||
    canManagePaymentSettings;
  const canViewFinanceVouchers =
    !!policies['Finance.Voucher.View'] || financeShell;
  const canViewFinanceManualAdjusts =
    canViewFinanceVouchers ||
    !!policies['Finance.Adjust.Create'] ||
    !!policies['Finance.Adjust.Submit'] ||
    !!policies['Finance.Adjust.Approve'] ||
    !!policies['Finance.Adjust.Reject'] ||
    !!policies['Finance.Adjust.Reverse'];
  return {
    isHostContext: isHost,
    canAdmin: !!policies['AbpIdentity.Users'],
    canManageUsers: !!policies['AbpIdentity.Users'],
    canManageRoles: !!policies['AbpIdentity.Roles'],
    canCreateUsers: !!policies['AbpIdentity.Users.Create'],
    canUpdateUsers: !!policies['AbpIdentity.Users.Update'],
    canDeleteUsers: !!policies['AbpIdentity.Users.Delete'],
    canManageUserRoles: !!policies['AbpIdentity.Users.Update.ManageRoles'],
    canManageUserPermissions: !!policies['AbpIdentity.Users.ManagePermissions'],
    // 角色页按钮级权限（对齐 ABP 内置 AbpIdentity.Roles 权限树）
    canCreateRoles: !!policies['AbpIdentity.Roles.Create'],
    canUpdateRoles: !!policies['AbpIdentity.Roles.Update'],
    canDeleteRoles: !!policies['AbpIdentity.Roles.Delete'],
    canManageRolePermissions: !!policies['AbpIdentity.Roles.ManagePermissions'],
    // 数据范围示例的扩展配置；常规角色管理只使用 ABP 功能权限。
    canManageRoleDataScopes: !!policies['AbpAdmin.DataScopes.Manage'],
    canManageTenants,
    canCreateTenants:
      canManageTenants && !!policies['AbpTenantManagement.Tenants.Create'],
    canUpdateTenants:
      canManageTenants && !!policies['AbpTenantManagement.Tenants.Update'],
    canDeleteTenants:
      canManageTenants && !!policies['AbpTenantManagement.Tenants.Delete'],
    canManageTenantConnectionStrings:
      canManageTenants &&
      !!policies['AbpTenantManagement.Tenants.ManageConnectionStrings'],
    canManageHostFeatures:
      !!policies['FeatureManagement.ManageHostFeatures'] && isHost,
    canManageEmailing,
    canManageEmailingTest: !!policies['SettingManagement.Emailing.Test'],
    canManageTimezone,
    canManageLanguages,
    canCreateLanguages:
      canManageLanguages && !!policies['AbpAdmin.Languages.Create'],
    canUpdateLanguages:
      canManageLanguages && !!policies['AbpAdmin.Languages.Update'],
    canDeleteLanguages:
      canManageLanguages && !!policies['AbpAdmin.Languages.Delete'],
    canChangeDefaultLanguage: !!policies['AbpAdmin.Languages.ChangeDefault'],
    canManageLanguageTexts: !!policies['AbpAdmin.LanguageTexts'],
    canEditLanguageTexts: !!policies['AbpAdmin.LanguageTexts.Edit'],
    canManageOrganizationUnits: !!policies['AbpAdmin.OrganizationUnits'],
    canCreateOrganizationUnits: !!policies['AbpAdmin.OrganizationUnits.Create'],
    canUpdateOrganizationUnits: !!policies['AbpAdmin.OrganizationUnits.Update'],
    canDeleteOrganizationUnits: !!policies['AbpAdmin.OrganizationUnits.Delete'],
    canManageOrganizationUnitMembers:
      !!policies['AbpAdmin.OrganizationUnits.ManageMembers'],
    canManageOrganizationUnitRoles:
      !!policies['AbpAdmin.OrganizationUnits.ManageRoles'],
    canManageClaimTypes: !!policies['AbpAdmin.ClaimTypes'],
    // 声明类型页按钮级权限（对齐后端 AbpAdmin.ClaimTypes 权限树）
    canCreateClaimTypes: !!policies['AbpAdmin.ClaimTypes.Create'],
    canUpdateClaimTypes: !!policies['AbpAdmin.ClaimTypes.Update'],
    canDeleteClaimTypes: !!policies['AbpAdmin.ClaimTypes.Delete'],
    canManageSecurityLogs: !!policies['AbpAdmin.SecurityLogs'],
    canManageSessions: !!policies['AbpAdmin.Sessions'],
    // 会话吊销（单会话 + 按用户批量吊销共用 Revoke 权限）
    canRevokeSessions: !!policies['AbpAdmin.Sessions.Revoke'],
    canManageAuditLogs: !!policies['AbpAdmin.AuditLogs'],
    canManageOperationLogs: !!policies['AbpAdmin.OperationLogs'],
    canExportAuditLogs: !!policies['AbpAdmin.AuditLogs.Export'],
    canHandleAuditLogErrors: !!policies['AbpAdmin.AuditLogs.HandleErrors'],
    // 实体变更回滚：能看变更历史 ≠ 能改写业务数据，独立权限
    canRestoreEntityChange: !!policies['AbpAdmin.AuditLogs.Restore'],
    // Webhook 管理（模块自持权限组 AbpAdmin.Webhooks.*）
    canManageWebhooks,
    canOpenWebhookWorkspace: canManageWebhooks || canManageWebhookSendRecords,
    canCreateWebhookSubscriptions:
      !!policies['AbpAdmin.Webhooks.Subscriptions.Create'],
    canUpdateWebhookSubscriptions:
      !!policies['AbpAdmin.Webhooks.Subscriptions.Update'],
    canDeleteWebhookSubscriptions:
      !!policies['AbpAdmin.Webhooks.Subscriptions.Delete'],
    canManageWebhookSendRecords,
    canResendWebhookSendRecords:
      !!policies['AbpAdmin.Webhooks.SendRecords.Resend'],
    canDeleteWebhookSendRecords:
      !!policies['AbpAdmin.Webhooks.SendRecords.Delete'],
    canManageOpenIddict: !!policies['AbpAdmin.OpenIddict'] && isHost,
    canManageOpenIddictApplications:
      !!policies['AbpAdmin.OpenIddict.Applications'] && isHost,
    canManageOpenIddictScopes:
      !!policies['AbpAdmin.OpenIddict.Scopes'] && isHost,
    // 应用/范围管理的按钮级权限（与后端 AbpAdminPermissions.OpenIddict 对齐）
    canCreateOpenIddictApplication:
      !!policies['AbpAdmin.OpenIddict.Applications.Create'] && isHost,
    canUpdateOpenIddictApplication:
      !!policies['AbpAdmin.OpenIddict.Applications.Update'] && isHost,
    canDeleteOpenIddictApplication:
      !!policies['AbpAdmin.OpenIddict.Applications.Delete'] && isHost,
    canManageOpenIddictApplicationPermissions:
      !!policies['AbpAdmin.OpenIddict.Applications.ManagePermissions'] &&
      isHost,
    canCreateOpenIddictScope:
      !!policies['AbpAdmin.OpenIddict.Scopes.Create'] && isHost,
    canUpdateOpenIddictScope:
      !!policies['AbpAdmin.OpenIddict.Scopes.Update'] && isHost,
    canDeleteOpenIddictScope:
      !!policies['AbpAdmin.OpenIddict.Scopes.Delete'] && isHost,
    // T2.9：OpenIddict 应用代取 access token（还需叠加应用自身的 Confidential + CC flow 条件）
    canGenerateOpenIddictAccessToken:
      !!policies['AbpAdmin.OpenIddict.Applications.GenerateAccessToken'] &&
      isHost,
    // 令牌/授权管理（Host 专属）
    canManageOpenIddictTokens:
      !!policies['AbpAdmin.OpenIddict.Tokens'] && isHost,
    canRevokeTokens: !!policies['AbpAdmin.OpenIddict.Tokens.Revoke'] && isHost,
    canPruneTokens: !!policies['AbpAdmin.OpenIddict.Tokens.Prune'] && isHost,
    canManageEditions,
    canCreateEditions:
      canManageEditions && !!policies['AbpAdmin.Editions.Create'],
    canUpdateEditions:
      canManageEditions && !!policies['AbpAdmin.Editions.Update'],
    canDeleteEditions:
      canManageEditions && !!policies['AbpAdmin.Editions.Delete'],
    // 版本/租户级功能开关编辑（对应 FeatureManagement provider 策略：E→版本权限，T→租户权限）
    canManageEditionFeatures:
      !!policies['AbpAdmin.Editions.ManageFeatures'] && isHost,
    canManageTenantFeatures:
      !!policies['AbpTenantManagement.Tenants.ManageFeatures'] && isHost,
    // EasyAbp.FileManagement 权限（T1.2 文件管理重写）
    canManageFiles: !!policies['EasyAbp.FileManagement.File'],
    canCreateFile: !!policies['EasyAbp.FileManagement.File.Create'],
    canUpdateFile: !!policies['EasyAbp.FileManagement.File.Update'],
    canDeleteFile: !!policies['EasyAbp.FileManagement.File.Delete'],
    canMoveFile: !!policies['EasyAbp.FileManagement.File.Move'],
    canGetFileDownloadInfo:
      !!policies['EasyAbp.FileManagement.File.GetDownloadInfo'],
    canManageTextTemplates,
    canUpdateTextTemplates:
      canManageTextTemplates && !!policies['AbpAdmin.TextTemplates.Update'],
    canEditNonSandboxedTemplates:
      !!policies['AbpAdmin.TextTemplates.EditNonSandboxedContents'],
    canManageBackgroundJobs: !!policies['AbpAdmin.BackgroundJobs'],
    canRetryBackgroundJobs: !!policies['AbpAdmin.BackgroundJobs.Retry'],
    canAbandonBackgroundJobs: !!policies['AbpAdmin.BackgroundJobs.Abandon'],
    canDeleteBackgroundJobs: !!policies['AbpAdmin.BackgroundJobs.Delete'],
    canManageScheduledJobs: !!policies['AbpAdmin.ScheduledJobs'],
    canCreateScheduledJobs: !!policies['AbpAdmin.ScheduledJobs.Create'],
    canUpdateScheduledJobs: !!policies['AbpAdmin.ScheduledJobs.Update'],
    canDeleteScheduledJobs: !!policies['AbpAdmin.ScheduledJobs.Delete'],
    canTriggerScheduledJobs: !!policies['AbpAdmin.ScheduledJobs.Trigger'],
    canManageVirtualFileExplorer: !!policies['AbpAdmin.VirtualFileExplorer'],
    // 服务监控 / 缓存监控（权限本身定义在 Host 侧，租户拿不到授予，这里无需再叠 isHost）
    canViewServerMonitor: !!policies['AbpAdmin.ServerMonitor'],
    canViewCacheMonitor: !!policies['AbpAdmin.CacheMonitor'],
    canManageCache: !!policies['AbpAdmin.CacheMonitor.Manage'],
    // SettingUi 通用设置页权限（T1.4）
    canManageSettings,
    canOpenCompatibilitySettings:
      canManageSettings || canManageEmailing || canManageTimezone,
    canManageNotificationChannels,
    canManageTenantPolicy,
    canOpenNotificationWorkspace:
      canManageNotificationChannels ||
      !!policies['EasyAbp.NotificationService.Notification.Manage'],
    canOpenTenantWorkspace:
      isHost &&
      (canManageTenantPolicy || !!policies['AbpTenantManagement.Tenants']),
    canOpenLoginSecurityWorkspace:
      canManageSettings || !!policies['AbpAdmin.SecurityLogs'],
    canOpenAuditWorkspace:
      canManageSettings || !!policies['AbpAdmin.AuditLogs'],
    canOpenLanguageWorkspace:
      canManageTimezone || !!policies['AbpAdmin.Languages'],
    canViewModuleManagement: isHost && !!policies['AbpAdmin.ModuleManagement'],
    canUpdateModuleManagement:
      isHost &&
      !!policies['AbpAdmin.ModuleManagement'] &&
      !!policies['AbpAdmin.ModuleManagement.Update'],
    // SettingUi 写权限（读写分离）：后端 AbpAdminSettingUiAppService 方法级 Authorize，
    // 前端只做按钮态联动，防线在后端
    canUpdateSettings: !!policies['AbpAdmin.SettingUi.Update'],
    // 业务模块数据权限示例（AbpAdmin.Biz.Template 自带权限组，/biz-template/data-scope）
    canManageBizDataScopeDemo: !!policies['BizTemplate.DataScopeDemo'],
    canOpenDataScopeExtension:
      !!policies['BizTemplate.DataScopeDemo'] ||
      (!!policies['AbpAdmin.DataScopes.Manage'] &&
        !!policies['AbpIdentity.Roles']),
    // 订单/收款模块（只给 Host 用户：后端应用服务有 Host 守卫，这里只管菜单可见性）
    canViewOrders: canViewOwnOrders || canViewAllOrders,
    canOpenOrderWorkspace:
      canViewOwnOrders || canViewAllOrders || canManagePaymentSettings,
    canOpenPaymentWorkspace:
      canManagePaymentSettings ||
      canViewDepositRecords ||
      (isHost && !!policies['AbpAdmin.Collection.Payments.View']),
    canViewDepositRecords,
    canOpenWithdrawalWorkspace,
    canViewOwnOrders,
    canViewAllOrders,
    canViewCollections: !!policies['AbpAdmin.Collection.Collections'],
    canViewMemberFunds: isHost && !!policies['AbpAdmin.Collection.Collections'],
    canViewCollectionPayments:
      !!policies['AbpAdmin.Collection.Payments.View'] && isHost,
    canReconcileCollectionPayments:
      !!policies['AbpAdmin.Collection.Payments.View'] &&
      !!policies['AbpAdmin.Collection.Payments.Reconcile'] &&
      isHost,
    canRefundCollectionPayments:
      !!policies['AbpAdmin.Collection.Payments.View'] &&
      !!policies['AbpAdmin.Collection.Payments.Refund'] &&
      isHost,
    // 订单、用户资金和渠道查询沿用此路由组，单页独立校验权限。
    canViewTrade:
      canViewOwnOrders ||
      canViewAllOrders ||
      canViewDepositRecords ||
      canOpenWithdrawalWorkspace ||
      canManagePaymentSettings ||
      (isHost && !!policies['AbpAdmin.Collection.Payments.View']),
    canManagePaymentSettings,
    canViewPaymentChannel:
      isHost &&
      (!!policies['AbpAdmin.Collection.Payments.View'] ||
        !!policies['SettingUi.ShowSettingPage']),
    canViewWeb3: canViewWeb3Operations || canManageWeb3Configuration,
    canViewWeb3Operations,
    canManageWeb3Configuration,
    // 链网配置（高危运维：RPC/助记词/金库/上限），独立权限仅授予运维角色
    canAdminNetwork: !!policies['AbpAdmin.Collection.Network.Admin'],
    canConfigureCollectionNetwork:
      isHost &&
      (!!policies['AbpAdmin.Collection.Network.Admin'] ||
        !!policies['SettingUi.ShowSettingPage']),
    canManageCollectionWallets:
      isHost &&
      (!!policies['AbpAdmin.Collection.Network.Admin'] ||
        !!policies['AbpAdmin.Collection.WithdrawAdmin']),
    canViewBalanceAdjustments,
    canCreateBalanceAdjustments:
      canViewBalanceAdjustments &&
      !!policies['AbpAdmin.Collection.BalanceAdjustments.Create'],
    canApproveBalanceAdjustments:
      canViewBalanceAdjustments &&
      !!policies['AbpAdmin.Collection.BalanceAdjustments.Approve'],
    canRejectBalanceAdjustments:
      canViewBalanceAdjustments &&
      !!policies['AbpAdmin.Collection.BalanceAdjustments.Reject'],
    canApproveOwnBalanceAdjustments:
      canViewBalanceAdjustments &&
      !!policies['AbpAdmin.Collection.BalanceAdjustments.ApproveOwn'],
    // 提现管理（资金出账高危动作，Host 专用独立权限）
    canAdminWithdrawals: !!policies['AbpAdmin.Collection.WithdrawAdmin'],
    // 用户导出
    canExportUsers: !!policies['AbpAdmin.Identity.Users.Export'],
    // T2.7 Account Pro 缺口：模拟登录（租户模拟仅 host）
    canImpersonateTenants:
      !!policies['AbpAdmin.Impersonation.Tenant'] && isHost,
    canImpersonateUsers: !!policies['AbpAdmin.Impersonation.User'],
    // T3.4 数据字典（EasyAbp 模块权限，直接引用模块的权限名，不复制常量）
    canViewDataDictionary:
      !!policies['EasyAbp.Abp.DataDictionary.DataDictionary'],
    canCreateDataDictionary:
      !!policies['EasyAbp.Abp.DataDictionary.DataDictionary.Create'],
    canUpdateDataDictionary:
      !!policies['EasyAbp.Abp.DataDictionary.DataDictionary.Update'],
    canDeleteDataDictionary:
      !!policies['EasyAbp.Abp.DataDictionary.DataDictionary.Delete'],
    // T3.5 通知管理页（看全量发送记录用模块的 Manage；用户侧铃铛不依赖它）
    canManageNotifications:
      !!policies['EasyAbp.NotificationService.Notification.Manage'],
    // 动态菜单管理（结构维护；可见性跟随 ABP 功能权限）
    canManageMenus: !!policies['AbpAdmin.Menus'],
    canCreateMenus: !!policies['AbpAdmin.Menus.Create'],
    canUpdateMenus: !!policies['AbpAdmin.Menus.Update'],
    canDeleteMenus: !!policies['AbpAdmin.Menus.Delete'],
    // 租户套餐（Host 专属）
    canManageTenantPackages: !!policies['AbpAdmin.TenantPackages'] && isHost,
    canCreateTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Create'] && isHost,
    canUpdateTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Update'] && isHost,
    canDeleteTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Delete'] && isHost,

    // —— 基础财务层（定稿 §5.2 · 后端 FinancePermissions）——
    // 权限码字符串必须与后端权限定义一字不差；改名先改后端再改这里与 routes access。
    // localStorage finance.shellAccess=1：仅放开前端菜单/按钮看壳；接口仍 403，不是绕权。
    // 不变量：canApproveOwnFinanceAdjust / canFinanceScopeAgent 故意不吃 shellAccess。
    // 调账动作与五态：Create/Submit/Approve/Reject/Reverse + ApproveOwn（自批，默认关）。
    canViewFinanceLedger,
    canViewFinanceOverviewStats,
    canOpenFinanceOverview:
      canViewFinanceOverviewStats || canViewFinanceConfiguration,
    canViewFinanceReconcile: canViewFinanceLedger && !financeAgentScoped,
    canExportFinanceLedger: !!policies['Finance.Ledger.Export'] || financeShell,
    canManageFinanceAccounts:
      !!policies['Finance.Account.Manage'] || financeShell,
    // A2 科目页可见：读权限 = Ledger.View | Account.Manage（与登录落地 / 产品清单一致）
    canViewFinanceAccounts,
    canViewFinanceRules,
    canViewFinanceVouchers,
    canViewFinanceManualAdjusts,
    canOpenFinanceVouchersWorkspace:
      canViewFinanceVouchers || canViewFinanceManualAdjusts,
    canReverseFinanceVouchers:
      !!policies['Finance.Voucher.Reverse'] || financeShell,
    canCreateFinanceAdjust: !!policies['Finance.Adjust.Create'] || financeShell,
    canApproveFinanceAdjust:
      !!policies['Finance.Adjust.Approve'] || financeShell,
    canSubmitFinanceAdjust: !!policies['Finance.Adjust.Submit'] || financeShell,
    canRejectFinanceAdjust: !!policies['Finance.Adjust.Reject'] || financeShell,
    canReverseFinanceAdjust:
      !!policies['Finance.Adjust.Reverse'] || financeShell,
    canApproveOwnFinanceAdjust: !!policies['Finance.Adjust.ApproveOwn'],
    canRunFinanceReconcile: !!policies['Finance.Reconcile.Run'] || financeShell,
    canViewFinancePeriod,
    canViewFinanceBizDocs: !!policies['Finance.BizDoc.View'] || financeShell,
    canViewFinanceBalances: !!policies['Finance.Balance.View'] || financeShell,
    canFinanceScopeAgent: financeAgentScoped,
    canViewFinanceConfiguration,
    // 财务总菜单可见性：产品主口径是 Finance.Ledger.View；子页各绑各的码。
    // 代理若只有 BizDoc/Balance.View，父路由 access=false 会整枝藏掉子菜单，
    // 故父级用「任一子页权限 ∪ shellAccess」并集；细权仍在子路由 / 按钮上。
    canViewFinance:
      canViewFinanceConfiguration ||
      canViewFinanceManualAdjusts ||
      !!policies['Finance.BizDoc.View'] ||
      !!policies['Finance.Balance.View'] ||
      canViewBalanceAdjustments,
  };
}
