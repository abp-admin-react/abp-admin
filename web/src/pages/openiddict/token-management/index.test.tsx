import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { beforeEach, expect, it, vi } from 'vitest';
import TokenManagement from './index';

const mocks = vi.hoisted(() => ({
  access: vi.fn(),
  applications: vi.fn(),
  tokens: vi.fn(),
  authorizations: vi.fn(),
}));

vi.mock('@umijs/max', () => ({ useAccess: mocks.access }));
vi.mock('@/abp/openIddictApplications', () => ({
  getOpenIddictApplications: mocks.applications,
}));
vi.mock('@/abp/openIddictTokens', async (actual) => ({
  ...(await actual<typeof import('@/abp/openIddictTokens')>()),
  getOpenIddictTokens: mocks.tokens,
  getOpenIddictAuthorizations: mocks.authorizations,
  pruneOpenIddictTokens: vi.fn(),
  revokeOpenIddictAuthorization: vi.fn(),
  revokeOpenIddictToken: vi.fn(),
  revokeOpenIddictTokensBySubject: vi.fn(),
}));
vi.mock('ag-grid-react', () => import('../../../../tests/gridTestDouble'));
vi.mock('@/abp/gridQuery', () => ({
  postGridQuery: (url: string, grid: unknown) =>
    url.endsWith('/query-tokens')
      ? mocks.tokens(grid)
      : mocks.authorizations(grid),
}));

beforeEach(() => {
  vi.resetAllMocks();
  for (const fn of [mocks.applications, mocks.tokens, mocks.authorizations]) {
    fn.mockResolvedValue({ items: [], totalCount: 0 });
  }
});

it('只授权令牌与授权页面时，读取两个列表而不查询应用目录', async () => {
  mocks.access.mockReturnValue({ canManageOpenIddictTokens: true });
  render(
    <App>
      <TokenManagement />
    </App>,
  );
  await waitFor(() => expect(mocks.tokens).toHaveBeenCalled());
  expect(screen.queryByRole('combobox', { name: '应用' })).toBeNull();
  fireEvent.click(screen.getByRole('tab', { name: '授权' }));
  await waitFor(() => expect(mocks.authorizations).toHaveBeenCalled());
  expect(mocks.applications).not.toHaveBeenCalled();
});

it('同时具备应用权限时提供应用筛选', async () => {
  mocks.access.mockReturnValue({
    canManageOpenIddictTokens: true,
    canManageOpenIddictApplications: true,
  });
  render(
    <App>
      <TokenManagement />
    </App>,
  );
  await waitFor(() => expect(mocks.applications).toHaveBeenCalledOnce());
  expect(screen.getByRole('combobox', { name: '应用' })).toBeTruthy();
});

it('切换令牌与授权时保留实际提交的筛选值', async () => {
  mocks.access.mockReturnValue({ canManageOpenIddictTokens: true });
  render(
    <App>
      <TokenManagement />
    </App>,
  );
  fireEvent.change(screen.getByLabelText('用户 Id'), {
    target: { value: 'subject-123' },
  });
  fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() =>
    expect(mocks.tokens).toHaveBeenLastCalledWith(
      expect.objectContaining({
        filters: [
          {
            field: 'subject',
            join: 'and',
            conditions: [{ op: 'eq', value: 'subject-123' }],
          },
        ],
      }),
    ),
  );
  fireEvent.click(screen.getByRole('tab', { name: '授权' }));
  await waitFor(() =>
    expect(mocks.authorizations).toHaveBeenLastCalledWith(
      expect.objectContaining({
        filters: [
          {
            field: 'subject',
            join: 'and',
            conditions: [{ op: 'eq', value: 'subject-123' }],
          },
        ],
      }),
    ),
  );
});
