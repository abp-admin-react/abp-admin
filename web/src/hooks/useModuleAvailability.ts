import { createContext, useContext } from 'react';
import type { ModuleAvailability, ModuleId } from '@/abp/moduleManagement';

export const ModuleAvailabilityContext = createContext<
  ModuleAvailability | undefined
>(undefined);

export function useModuleEnabled(id: ModuleId) {
  const availability = useContext(ModuleAvailabilityContext);
  return availability === undefined ||
    (availability.initialized && availability.states[id] === true);
}
