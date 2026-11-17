import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App } from 'antd';
import { useEffect } from 'react';
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
vi.mock('@/abp/openIddictTokens', () => ({
  getOpenIddictTokens: mocks.tokens,
  getOpenIddictAuthorizations: mocks.authorizations,
  pruneOpenIddictTokens: vi.fn(),
  revokeOpenIddictAuthorization: vi.fn(),
  revokeOpenIddictToken: vi.fn(),
  revokeOpenIddictTokensBySubject: vi.fn(),
}));
vi.mock('@/components/AutoHeightProTable', () => ({
  default: ({ request, params }: { request: (params: { current: number }) => Promise<unknown>; params: object }) => {
    useEffect(() => { void request({ current: 1, ...params }); }, [request, params]);
    return null;
  },
}));

beforeEach(() => {
  vi.resetAllMocks();
  for (const fn of [mocks.applications, mocks.tokens, mocks.authorizations]) {
    fn.mockResolvedValue({ items: [], totalCount: 0 });
  }
});

it('只授权令牌与授权页面时，读取两个列表而不查询应用目录', async () => {
  mocks.access.mockReturnValue({ canManageOpenIddictTokens: true });
  render(<App><TokenManagement /></App>);
  await waitFor(() => expect(mocks.tokens).toHaveBeenCalled());
  expect(screen.queryByLabelText('应用')).toBeNull();
  fireEvent.click(screen.getByRole('tab', { name: '授权' }));
  await waitFor(() => expect(mocks.authorizations).toHaveBeenCalled());
  expect(mocks.applications).not.toHaveBeenCalled();
});

it('同时具备应用权限时提供应用筛选', async () => {
  mocks.access.mockReturnValue({
    canManageOpenIddictTokens: true,
    canManageOpenIddictApplications: true,
  });
  render(<App><TokenManagement /></App>);
  await waitFor(() => expect(mocks.applications).toHaveBeenCalledOnce());
  expect(screen.getByLabelText('应用')).toBeTruthy();
});

it('切换令牌与授权时保留实际提交的筛选值', async () => {
  mocks.access.mockReturnValue({ canManageOpenIddictTokens: true });
  render(<App><TokenManagement /></App>);
  fireEvent.change(screen.getByLabelText('用户 Id'), { target: { value: 'subject-123' } });
  fireEvent.click(screen.getByRole('button', { name: /查\s*询/ }));
  await waitFor(() => expect(mocks.tokens).toHaveBeenLastCalledWith(
    expect.objectContaining({ subject: 'subject-123' }),
  ));
  fireEvent.click(screen.getByRole('tab', { name: '授权' }));
  await waitFor(() => expect(mocks.authorizations).toHaveBeenLastCalledWith(
    expect.objectContaining({ subject: 'subject-123' }),
  ));
});
