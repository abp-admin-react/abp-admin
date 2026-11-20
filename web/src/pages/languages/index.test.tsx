import { postGridQuery } from '@/abp/gridQuery';

vi.mock('@/abp/gridQuery', () => ({ postGridQuery: vi.fn() }));
vi.mock('ag-grid-react', () => import('@/../tests/gridTestDouble'));

import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// T2.2 验收：覆盖「列表渲染与无权限时按钮隐藏」。
// 权限可变的 mock：默认有管理权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = {
  canManageLanguages: true,
  canCreateLanguages: true,
  canChangeDefaultLanguage: true,
};

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
  useLocation: () => ({
    pathname: '/administration/localization/languages',
    search: '',
  }),
}));

vi.mock('@/abp/localizationAdmin', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/abp/localizationAdmin')>()),
  getLanguages: vi.fn(),
  createLanguage: vi.fn(),
  updateLanguage: vi.fn(),
  deleteLanguage: vi.fn(),
  setDefaultLanguage: vi.fn(),
}));

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ModalForm: ({ children, title }: any) => (
    <div data-testid="modal-form" data-title={title}>
      {children}
    </div>
  ),
  ProFormText: ({ label }: any) => <input aria-label={label} />,
  ProFormSelect: ({ label }: any) => <select aria-label={label} />,
  ProFormSwitch: ({ label }: any) => (
    <input type="checkbox" aria-label={label} />
  ),
}));

const mockLanguages = {
  items: [
    {
      id: 'lang-1',
      cultureName: 'zh-Hans',
      uiCultureName: 'zh-Hans',
      displayName: 'Chinese (Simplified)',
      isEnabled: true,
      isDefault: true,
    },
    {
      id: 'lang-2',
      cultureName: 'en',
      uiCultureName: 'en',
      displayName: 'English',
      isEnabled: true,
      isDefault: false,
    },
  ],
};

describe('LanguagesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAccess = {
      canManageLanguages: true,
      canCreateLanguages: true,
      canChangeDefaultLanguage: true,
    };
    vi.mocked(postGridQuery).mockResolvedValue(mockLanguages as never);
  });

  it('挂载即请求语言列表', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    await waitFor(() => {
      expect(postGridQuery).toHaveBeenCalled();
    });
  });

  it('渲染规格要求的列', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    const columns = await screen.findAllByRole('columnheader');
    const titles = columns.map((c) => c.getAttribute('aria-label'));
    expect(titles).toContain('显示名');
    expect(titles).toContain('Culture Name');
    expect(titles).toContain('UI Culture Name');
    expect(titles).toContain('旗标');
    expect(titles).toContain('状态');
    expect(titles).toContain('默认');
    expect(titles).toContain('操作');
  });

  it('有管理权限时显示「新增语言」按钮', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    expect(await screen.findByText('新增语言')).toBeTruthy();
  });

  it('只有列表权限时隐藏「新增语言」', async () => {
    mockAccess = { canManageLanguages: true };
    const { default: Page } = await import('./index');
    render(<Page />);

    await screen.findByRole('searchbox');
    expect(screen.queryByText('新增语言')).toBeNull();
  });
});
