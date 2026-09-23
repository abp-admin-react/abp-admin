/**
 * @see https://umijs.org/docs/max/access#access
 * */
export default function access(
  initialState:
    | {
        currentUser?: API.CurrentUser;
        grantedPolicies?: Record<string, boolean>;
        currentTenant?: { isAvailable?: boolean };
      }
    | undefined,
) {
  const policies = initialState?.grantedPolicies ?? {};
  const isHost = !initialState?.currentTenant?.isAvailable;
  return {
    canAdmin: !!policies['AbpIdentity.Users'],
    canManageUsers: !!policies['AbpIdentity.Users'],
    canManageRoles: !!policies['AbpIdentity.Roles'],
    // 角色页按钮级权限（对齐 ABP 内置 AbpIdentity.Roles 权限树）
    canUpdateRoles: !!policies['AbpIdentity.Roles.Update'],
    canDeleteRoles: !!policies['AbpIdentity.Roles.Delete'],
    // 角色数据范围入口（自研行级数据权限，RoleDataScopeAppService 类级 Manage 授权）
    canManageRoleDataScopes: !!policies['AbpAdmin.DataScopes.Manage'],
    canManageTenants: !!policies['AbpTenantManagement.Tenants'] && isHost,
    canManageHostFeatures:
      !!policies['FeatureManagement.ManageHostFeatures'] && isHost,
    canManageEmailing: !!policies['SettingManagement.Emailing'],
    canManageEmailingTest: !!policies['SettingManagement.Emailing.Test'],
    canManageTimezone: !!policies['SettingManagement.TimeZone'],
    canManageLanguages: !!policies['AbpAdmin.Languages'],
    canChangeDefaultLanguage: !!policies['AbpAdmin.Languages.ChangeDefault'],
    canManageLanguageTexts: !!policies['AbpAdmin.LanguageTexts'],
    canEditLanguageTexts: !!policies['AbpAdmin.LanguageTexts.Edit'],
    canManageOrganizationUnits: !!policies['AbpAdmin.OrganizationUnits'],
    // 岗位管理（对标 RuoYi sys_post）
    canManagePosts: !!policies['AbpAdmin.Posts'],
    canCreatePosts: !!policies['AbpAdmin.Posts.Create'],
    canUpdatePosts: !!policies['AbpAdmin.Posts.Update'],
    canDeletePosts: !!policies['AbpAdmin.Posts.Delete'],
    canManagePostMembers: !!policies['AbpAdmin.Posts.ManageMembers'],
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
    canManageEditions: !!policies['AbpAdmin.Editions'] && isHost,
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
    canManageTextTemplates: !!policies['AbpAdmin.TextTemplates'],
    canEditNonSandboxedTemplates:
      !!policies['AbpAdmin.TextTemplates.EditNonSandboxedContents'],
    canManageBackgroundJobs: !!policies['AbpAdmin.BackgroundJobs'],
    canManageScheduledJobs: !!policies['AbpAdmin.ScheduledJobs'],
    canManageVirtualFileExplorer: !!policies['AbpAdmin.VirtualFileExplorer'],
    // 服务监控 / 缓存监控（权限本身定义在 Host 侧，租户拿不到授予，这里无需再叠 isHost）
    canViewServerMonitor: !!policies['AbpAdmin.ServerMonitor'],
    canViewCacheMonitor: !!policies['AbpAdmin.CacheMonitor'],
    canManageCache: !!policies['AbpAdmin.CacheMonitor.Manage'],
    // SettingUi 通用设置页权限（T1.4）
    canManageSettings: !!policies['SettingUi.ShowSettingPage'],
    // SettingUi 写权限（读写分离）：后端 AbpAdminSettingUiAppService 方法级 Authorize，
    // 前端只做按钮态联动，防线在后端
    canUpdateSettings: !!policies['AbpAdmin.SettingUi.Update'],
    // 数据范围演示（T1.5）
    canManageDataScopeDemo: !!policies['AbpAdmin.DataScopes.Demo'],
    // T2.6 Identity Pro 缺口：用户导入导出
    canImportUsers: !!policies['AbpAdmin.Identity.Users.Import'],
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
    canManagePayments: !!policies['AbpAdmin.Payments'],
    // 动态菜单管理（菜单结构维护 + 角色分配）
    canManageMenus: !!policies['AbpAdmin.Menus'],
    canCreateMenus: !!policies['AbpAdmin.Menus.Create'],
    canUpdateMenus: !!policies['AbpAdmin.Menus.Update'],
    canDeleteMenus: !!policies['AbpAdmin.Menus.Delete'],
    canAssignMenuRoles: !!policies['AbpAdmin.Menus.AssignRoles'],
    // 租户套餐（Host 专属）
    canManageTenantPackages: !!policies['AbpAdmin.TenantPackages'] && isHost,
    canCreateTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Create'] && isHost,
    canUpdateTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Update'] && isHost,
    canDeleteTenantPackages:
      !!policies['AbpAdmin.TenantPackages.Delete'] && isHost,
  };
}
