import { request } from '@umijs/max';

export type ModuleId = 'Orders' | 'Collection' | 'Chain' | 'Finance';

export type ModuleAvailability = {
  version: number;
  initialized: boolean;
  states: Partial<Record<ModuleId, boolean>>;
};

export type ManagedModule = {
  id: string;
  displayName: string;
  isEnabled: boolean;
  dependencies: string[];
  configurationPath: string | null;
  historyPath: string | null;
};

export type ModuleManagementState = {
  version: number;
  initialized: boolean;
  modules: ManagedModule[];
};

export type ModuleChange = {
  moduleId: string;
  isEnabled: boolean;
  expectedVersion: number;
};

export type ModuleChangePreview = {
  version: number;
  canApply: boolean;
  changes: { moduleId: string; wasEnabled: boolean; isEnabled: boolean }[];
  blockingModuleIds: string[];
};

const path = '/api/admin/module-management';

export function getModuleManagement() {
  return request<ModuleManagementState>(path);
}

export function previewModuleChange(data: ModuleChange) {
  return request<ModuleChangePreview>(`${path}/preview`, {
    method: 'POST',
    data,
    skipErrorHandler: true,
  });
}

export function updateModuleManagement(data: ModuleChange) {
  return request<ModuleManagementState>(path, {
    method: 'PUT',
    data,
    skipErrorHandler: true,
  });
}
