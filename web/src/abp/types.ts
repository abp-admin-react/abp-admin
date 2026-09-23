export type AbpCurrentUser = {
  isAuthenticated?: boolean;
  id?: string | null;
  userName?: string | null;
  name?: string | null;
  surName?: string | null;
  email?: string | null;
  emailVerified?: boolean;
  phoneNumber?: string | null;
  phoneNumberVerified?: boolean;
  roles?: string[];
  tenantId?: string | null;
};

export type AbpCurrentTenant = {
  id?: string | null;
  name?: string | null;
  isAvailable?: boolean;
};

export type AbpApplicationConfiguration = {
  currentUser?: AbpCurrentUser;
  currentTenant?: AbpCurrentTenant;
  auth?: {
    grantedPolicies?: Record<string, boolean>;
  };
  setting?: {
    values?: Record<string, string>;
  };
  features?: {
    values?: Record<string, string>;
  };
  clock?: {
    kind?: string;
    supportsMultipleTimezone?: boolean;
  };
  localization?: {
    currentCulture?: {
      cultureName?: string;
      displayName?: string;
    };
  };
  /** Cookie Consent 配置（后端 IApplicationConfigurationContributor 下发） */
  cookieConsent?: {
    isEnabled?: boolean;
    cookiePolicyUrl?: string;
    privacyPolicyUrl?: string;
    expirationDays?: number;
  };
  /** 后端 IApplicationConfigurationContributor 下发的扩展属性 */
  extraProperties?: {
    /** SignalR 开关（T3.2）：false 时前端不尝试连接，实时能力降级为轮询 */
    signalr?: {
      enabled?: boolean;
    };
    /** 字典标签色白名单（T3.4，DataDictionaryTagTypes.All），唯一来源是后端 */
    dataDictionaryTagTypes?: string[];
  };
};

export type FindTenantResult = {
  success: boolean;
  tenantId?: string;
  name?: string;
  isActive?: boolean;
};

export type TenantDto = {
  id: string;
  name: string;
  concurrencyStamp?: string;
  extraProperties?: Record<string, string | null>;
};

export type TenantListResult = {
  items: TenantDto[];
  totalCount: number;
};

export type CreateTenantInput = {
  name: string;
  adminEmailAddress: string;
  adminPassword: string;
};
