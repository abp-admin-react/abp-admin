// @vitest-environment node
import { describe, expect, it, vi } from 'vitest';

vi.mock('antd', () => ({
  App: {
    useApp: () => ({
      message: {
        error: vi.fn(),
        success: vi.fn(),
        info: vi.fn(),
        warning: vi.fn(),
      },
    }),
  },
  Alert: (props: unknown) => props,
  Button: (props: unknown) => props,
  Card: (props: unknown) => props,
  Space: (props: unknown) => props,
  Typography: {
    Paragraph: (props: unknown) => props,
    Text: (props: unknown) => props,
  },
}));

vi.mock('@/abp/customer/request', () => ({
  createCustomerRequest: () => async () => [],
}));

vi.mock('@/abp/customer/browserAdapter', () => ({
  createBrowserTransport: () => async () => ({ status: 200, body: {} }),
}));

vi.mock('@/abp/oidc', () => ({
  getAccessToken: async () => 'demo-token',
}));

import CustomerErrorsDemoPage from './index';

describe('customer-errors-demo', () => {
  it('导出页面组件', () => {
    expect(typeof CustomerErrorsDemoPage).toBe('function');
  });
});
