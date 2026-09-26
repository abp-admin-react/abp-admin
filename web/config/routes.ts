/**
 * 菜单层级参照 ABP 官方启动模板（https://abp.io/get-started）：
 * - 主菜单：Home + Administration（组内顺序 Saas(1) → Identity(2) → Settings(3)，见模板
 *   MyProjectNameMenuContributor），其余管理类模块（OpenIddict、本地化、文本模板、审计日志）同样挂在 Administration 下
 * - 项目自定义的运维页归入"系统"组，业务功能（文件/支付）挂在 Administration 末尾，
 *   演示页与个人信息（当前会话）保持顶级
 * - 历史路径（/identity/*、/saas/* 等）以 redirect 保留，兼容旧收藏夹
 */
export default [
  {
    path: '/user',
    layout: false,
    routes: [
      {
        name: 'login',
        path: '/user/login',
        component: './user/login',
      },
      {
        name: 'callback',
        path: '/user/callback',
        component: './user/callback',
        hideInMenu: true,
      },
      {
        name: 'two-factor',
        path: '/user/two-factor',
        component: './user/two-factor',
        hideInMenu: true,
      },
    ],
  },
  {
    path: '/tenant-not-found',
    layout: false,
    component: './tenant-not-found',
  },
  {
    path: '/administration',
    name: 'administration',
    icon: 'appstore',
    routes: [
      {
        path: '/administration/saas',
        name: 'saas',
        routes: [
          {
            path: '/administration/saas/tenants',
            name: 'tenants',
            access: 'canManageTenants',
            component: './tenants',
          },
      {
        path: '/administration/saas/editions',
        name: 'editions',
        access: 'canManageEditions',
        component: './saas/editions',
      },
      {
        path: '/administration/saas/tenant-packages',
        name: 'tenant-packages',
        icon: 'cluster',
        access: 'canManageTenantPackages',
        component: './saas/tenant-packages',
      },
        ],
      },
      {
        path: '/administration/identity',
        name: 'identity',
        icon: 'team',
        routes: [
          {
            path: '/administration/identity/users',
            name: 'users',
            access: 'canManageUsers',
            component: './identity/users',
          },
          {
            path: '/administration/identity/roles',
            name: 'roles',
            access: 'canManageRoles',
            component: './identity/roles',
          },
          {
            path: '/administration/identity/organization-units',
            name: 'organization-units',
            access: 'canManageOrganizationUnits',
            component: './identity/organization-units',
          },
          {
            path: '/administration/identity/posts',
            name: 'posts',
            access: 'canManagePosts',
            component: './identity/posts',
          },
          {
            path: '/administration/identity/claim-types',
            name: 'claim-types',
            access: 'canManageClaimTypes',
            component: './identity/claim-types',
          },
          {
            path: '/administration/identity/security-logs',
            name: 'security-logs',
            access: 'canManageSecurityLogs',
            component: './identity/security-logs',
          },
          {
            path: '/administration/identity/sessions',
            name: 'sessions',
            access: 'canManageSessions',
            component: './identity/sessions',
          },
        ],
      },
      {
        path: '/administration/openiddict',
        name: 'openiddict',
        icon: 'safety',
        access: 'canManageOpenIddict',
        routes: [
          {
            path: '/administration/openiddict/applications',
            name: 'applications',
            access: 'canManageOpenIddictApplications',
            component: './openiddict/applications',
          },
      {
        path: '/administration/openiddict/scopes',
        name: 'scopes',
        access: 'canManageOpenIddictScopes',
        component: './openiddict/scopes',
      },
      {
        path: '/administration/openiddict/token-management',
        name: 'token-management',
        access: 'canManageOpenIddictTokens',
        component: './openiddict/token-management',
      },
        ],
      },
      {
        path: '/administration/features',
        name: 'features',
        icon: 'control',
        access: 'canManageHostFeatures',
        component: './features',
      },
      {
        path: '/administration/localization',
        name: 'localization',
        icon: 'global',
        routes: [
          {
            path: '/administration/localization/languages',
            name: 'languages',
            access: 'canManageLanguages',
            component: './languages',
          },
          {
            path: '/administration/localization/texts',
            name: 'language-texts',
            access: 'canManageLanguageTexts',
            component: './languages/texts',
          },
        ],
      },
      {
        path: '/administration/text-templates',
        name: 'text-templates',
        icon: 'fileText',
        access: 'canManageTextTemplates',
        component: './text-templates',
      },
      {
        path: '/administration/audit-logs',
        name: 'audit-logs',
        icon: 'fileSearch',
        access: 'canManageAuditLogs',
        component: './audit-logs',
      },
      {
        path: '/administration/operation-logs',
        name: 'operation-logs',
        icon: 'history',
        access: 'canManageOperationLogs',
        component: './operation-logs',
      },
      {
        path: '/administration/files',
        name: 'files',
        icon: 'folder',
        access: 'canManageFiles',
        component: './files',
      },
      {
        path: '/administration/payments',
        name: 'payments',
        icon: 'payCircle',
        access: 'canManagePayments',
        component: './payments',
      },
      {
        path: '/administration/menus',
        name: 'menus',
        icon: 'menu',
        access: 'canManageMenus',
        component: './menus',
      },
      {
        path: '/administration/settings',
        name: 'settings',
        icon: 'setting',
        access: 'canManageSettings',
        component: './settings',
      },
    ],
  },
  {
    path: '/system',
    name: 'system',
    icon: 'desktop',
    routes: [
      {
        path: '/system/background-jobs',
        name: 'background-jobs',
        icon: 'thunderbolt',
        access: 'canManageBackgroundJobs',
        component: './background-jobs',
      },
      {
        path: '/system/scheduled-jobs',
        name: 'scheduled-jobs',
        icon: 'clockCircle',
        access: 'canManageScheduledJobs',
        component: './scheduled-jobs',
      },
      {
        path: '/system/notifications',
        name: 'notifications',
        icon: 'bell',
        access: 'canManageNotifications',
        component: './notifications',
      },
      {
        path: '/system/data-dictionary',
        name: 'data-dictionary',
        icon: 'table',
        access: 'canViewDataDictionary',
        component: './data-dictionary',
      },
      {
        path: '/system/virtual-file-explorer',
        name: 'virtual-file-explorer',
        icon: 'folderOpen',
        access: 'canManageVirtualFileExplorer',
        component: './virtual-file-explorer',
      },
      {
        path: '/system/server-monitor',
        name: 'server-monitor',
        icon: 'dashboard',
        access: 'canViewServerMonitor',
        component: './system/server-monitor',
      },
      {
        path: '/system/cache-monitor',
        name: 'cache-monitor',
        icon: 'hdd',
        access: 'canViewCacheMonitor',
        component: './system/cache-monitor',
      },
    ],
  },
  {
    path: '/data-scope-demo',
    name: 'data-scope-demo',
    icon: 'database',
    access: 'canManageDataScopeDemo',
    component: './data-scope-demo',
  },
  {
    path: '/current-session',
    name: 'current-session',
    icon: 'user',
    component: './current-session',
  },
  {
    path: '/account/center',
    name: 'account-center',
    icon: 'user',
    hideInMenu: true,
    component: './account/center',
  },
  {
    path: '/account/force-change-password',
    hideInMenu: true,
    component: './account/force-change-password',
  },
  {
    path: '/',
    redirect: '/administration',
  },
  {
    // 历史路径兼容：旧菜单层级下的地址重定向到新层级
    path: '/saas/tenants',
    redirect: '/administration/saas/tenants',
  },
  {
    path: '/saas/editions',
    redirect: '/administration/saas/editions',
  },
  {
    path: '/tenants',
    redirect: '/administration/saas/tenants',
  },
  {
    path: '/identity',
    redirect: '/administration/identity/users',
  },
  {
    path: '/identity/users',
    redirect: '/administration/identity/users',
  },
  {
    path: '/identity/roles',
    redirect: '/administration/identity/roles',
  },
  {
    path: '/identity/organization-units',
    redirect: '/administration/identity/organization-units',
  },
  {
    path: '/identity/claim-types',
    redirect: '/administration/identity/claim-types',
  },
  {
    path: '/identity/security-logs',
    redirect: '/administration/identity/security-logs',
  },
  {
    path: '/identity/sessions',
    redirect: '/administration/identity/sessions',
  },
  {
    path: '/openiddict',
    redirect: '/administration/openiddict/applications',
  },
  {
    path: '/openiddict/applications',
    redirect: '/administration/openiddict/applications',
  },
  {
    path: '/openiddict/scopes',
    redirect: '/administration/openiddict/scopes',
  },
  {
    path: '/features',
    redirect: '/administration/features',
  },
  {
    path: '/localization',
    redirect: '/administration/localization/languages',
  },
  {
    path: '/localization/languages',
    redirect: '/administration/localization/languages',
  },
  {
    path: '/localization/texts',
    redirect: '/administration/localization/texts',
  },
  {
    path: '/text-templates',
    redirect: '/administration/text-templates',
  },
  {
    path: '/audit-logs',
    redirect: '/administration/audit-logs',
  },
  {
    path: '/settings',
    redirect: '/administration/settings',
  },
  {
    path: '/background-jobs',
    redirect: '/system/background-jobs',
  },
  {
    path: '/scheduled-jobs',
    redirect: '/system/scheduled-jobs',
  },
  {
    path: '/notifications',
    redirect: '/system/notifications',
  },
  {
    path: '/data-dictionary',
    redirect: '/system/data-dictionary',
  },
  {
    path: '/virtual-file-explorer',
    redirect: '/system/virtual-file-explorer',
  },
  {
    path: '/files',
    redirect: '/administration/files',
  },
  {
    path: '/payments',
    redirect: '/administration/payments',
  },
  {
    component: './exception/404',
    layout: false,
    path: './*',
  },
];
