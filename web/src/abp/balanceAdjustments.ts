import { request } from '@umijs/max';

const BASE = '/api/app/collection-balance-adjustment';

export type BalanceAdjustmentStatus = 0 | 1 | 2;

export interface BalanceAdjustmentDto {
  id: string;
  requestId: string;
  userId: string;
  currency: number;
  amount: string;
  reason: string;
  status: BalanceAdjustmentStatus;
  applicantId: string;
  reviewerId: string | null;
  reviewedAt: string | null;
  reviewNote: string | null;
  balanceTxnId: string | null;
  balanceAfter: string | null;
  ledgerVoucherId: string | null;
  creationTime: string;
}

export interface CreateBalanceAdjustmentInput {
  requestId: string;
  userId: string;
  currency: number;
  amount: string;
  reason: string;
}

export function getBalanceAdjustments(query: {
  skipCount: number;
  maxResultCount: number;
  userId?: string;
  status?: BalanceAdjustmentStatus;
  filter?: string;
}): Promise<{ items: BalanceAdjustmentDto[]; totalCount: number }> {
  return request(BASE, {
    params: {
      SkipCount: query.skipCount,
      MaxResultCount: query.maxResultCount,
      UserId: query.userId,
      Status: query.status,
      Filter: query.filter,
    },
  });
}

export function createBalanceAdjustment(
  input: CreateBalanceAdjustmentInput,
): Promise<BalanceAdjustmentDto> {
  return request(BASE, { method: 'POST', data: input });
}

export function approveBalanceAdjustment(
  id: string,
): Promise<BalanceAdjustmentDto> {
  return request(`${BASE}/${id}/approve`, { method: 'POST' });
}

export function rejectBalanceAdjustment(
  id: string,
  reason: string,
): Promise<BalanceAdjustmentDto> {
  return request(`${BASE}/${id}/reject`, { method: 'POST', data: { reason } });
}

export function getBalanceAdjustment(
  id: string,
): Promise<BalanceAdjustmentDto> {
  return request(`${BASE}/${id}`);
}
