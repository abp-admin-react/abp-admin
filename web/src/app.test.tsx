import { beforeEach, describe, expect, it, vi } from 'vitest';

// Mock all heavy dependencies before importing app
const mockReplace = vi.fn();
const mockHistory = {
  location: {
    pathname: '/welcome',
    search: '',
    hash: '',
  },
  replace: mockReplace,
};

const mockGetUser = vi.fn();
const mockGetApplicationConfiguration = vi.fn();
const mockGetCurrentAccountStatus = vi.fn();
const mockSyncTenantFromSubdomain = vi.fn();

vi.mock('@umijs/max', () => ({
  history: mockHistory,
  Link: ({ children }: any) => children,
}));

vi.mock('@/abp/oidc', () => ({
  getUserManager: () => ({
    getUser: mockGetUser,
  }),
}));

vi.mock('@/abp/config', () => ({
  getApplicationConfiguration: mockGetApplicationConfiguration,
}));

vi.mock('@/abp/identity', () => ({
  getCurrentAccountStatus: mockGetCurrentAccountStatus,
}));

vi.mock('@/abp/subdomain', () => ({
  syncTenantFromSubdomain: mockSyncTenantFromSubdomain,
}));

vi.mock('@/components', () => ({
  AvatarDropdown: () => null,
  CookieConsent: () => null,
  DocLink: () => null,
  ErrorBoundary: ({ children }: any) => children,
  Footer: () => null,
  ImpersonationBanner: () => null,
  LangDropdown: () => null,
  OfflineBanner: () => null,
  VersionDropdown: () => null,
}));

vi.mock('@ant-design/pro-components', () => ({
  SettingDrawer: () => null,
}));

vi.mock('@ant-design/icons', () => {
  // app.tsx（LinkOutlined）与动态菜单图标映射 src/abp/menuIcons.ts 都从这里取名
  const icons = [
    'LinkOutlined',
    'SmileOutlined',
    'AppstoreOutlined',
    'ClusterOutlined',
    'TeamOutlined',
    'SafetyOutlined',
    'ControlOutlined',
    'GlobalOutlined',
    'HistoryOutlined',
    'FileTextOutlined',
    'FileSearchOutlined',
    'FolderOutlined',
    'PayCircleOutlined',
    'SettingOutlined',
    'DesktopOutlined',
    'ThunderboltOutlined',
    'ClockCircleOutlined',
    'BellOutlined',
    'TableOutlined',
    'FolderOpenOutlined',
    'DatabaseOutlined',
    'UserOutlined',
    'MenuOutlined',
  ];
  return Object.fromEntries(icons.map((name) => [name, () => null]));
});

vi.mock('./requestErrorConfig', () => ({
  errorConfig: {},
}));

vi.mock('../config/defaultSettings', () => ({
  default: { navTheme: 'light' },
}));

/** 让 OIDC 会话与 ABP 应用配置都表现为“已登录的管理员” */
function mockAuthenticatedUser() {
  mockGetUser.mockResolvedValue({ expired: false });
  mockGetApplicationConfiguration.mockResolvedValue({
    currentUser: {
      isAuthenticated: true,
      id: '1',
      userName: 'tester',
      name: 'Test User',
      email: 'test@example.com',
      roles: ['admin'],
    },
    auth: { grantedPolicies: { 'AbpIdentity.Users': true } },
    currentTenant: { isAvailable: false },
    setting: { values: {} },
  });
}

describe('app getInitialState', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockHistory.location = {
      pathname: '/welcome',
      search: '',
      hash: '',
    };
    mockSyncTenantFromSubdomain.mockResolvedValue({ mode: 'host' });
    mockGetCurrentAccountStatus.mockResolvedValue({
      shouldChangePassword: false,
    });
  });

  it('should fetch currentUser when not on login page', async () => {
    const { getInitialState } = await import('./app');
    mockAuthenticatedUser();

    const state = await getInitialState();

    expect(mockGetApplicationConfiguration).toHaveBeenCalled();
    expect(state.currentUser).toEqual({
      name: 'Test User',
      userid: '1',
      email: 'test@example.com',
      userName: 'tester',
      roles: ['admin'],
      access: 'admin',
    });
    expect(state.grantedPolicies).toEqual({ 'AbpIdentity.Users': true });
    expect(state.settingDrawerOpen).toBe(false);
    expect(state.fetchUserInfo).toBeDefined();
  });

  it('should redirect to login when currentUser fetch fails (401)', async () => {
    const { getInitialState } = await import('./app');
    // 无有效 OIDC 会话 → fetchUserInfo 返回 undefined
    mockGetUser.mockResolvedValue(null);

    const state = await getInitialState();

    expect(mockReplace).toHaveBeenCalledWith(
      expect.stringContaining('/user/login?redirect='),
    );
    expect(state.currentUser).toBeUndefined();
  });

  it('should not fetch currentUser on login page', async () => {
    const { getInitialState } = await import('./app');
    mockHistory.location = {
      pathname: '/user/login',
      search: '',
      hash: '',
    };

    const state = await getInitialState();

    expect(mockGetUser).not.toHaveBeenCalled();
    expect(state.currentUser).toBeUndefined();
    expect(state.fetchUserInfo).toBeDefined();
  });

  it('should encode redirect path correctly on 401', async () => {
    const { getInitialState } = await import('./app');
    mockHistory.location = {
      pathname: '/admin/users',
      search: '?page=2',
      hash: '#section',
    };
    mockGetUser.mockResolvedValue(null);

    await getInitialState();

    // 源码只回跳 pathname + search（不含 hash）
    expect(mockReplace).toHaveBeenCalledWith(
      `/user/login?redirect=${encodeURIComponent('/admin/users?page=2')}`,
    );
  });

  it('should include default settings in initial state', async () => {
    const { getInitialState } = await import('./app');
    mockAuthenticatedUser();

    const state = await getInitialState();

    expect(state.settings).toEqual({ navTheme: 'light' });
  });

  it('fetchUserInfo should return user data on success', async () => {
    const { getInitialState } = await import('./app');
    mockAuthenticatedUser();

    const state = await getInitialState();

    const user = await state.fetchUserInfo?.();
    expect(user).toEqual({
      name: 'Test User',
      userid: '1',
      email: 'test@example.com',
      userName: 'tester',
      roles: ['admin'],
      access: 'admin',
    });
  });
});
