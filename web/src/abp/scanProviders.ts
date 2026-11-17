import { request } from '@umijs/max';
import type { AnkrBalanceDto } from './chainAdmin';

export type ScanProvider = 'ankr' | 'etherscan';

export interface ScanProviderNetworkDto {
  networkId: string;
  logResultCap: number | null;
}

export interface ScanProviderDto {
  id: string;
  provider: ScanProvider;
  label: string | null;
  isEnabled: boolean;
  sortOrder: number;
  keyMasked: string;
  platformKeyMasked: string | null;
  group: string | null;
  networks: ScanProviderNetworkDto[];
}

export interface ScanProviderListDto {
  providers: ScanProviderDto[];
  legacyBalancePending: boolean;
  legacyRpcActive: boolean;
}

export interface ConfigureScanProviderInput {
  provider: ScanProvider;
  label: string | null;
  isEnabled: boolean;
  apiKey: string | null;
  platformAuthKey: string | null;
  clearPlatformAuthKey: boolean;
  group: string | null;
  networks: ScanProviderNetworkDto[];
}

export interface ScanProviderTestDto {
  passed: boolean;
  message: string;
}

const BASE = '/api/app/collection-scan-provider';

export function getScanProviders(): Promise<ScanProviderListDto> {
  return request(BASE);
}

export function createScanProvider(
  input: ConfigureScanProviderInput,
): Promise<ScanProviderDto> {
  return request(BASE, { method: 'POST', data: input });
}

export function updateScanProvider(
  id: string,
  input: ConfigureScanProviderInput,
): Promise<ScanProviderDto> {
  return request(`${BASE}/${id}`, { method: 'PUT', data: input });
}

export function deleteScanProvider(id: string): Promise<void> {
  return request(`${BASE}/${id}`, { method: 'DELETE' });
}

export function reorderScanProviders(ids: string[]): Promise<void> {
  return request(`${BASE}/reorder`, { method: 'POST', data: { ids } });
}

export function testScanProvider(input: {
  id: string | null;
  provider: ScanProvider;
  apiKey: string | null;
  networkId: string;
}): Promise<ScanProviderTestDto> {
  return request(`${BASE}/test`, {
    method: 'POST',
    data: input,
    skipErrorHandler: true,
  });
}

export function testScanProviderBalance(input: {
  id: string | null;
  platformAuthKey: string | null;
  group: string | null;
}): Promise<AnkrBalanceDto> {
  return request(`${BASE}/test-balance`, {
    method: 'POST',
    data: input,
    skipErrorHandler: true,
  });
}

export function getScanProviderBalance(id: string): Promise<AnkrBalanceDto> {
  return request(`${BASE}/${id}/balance`, { skipErrorHandler: true });
}

export function claimLegacyScanProviderBalance(id: string): Promise<void> {
  return request(`${BASE}/${id}/claim-legacy-balance`, { method: 'POST' });
}

export function getDashboardAnkrBalance(): Promise<
  AnkrBalanceDto & { label: string | null }
> {
  return request(`${BASE}/dashboard-balance`, { skipErrorHandler: true });
}
