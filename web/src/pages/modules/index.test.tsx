import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { App } from 'antd';
import { describe, expect, it, vi } from 'vitest';
import type { ManagedModule } from '@/abp/moduleManagement';
import english from '@/locales/en-US';
import chinese from '@/locales/zh-CN';
import traditionalChinese from '@/locales/zh-TW';
import ModulesPage from './index';

const mocks = vi.hoisted(() => ({
  locale: 'zh-CN' as 'zh-CN' | 'zh-TW' | 'en-US',
  getModules: vi.fn(),
  preview: vi.fn(),
  permissions: {} as Record<string, boolean>,
}));

vi.mock('@umijs/max', () => ({
  useAccess: () => ({
    canViewModuleManagement: true,
    canUpdateModuleManagement: true,
    canManagePaymentSettings: true,
    ...mocks.permissions,
  }),
  useModel: () => ({ setInitialState: vi.fn() }),
  Link: ({ to, ...props }: React.ComponentProps<'a'> & { to: string }) => (
    <a href={to} {...props} />
  ),
  useIntl: () => ({
    locale: mocks.locale,
    formatMessage: (
      {
        id,
        defaultMessage,
      }: { id: keyof typeof english; defaultMessage: string },
      values?: Record<string, string>,
    ) => {
      const messages = {
        'zh-CN': chinese,
        'zh-TW': traditionalChinese,
        'en-US': english,
      }[mocks.locale];
      return (messages[id as keyof typeof messages] ?? defaultMessage).replace(
        /\{(\w+)\}/g,
        (_, key: string) => values?.[key] ?? '',
      );
    },
  }),
}));

vi.mock('@/abp/moduleManagement', () => ({
  getModuleManagement: mocks.getModules,
  previewModuleChange: mocks.preview,
  updateModuleManagement: vi.fn(),
}));

const modules: ManagedModule[] = [
  {
    id: 'Orders',
    displayName: 'Backend Orders',
    isEnabled: false,
    dependencies: ['Collection'],
    configurationPath: '/collection/orders?settings=rules',
    historyPath: null,
  },
  {
    id: 'Collection',
    displayName: 'Backend Payments',
    isEnabled: true,
    dependencies: [],
    configurationPath: null,
    historyPath: null,
  },
  {
    id: 'Chain',
    displayName: 'Backend Chain',
    isEnabled: true,
    dependencies: [],
    configurationPath: null,
    historyPath: null,
  },
  {
    id: 'Finance',
    displayName: 'Backend Finance',
    isEnabled: true,
    dependencies: [],
    configurationPath: null,
    historyPath: null,
  },
  {
    id: 'Risk',
    displayName: 'Risk Engine',
    isEnabled: true,
    dependencies: ['Collection'],
    configurationPath: null,
    historyPath: null,
  },
];

describe('module management localization', () => {
  it('独立提现管理和账簿审批可进入自己的配置与历史', async () => {
    mocks.locale = 'zh-CN';
    mocks.permissions = {
      canUpdateModuleManagement: false,
      canManagePaymentSettings: false,
      canManageWeb3Configuration: true,
      canManageCollectionWallets: true,
      canAdminWithdrawals: true,
      canViewWeb3Operations: true,
      canOpenFinanceVouchersWorkspace: true,
    };
    mocks.getModules.mockResolvedValue({
      version: 1,
      initialized: true,
      modules: modules.map((module) => {
        if (module.id === 'Chain') {
          return { ...module, configurationPath: '/web3/operations?settings=network', historyPath: '/web3/operations' };
        }
        if (module.id === 'Finance') {
          return { ...module, historyPath: '/finance/vouchers' };
        }
        return module;
      }),
    });
    try {
      render(<QueryClientProvider client={new QueryClient()}><App><ModulesPage /></App></QueryClientProvider>);
      expect(await screen.findByRole('link', { name: '配置链上充提' })).toHaveAttribute('href', '/collection/withdrawals?settings=withdraw');
      expect(screen.getByRole('link', { name: '查看链上充提历史记录' })).toHaveAttribute('href', '/collection/withdrawals');
      expect(screen.getByRole('link', { name: /财务工作区/ })).toHaveAttribute('href', '/finance/vouchers');
      expect(screen.queryByRole('button', { name: '停用收款' })).toBeNull();
    } finally {
      mocks.permissions = {};
    }
  });

  it.each([
    [
      'zh-CN',
      '订单',
      '收款',
      '链上充提',
      '财务工作区',
      '配置订单',
      '启用订单',
      '停用收款',
    ],
    [
      'zh-TW',
      '訂單',
      '收款',
      '鏈上充提',
      '財務工作區',
      '設定訂單',
      '啟用訂單',
      '停用收款',
    ],
    [
      'en-US',
      'Orders',
      'Payments',
      'On-chain deposits and withdrawals',
      'Finance workspace',
      'Configure Orders',
      'Enable Orders',
      'Disable Payments',
    ],
  ] as const)(
    '%s uses one localized name in the table, dependencies and confirmation',
    async (locale, orders, payments, chain, finance, configure, enableOrders, disablePayments) => {
      mocks.locale = locale;
      mocks.getModules.mockResolvedValue({
        version: 1,
        initialized: true,
        modules,
      });
      mocks.preview.mockImplementation(async ({ isEnabled }) =>
        isEnabled
          ? {
              version: 1,
              canApply: true,
              changes: [
                { moduleId: 'Orders', wasEnabled: false, isEnabled: true },
                { moduleId: 'Risk', wasEnabled: false, isEnabled: true },
              ],
              blockingModuleIds: [],
            }
          : {
              version: 1,
              canApply: false,
              changes: [],
              blockingModuleIds: ['Orders', 'Risk'],
            },
      );

      render(
        <QueryClientProvider client={new QueryClient()}>
          <App>
            <ModulesPage />
          </App>
        </QueryClientProvider>,
      );

      const ordersCell = await screen.findByRole('cell', {
        name: orders,
      });
      const ordersRow = ordersCell.closest('tr');
      if (!ordersRow) throw new Error('订单模块单元格缺少所在行');
      expect(
        within(ordersRow).getByRole('cell', { name: payments }),
      ).toBeVisible();
      expect(screen.getByRole('cell', { name: chain })).toBeVisible();
      expect(screen.getByRole('cell', { name: finance })).toBeVisible();
      expect(screen.getByRole('cell', { name: 'Risk Engine' })).toBeVisible();
      expect(
        within(ordersRow).getByRole('link', { name: configure }),
      ).toBeVisible();

      fireEvent.click(
        within(ordersRow).getByRole('button', { name: enableOrders }),
      );
      const confirm = await screen.findByRole('dialog', { name: enableOrders });
      expect(
        within(confirm).getByText(
          (text, element) =>
            element?.tagName === 'LI' && text.startsWith(orders),
        ),
      ).toBeInTheDocument();
      expect(
        within(confirm).getByText(
          (text, element) =>
            element?.tagName === 'LI' && text.startsWith('Risk Engine'),
        ),
      ).toBeInTheDocument();
      fireEvent.click(
        within(confirm).getByRole('button', {
          name: locale === 'en-US' ? 'Cancel' : /取\s*消/,
        }),
      );
      await waitFor(() =>
        expect(screen.queryByRole('dialog', { name: enableOrders })).toBeNull(),
      );

      fireEvent.click(screen.getByRole('button', { name: disablePayments }));
      const blocked = await screen.findByRole('dialog', {
        name: disablePayments,
      });
      await waitFor(() =>
        expect(
          within(blocked).getByText(new RegExp(`${orders}.*Risk Engine`)),
        ).toBeInTheDocument(),
      );
    },
  );
});
