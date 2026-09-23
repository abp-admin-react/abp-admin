import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as languageService from '@/services/abpadmin/language';

// T2.2 验收：覆盖「列表渲染与无权限时按钮隐藏」。
// 权限可变的 mock：默认有管理权限，单个用例可覆盖
let mockAccess: Record<string, boolean> = {
  canManageLanguages: true,
  canChangeDefaultLanguage: true,
};

vi.mock('@umijs/max', () => ({
  useAccess: () => mockAccess,
}));

// Mock 生成的服务层（生成代码不可手改，mock 模块边界）
vi.mock('@/services/abpadmin/language', async () => {
  const actual = await vi.importActual<
    typeof import('@/services/abpadmin/language')
  >('@/services/abpadmin/language');
  return {
    ...actual,
    getApiAppLanguage: vi.fn(),
    postApiAppLanguage: vi.fn(),
    putApiAppLanguageId: vi.fn(),
    deleteApiAppLanguageId: vi.fn(),
    postApiAppLanguageIdSetAsDefault: vi.fn(),
  };
});

// Mock ProComponents：保留语义化 testid，便于断言
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: any) => (
    <div data-testid="page-container">{children}</div>
  ),
  ProTable: ({ columns, toolbar, request }: any) => {
    request?.({}, {}, {});
    return (
      <div data-testid="pro-table">
        <div data-testid="table-columns">
          {columns?.map((col: any, i: number) => (
            <div key={col.dataIndex ?? col.key ?? i} data-testid="column">
              {col.title}
            </div>
          ))}
        </div>
        <div data-testid="toolbar">{toolbar?.actions}</div>
      </div>
    );
  },
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
    mockAccess = { canManageLanguages: true, canChangeDefaultLanguage: true };
    vi.mocked(languageService.getApiAppLanguage).mockResolvedValue(
      mockLanguages as never,
    );
  });

  it('挂载即请求语言列表', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    await waitFor(() => {
      expect(languageService.getApiAppLanguage).toHaveBeenCalled();
    });
  });

  it('渲染规格要求的列', async () => {
    const { default: Page } = await import('./index');
    render(<Page />);

    const columns = await screen.findAllByTestId('column');
    const titles = columns.map((c) => c.textContent);
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

  it('无管理权限时「新增语言」按钮隐藏', async () => {
    mockAccess = { canManageLanguages: false, canChangeDefaultLanguage: false };
    const { default: Page } = await import('./index');
    render(<Page />);

    await screen.findByTestId('toolbar');
    expect(screen.queryByText('新增语言')).toBeNull();
  });
});
