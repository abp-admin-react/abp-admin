/**
 * 租户 extraProperties 的属性名常量。
 * 与后端 AbpAdminTenantConsts / EditionConsts 的属性名一一对应——
 * 后端改名时前端有单一改点，不会散落多处静默显示为 "-"。
 */
export const TENANT_PROP = {
  /** 版本 Id（EditionConsts.TenantEditionPropertyName） */
  EditionId: 'EditionId',
  /** 激活状态（AbpAdminTenantConsts.ActivationStatePropertyName） */
  ActivationState: 'ActivationState',
  /** 激活到期时间（AbpAdminTenantConsts.ActivationEndDatePropertyName） */
  ActivationEndDate: 'ActivationEndDate',
  /** 版本到期时间 UTC（AbpAdminTenantConsts.EditionEndDateUtcPropertyName） */
  EditionEndDateUtc: 'EditionEndDateUtc',
} as const;
