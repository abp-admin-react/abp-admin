import { describe, expect, it } from 'vitest';
import access from './access';

describe('access', () => {
  it('用户余额调账独立授权并允许进入资金账务目录', () => {
    const grantedPolicies = {
      'AbpAdmin.Collection.BalanceAdjustments.View': true,
      'AbpAdmin.Collection.BalanceAdjustments.Create': true,
      'AbpAdmin.Collection.BalanceAdjustments.Approve': true,
    };
    const host = access({ grantedPolicies });
    expect(host.canViewFinance).toBe(true);
    expect(host.canViewBalanceAdjustments).toBe(true);
    expect(host.canCreateBalanceAdjustments).toBe(true);
    expect(host.canApproveBalanceAdjustments).toBe(true);
    expect(host.canApproveOwnBalanceAdjustments).toBe(false);
    expect(host.canViewFinanceConfiguration).toBe(false);
    const tenant = access({
      grantedPolicies,
      currentTenant: { isAvailable: true },
    });
    expect(tenant.canViewBalanceAdjustments).toBe(false);
    expect(tenant.canApproveBalanceAdjustments).toBe(false);
  });

  it('提现管理员可以进入集中钱包配置', () => {
    expect(
      access({ grantedPolicies: { 'AbpAdmin.Collection.WithdrawAdmin': true } })
        .canManageCollectionWallets,
    ).toBe(true);
    expect(access({}).canManageCollectionWallets).toBe(false);
  });

  it('渠道目录允许只持有设置权限的宿主进入', () => {
    const grantedPolicies = { 'SettingUi.ShowSettingPage': true };
    const host = access({ grantedPolicies });
    expect(host.canManagePaymentSettings).toBe(true);
    expect(host.canViewPaymentChannel).toBe(true);
    expect(host.canViewWeb3).toBe(true);
    expect(host.canViewTrade).toBe(true);
    expect(host.canViewCollectionPayments).toBe(false);
    const tenant = access({
      grantedPolicies,
      currentTenant: { isAvailable: true },
    });
    expect(tenant.canManagePaymentSettings).toBe(false);
    expect(tenant.canViewPaymentChannel).toBe(false);
    expect(tenant.canViewWeb3).toBe(false);
    expect(tenant.canViewTrade).toBe(false);
    expect(tenant.canViewFinance).toBe(false);
  });

  it('按服务端的宿主限制显示订单和用户余额查询', () => {
    const grantedPolicies = {
      'AbpAdmin.Ordering.Orders': true,
      'AbpAdmin.Collection.Collections': true,
    };
    const tenant = access({
      grantedPolicies,
      currentTenant: { isAvailable: true },
    });
    expect(tenant.canViewOrders).toBe(false);
    expect(access({ grantedPolicies }).canViewOrders).toBe(true);
    expect(tenant.canViewMemberFunds).toBe(false);
    expect(access({ grantedPolicies }).canViewMemberFunds).toBe(true);
  });

  it.each([
    ['AbpAdmin.Collection.Collections', true, false],
    ['AbpAdmin.Collection.Network.Admin', true, true],
    ['AbpAdmin.Collection.WithdrawAdmin', true, true],
    ['SettingUi.ShowSettingPage', false, true],
  ])(
    'Web3 分别按角色授权运营与配置 (%s)',
    (permission, operations, configuration) => {
      const grantedPolicies = { [permission]: true };
      const host = access({ grantedPolicies });
      expect(host.canViewWeb3).toBe(true);
      expect(host.canViewWeb3Operations).toBe(operations);
      expect(host.canManageWeb3Configuration).toBe(configuration);
      const tenant = access({
        grantedPolicies,
        currentTenant: { isAvailable: true },
      });
      expect(tenant.canViewWeb3Operations).toBe(false);
      expect(tenant.canManageWeb3Configuration).toBe(false);
      expect(access({}).canViewWeb3).toBe(false);
    },
  );

  it('only allows host users with the Collection payment-view policy', () => {
    const grantedPolicies = { 'AbpAdmin.Collection.Payments.View': true };

    expect(access({ grantedPolicies }).canViewCollectionPayments).toBe(true);
    expect(
      access({ grantedPolicies, currentTenant: { isAvailable: true } })
        .canViewCollectionPayments,
    ).toBe(false);
    expect(access({ grantedPolicies: {} }).canViewCollectionPayments).toBe(
      false,
    );
  });

  it('keeps refund and reconcile permissions separate and host only', () => {
    const view = { 'AbpAdmin.Collection.Payments.View': true };
    const refund = { 'AbpAdmin.Collection.Payments.Refund': true };
    const reconcile = { 'AbpAdmin.Collection.Payments.Reconcile': true };
    expect(
      access({ grantedPolicies: { ...view, ...refund } })
        .canRefundCollectionPayments,
    ).toBe(true);
    expect(
      access({ grantedPolicies: { ...view, ...refund } })
        .canReconcileCollectionPayments,
    ).toBe(false);
    expect(
      access({ grantedPolicies: refund }).canRefundCollectionPayments,
    ).toBe(false);
    expect(
      access({ grantedPolicies: { ...view, ...reconcile } })
        .canReconcileCollectionPayments,
    ).toBe(true);
    expect(
      access({
        grantedPolicies: { ...view, ...refund, ...reconcile },
        currentTenant: { isAvailable: true },
      }).canRefundCollectionPayments,
    ).toBe(false);
  });

  it('should return canAdmin true when user has AbpIdentity.Users policy', () => {
    const initialState = {
      currentUser: {
        userid: '1',
        name: 'Admin User',
        avatar: 'https://example.com/avatar.png',
        access: 'admin' as const,
      },
      grantedPolicies: { 'AbpIdentity.Users': true },
    };

    const result = access(initialState);

    expect(result.canAdmin).toBe(true);
  });

  it('should return canAdmin false when user has non-admin access', () => {
    const initialState = {
      currentUser: {
        userid: '2',
        name: 'Regular User',
        avatar: 'https://example.com/avatar.png',
        access: 'user' as const,
      },
    };

    const result = access(initialState);

    expect(result.canAdmin).toBe(false);
  });

  it('should return canAdmin false when user access is undefined', () => {
    const initialState = {
      currentUser: {
        userid: '3',
        name: 'Guest User',
        avatar: 'https://example.com/avatar.png',
      },
    };

    const result = access(initialState);

    expect(result.canAdmin).toBe(false);
  });

  it('should return canAdmin false when currentUser is undefined', () => {
    const initialState = {
      currentUser: undefined,
    };

    const result = access(initialState);

    expect(result.canAdmin).toBeFalsy();
  });

  it('should return canAdmin false when initialState is undefined', () => {
    const result = access(undefined);

    expect(result.canAdmin).toBeFalsy();
  });
});
