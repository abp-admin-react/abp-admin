import { LinkOutlined } from '@ant-design/icons';
import type { Settings as LayoutSettings } from '@ant-design/pro-components';
import { SettingDrawer } from '@ant-design/pro-components';
import { QueryClientProvider } from '@tanstack/react-query';
import type { RequestConfig, RunTimeLayoutConfig } from '@umijs/max';
import { history, Link } from '@umijs/max';
import { Tag } from 'antd';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import React from 'react';

dayjs.extend(relativeTime);

import { getApplicationConfiguration } from '@/abp/config';
import { getCurrentAccountStatus } from '@/abp/identity';
import { getMyAvatarInfo } from '@/abp/imaging';
import { toMenuIcon } from '@/abp/menuIcons';
import { getMyMenu, type MyMenuItemDto } from '@/abp/menus';
import { getUserManager } from '@/abp/oidc';
import { syncTenantFromSubdomain } from '@/abp/subdomain';
import type { AbpCurrentTenant } from '@/abp/types';
import {
  AvatarDropdown,
  CookieConsent,
  ErrorBoundary,
  IdleSessionWatcher,
  ImpersonationBanner,
  LangDropdown,
  NotificationBell,
  OfflineBanner,
  RealTimeConnection,
  VersionDropdown,
} from '@/components';
import { queryClient } from '@/queryClient';
import defaultSettings from '../config/defaultSettings';
import { errorConfig } from './requestErrorConfig';

const isDev = process.env.NODE_ENV === 'development';
const loginPath = '/user/login';
const forceChangePasswordPath = '/account/force-change-password';
const publicPaths = [loginPath, '/user/callback', '/tenant-not-found'];
/** 需要登录但不需要检查密码过期的路径 */
const skipPasswordCheckPaths = [forceChangePasswordPath];

/** 判定后端下发的菜单 path 是否为站内路由形态。
 * 服务端菜单管理已拒绝外链（//、://、非 / 开头），此处是渲染前的第二道防线：
 * 历史脏数据或绕过校验的行直接丢弃，绝不让外链进入可信侧边栏。 */
function isSafeMenuPath(path?: string | null): path is string {
  return (
    !!path &&
    path.startsWith('/') &&
    !path.startsWith('//') &&
    !path.includes('://')
  );
}

/** 后端动态菜单 → ProLayout MenuDataItem。
 * locale 键由本函数沿树拼完整链（menu.a.b.c）：节点因隐藏祖先上浮后树层级变化，
 * ProLayout 按 name 链自动拼的键会与语言包错位；显式传完整键 + name=title 兜底，
 * 有翻译用翻译、无翻译显示后端 Title（不再露出 name 尾段）。
 * 目录无 path 时合成占位 path（ProLayout 要求菜单项有 path 或 children）。 */
function toMenuData(
  items: MyMenuItemDto[],
  parentLocaleKey = 'menu',
): Record<string, unknown>[] {
  return items
    .filter(
      (item) =>
        item.path === null ||
        item.path === undefined ||
        isSafeMenuPath(item.path),
    )
    .map((item, index) => {
      const localeKey = item.name
        ? `${parentLocaleKey}.${item.name}`
        : `${parentLocaleKey}.x${index}`;
      return {
        path:
          item.path ?? `#catalog-${encodeURIComponent(item.title)}-${index}`,
        name: item.title,
        locale: localeKey,
        icon: toMenuIcon(item.icon),
        children: item.children?.length
          ? toMenuData(item.children, localeKey)
          : undefined,
      };
    });
}

/** ABP 当前用户：getApplicationConfiguration 映射产物（模板 API.CurrentUser 已随
 * 脚手架演示层删除，此处自持字段清单——以 app.tsx 的 mapped 赋值为准） */
export type AppCurrentUser = {
  name?: string;
  userid?: string;
  email?: string;
  userName?: string;
  roles?: string[];
  access?: 'admin' | 'user';
  avatar?: string;
};

export type AppInitialState = {
  settings?: Partial<LayoutSettings>;
  currentUser?: AppCurrentUser;
  currentTenant?: AbpCurrentTenant;
  grantedPolicies?: Record<string, boolean>;
  settingValues?: Record<string, string>;
  tenantMissing?: boolean;
  loading?: boolean;
  fetchUserInfo?: () => Promise<AppCurrentUser | undefined>;
  settingDrawerOpen?: boolean;
  /** 后端 SignalR:Enabled 开关（T3.2），缺省视为 true */
  signalrEnabled?: boolean;
  /** 字典标签色白名单（T3.4，来自 application-configuration 的 extraProperties） */
  dataDictionaryTagTypes?: string[];
};

export async function getInitialState(): Promise<AppInitialState> {
  const fetchUserInfo = async () => {
    try {
      const user = await getUserManager().getUser();
      if (!user || user.expired) {
        return undefined;
      }
      const config = await getApplicationConfiguration();
      if (!config.currentUser?.isAuthenticated) {
        return undefined;
      }
      const mapped: AppCurrentUser = {
        name: config.currentUser.name || config.currentUser.userName || '',
        userid: config.currentUser.id || '',
        email: config.currentUser.email || '',
        userName: config.currentUser.userName || '',
        roles: config.currentUser.roles || [],
        access: config.auth?.grantedPolicies?.['AbpIdentity.Users']
          ? 'admin'
          : 'user',
      };
      // T3.1：头像 URL 由后端下发（含版本参数），前端不拼规则；
      // 取不到不阻塞登录态
      try {
        const avatarInfo = await getMyAvatarInfo();
        mapped.avatar = avatarInfo.avatarUrl || undefined;
      } catch {
        // ignore
      }
      (
        fetchUserInfo as typeof fetchUserInfo & { lastConfig?: typeof config }
      ).lastConfig = config;
      return mapped;
    } catch {
      return undefined;
    }
  };

  const { location } = history;
  const subdomain = await syncTenantFromSubdomain();
  if (subdomain.mode === 'missing') {
    if (location.pathname !== '/tenant-not-found') {
      history.replace('/tenant-not-found');
    }
    return {
      fetchUserInfo,
      tenantMissing: true,
      settings: defaultSettings as Partial<LayoutSettings>,
      settingDrawerOpen: false,
    };
  }

  if (publicPaths.includes(location.pathname)) {
    return {
      fetchUserInfo,
      settings: defaultSettings as Partial<LayoutSettings>,
      settingDrawerOpen: false,
    };
  }

  const currentUser = await fetchUserInfo();
  const lastConfig = (
    fetchUserInfo as typeof fetchUserInfo & {
      lastConfig?: Awaited<ReturnType<typeof getApplicationConfiguration>>;
    }
  ).lastConfig;

  if (!currentUser) {
    history.replace(
      `${loginPath}?redirect=${encodeURIComponent(location.pathname + location.search)}`,
    );
  } else if (!skipPasswordCheckPaths.includes(location.pathname)) {
    // 检查是否需要强制改密（密码过期或管理员要求）
    try {
      const accountStatus = await getCurrentAccountStatus();
      if (accountStatus.shouldChangePassword) {
        history.replace(forceChangePasswordPath);
      }
    } catch {
      // 接口异常时不阻塞正常流程
    }
  }

  return {
    fetchUserInfo,
    currentUser,
    currentTenant: lastConfig?.currentTenant,
    grantedPolicies: lastConfig?.auth?.grantedPolicies,
    settingValues: lastConfig?.setting?.values,
    // T3.2：后端 SignalR:Enabled=false 时前端不尝试连接（降级轮询），缺省视为开启
    signalrEnabled: lastConfig?.extraProperties?.signalr?.enabled !== false,
    // T3.4：字典标签色白名单（后端 DataDictionaryTagTypes.All），字典编辑下拉的唯一来源
    dataDictionaryTagTypes: lastConfig?.extraProperties?.dataDictionaryTagTypes,
    settings: defaultSettings as Partial<LayoutSettings>,
    settingDrawerOpen: false,
  };
}

export const layout: RunTimeLayoutConfig = ({
  initialState,
  setInitialState,
}) => {
  return {
    // 动态菜单：服务端按混合授权（权限 + 角色勾选）下发可见菜单树；
    // 请求失败时 ProLayout 回退到 config/routes.ts 的静态菜单。
    // 注意：运行时配置会整体覆盖默认 menu 对象，locale 必须显式带 true，
    // 否则菜单标题的 menu.* 国际化会失效（ProLayout getMenuData 以 menu?.locale 判断）。
    menu: {
      locale: true,
      params: {
        user: initialState?.currentUser?.userid ?? '',
        tenant: initialState?.currentTenant?.id ?? 'host',
      },
      request: async () => {
        const res = await getMyMenu();
        return toMenuData(res.items);
      },
    },
    menuItemRender: (item, dom) => {
      if (item.path) {
        return (
          <Link to={item.path} prefetch>
            {dom}
          </Link>
        );
      }
      return dom;
    },
    actionsRender: () => {
      const localeEnabled =
        (initialState?.settings as { locale?: boolean })?.locale !== false;
      const tenant = initialState?.currentTenant;
      const loggedIn = !!initialState?.currentUser?.userid;
      return [
        tenant?.isAvailable ? (
          <Tag key="tenant" color="blue">
            {tenant.name}
          </Tag>
        ) : (
          <Tag key="tenant">Host</Tag>
        ),
        // T3.5：通知铃铛（所有登录用户可见；数据源是"我的通知"端点，不依赖 Manage 权限）
        loggedIn && <NotificationBell key="notification" />,
        <VersionDropdown key="version" />,
        localeEnabled && <LangDropdown key="lang" />,
      ].filter(Boolean);
    },
    avatarProps: {
      src: initialState?.currentUser?.avatar,
      title:
        initialState?.currentUser?.userName || initialState?.currentUser?.name,
      render: (_, avatarChildren) => (
        <AvatarDropdown>{avatarChildren}</AvatarDropdown>
      ),
    },
    onPageChange: () => {
      const { location } = history;
      if (initialState?.tenantMissing) {
        if (location.pathname !== '/tenant-not-found') {
          history.replace('/tenant-not-found');
        }
        return;
      }
      if (
        !initialState?.currentUser &&
        !publicPaths.includes(location.pathname) &&
        !skipPasswordCheckPaths.includes(location.pathname)
      ) {
        history.replace(
          `${loginPath}?redirect=${encodeURIComponent(location.pathname + location.search)}`,
        );
      }
    },
    links: isDev
      ? [
          <Link key="openapi" to="/umi/plugin/openapi" target="_blank">
            <LinkOutlined />
            <span>OpenAPI 文档</span>
          </Link>,
        ]
      : [],
    ErrorBoundary,
    menuHeaderRender: undefined,
    childrenRender: (children) => {
      return (
        <>
          {/* T2.7：模拟登录状态下顶部固定不可关闭的警告条 */}
          <ImpersonationBanner />
          <IdleSessionWatcher />
          {/* T3.2：登录后建立 SignalR 连接（T3.5 的 ReceiveNotification 等处理器已挂载） */}
          <RealTimeConnection />
          {children}
          <SettingDrawer
            disableUrlParams
            enableDarkTheme
            collapse={initialState?.settingDrawerOpen}
            onCollapseChange={(open) => {
              setInitialState((s) => ({
                ...s,
                settingDrawerOpen: open,
              }));
            }}
            settings={initialState?.settings}
            onSettingChange={(settings) => {
              setInitialState((s) => ({
                ...s,
                settings,
              }));
            }}
          />
        </>
      );
    },
    ...initialState?.settings,
  };
};

export const request: RequestConfig = {
  baseURL: '',
  ...errorConfig,
};

export function rootContainer(container: React.ReactNode) {
  return (
    <>
      <OfflineBanner />
      <ErrorBoundary>
        {/* T3.4：全局唯一的 QueryClientProvider（实例见 src/queryClient.ts），
            工具函数 dictionaryRequest 与 useDictionary 共享同一份缓存 */}
        <QueryClientProvider client={queryClient}>
          {container}
        </QueryClientProvider>
      </ErrorBoundary>
      <CookieConsent />
    </>
  );
}
