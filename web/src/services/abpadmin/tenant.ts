// @ts-ignore
/* eslint-disable */
import { request } from "@umijs/max";

/** 此处后端没有提供注释 GET /api/app/tenant */
export async function getApiAppTenant(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1TenantDto>("/api/app/tenant", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 与基类逻辑逐步一致，唯一差别：租户数据种子按贡献者分离 UoW、随做随提交
（T3.4 在 AbpAdminDbMigrationService 的同款修法，运行时路径漏修导致建租户 500）。
基类在同一请求 UoW 里跑全部 IDataSeedContributor：框架 PermissionDataSeedContributor
把当前定义的全部权限授给 admin 角色（插入但本 UoW 内未提交），而 T1 自研的
SettingUi/FileManagement/DataScope/NotificationService 权限种子走 IPermissionDataSeeder
「先查后插」——查的是数据库，看不到同 UoW 里未提交的行，去重失效 → 双方对同一
(TenantId, Name, ProviderName, ProviderKey) 各插一条，请求末尾 AbpUowActionFilter
SaveChanges 撞 AbpPermissionGrants 唯一索引（SQLite Error 19）→ 500。
分离 UoW 后每个贡献者立即提交，后面的贡献者查得到前面已提交的行，幂等去重生效。
RequiresNew 必须为 true：否则子 UoW 并入环境 UoW，仍然攒到最后一次提交。 POST /api/app/tenant */
export async function postApiAppTenant(
  body: API.TenantCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.TenantDto>("/api/app/tenant", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/app/tenant/${param0} */
export async function getApiAppTenantId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantDto>(`/api/app/tenant/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/app/tenant/${param0} */
export async function putApiAppTenantId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppTenantIdParams,
  body: API.TenantUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantDto>(`/api/app/tenant/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/tenant/${param0} */
export async function deleteApiAppTenantId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppTenantIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 应用套餐到租户：把租户菜单树重置为"套餐过滤后的全局模板拷贝"。
破坏性操作——会清掉该租户现有菜单与菜单角色勾选，调用方前端需二次确认。
套餐以 PackageId（强标识）持久化在租户 extra property 上（见 AbpAdminTenantConsts.PackageIdPropertyName），
懒拷贝据此过滤；套餐解析/守卫规则收敛在 AbpAdmin.Tenants.TenantPackageManager。
POST /api/app/tenant/{id}/apply-package POST /api/app/tenant/${param0}/apply-package */
export async function postApiAppTenantIdApplyPackage(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppTenantIdApplyPackageParams,
  body: API.ApplyTenantPackageDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant/${param0}/apply-package`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 校验连接串：尝试建立一次数据库连接并立即关闭。纯校验，不写库。
POST /api/app/tenant/{id}/check-connection-string POST /api/app/tenant/${param0}/check-connection-string */
export async function postApiAppTenantIdCheckConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.postApiAppTenantIdCheckConnectionStringParams,
  body: API.CheckTenantConnectionStringInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.CheckTenantConnectionStringResultDto>(
    `/api/app/tenant/${param0}/check-connection-string`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      params: { ...queryParams },
      data: body,
      ...(options || {}),
    }
  );
}

/** 连接字符串管理视图：当前记录（掩码）+ 功能开关 + 是否使用共享数据库。
GET /api/app/tenant/{id}/connection-strings GET /api/app/tenant/${param0}/connection-strings */
export async function getApiAppTenantIdConnectionStrings(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantIdConnectionStringsParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantConnectionStringManagementDto>(
    `/api/app/tenant/${param0}/connection-strings`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 全量同步租户连接串。设置项禁用时返回业务异常（不是静默忽略）。
Items 语义：掩码/空 = 保持原值；含掩码字面量 = 拒绝；从提交里消失的既有记录 = 删除。
提交了明文 Default（非 UseSharedDatabase）后发布 AbpAdmin.Data.TenantDatabaseMigrationNeededEto
（UoW 提交后投递）触发运行期建库+迁移——与 UpdateDefaultConnectionStringAsync 同语义、幂等；
前端连接串抽屉把 Default 作为 Items 一项走本端点，这里是生产主路径。
PUT /api/app/tenant/{id}/connection-strings PUT /api/app/tenant/${param0}/connection-strings */
export async function putApiAppTenantIdConnectionStrings(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppTenantIdConnectionStringsParams,
  body: API.UpdateTenantConnectionStringsInput,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant/${param0}/connection-strings`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 管理侧 API 永不返回明文：有值时返回固定掩码。 GET /api/app/tenant/${param0}/default-connection-string */
export async function getApiAppTenantIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiAppTenantIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<string>(
    `/api/app/tenant/${param0}/default-connection-string`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 更新默认连接串：加密存储；掩码/空值表示保持原值；含掩码字面量的值拒绝（业务异常）。
提交了明文 Default 后发布 AbpAdmin.Data.TenantDatabaseMigrationNeededEto（UoW 提交后投递）
触发独立租户库的运行期建库+schema 迁移——幂等，重复保存可作迁移失败的重试手段。
设置项禁用该功能时返回业务异常（不是静默忽略）。 PUT /api/app/tenant/${param0}/default-connection-string */
export async function putApiAppTenantIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiAppTenantIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant/${param0}/default-connection-string`, {
    method: "PUT",
    params: {
      ...queryParams,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/app/tenant/${param0}/default-connection-string */
export async function deleteApiAppTenantIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiAppTenantIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/app/tenant/${param0}/default-connection-string`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 可分配连接串的数据库清单：只列 IsUsedByTenants = true 的数据库。
GET /api/app/tenant/available-databases GET /api/app/tenant/available-databases */
export async function getApiAppTenantAvailableDatabases(options?: {
  [key: string]: any;
}) {
  return request<string[]>("/api/app/tenant/available-databases", {
    method: "GET",
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/multi-tenancy/tenants */
export async function getApiMultiTenancyTenants(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiMultiTenancyTenantsParams,
  options?: { [key: string]: any }
) {
  return request<API.PagedResultDto1TenantDto>("/api/multi-tenancy/tenants", {
    method: "GET",
    params: {
      ...params,
    },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 POST /api/multi-tenancy/tenants */
export async function postApiMultiTenancyTenants(
  body: API.TenantCreateDto,
  options?: { [key: string]: any }
) {
  return request<API.TenantDto>("/api/multi-tenancy/tenants", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/multi-tenancy/tenants/${param0} */
export async function getApiMultiTenancyTenantsId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiMultiTenancyTenantsIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantDto>(`/api/multi-tenancy/tenants/${param0}`, {
    method: "GET",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 PUT /api/multi-tenancy/tenants/${param0} */
export async function putApiMultiTenancyTenantsId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiMultiTenancyTenantsIdParams,
  body: API.TenantUpdateDto,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<API.TenantDto>(`/api/multi-tenancy/tenants/${param0}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
    },
    params: { ...queryParams },
    data: body,
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 DELETE /api/multi-tenancy/tenants/${param0} */
export async function deleteApiMultiTenancyTenantsId(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiMultiTenancyTenantsIdParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(`/api/multi-tenancy/tenants/${param0}`, {
    method: "DELETE",
    params: { ...queryParams },
    ...(options || {}),
  });
}

/** 此处后端没有提供注释 GET /api/multi-tenancy/tenants/${param0}/default-connection-string */
export async function getApiMultiTenancyTenantsIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.getApiMultiTenancyTenantsIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<string>(
    `/api/multi-tenancy/tenants/${param0}/default-connection-string`,
    {
      method: "GET",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 PUT /api/multi-tenancy/tenants/${param0}/default-connection-string */
export async function putApiMultiTenancyTenantsIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.putApiMultiTenancyTenantsIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/multi-tenancy/tenants/${param0}/default-connection-string`,
    {
      method: "PUT",
      params: {
        ...queryParams,
      },
      ...(options || {}),
    }
  );
}

/** 此处后端没有提供注释 DELETE /api/multi-tenancy/tenants/${param0}/default-connection-string */
export async function deleteApiMultiTenancyTenantsIdDefaultConnectionString(
  // 叠加生成的Param类型 (非body参数swagger默认没有生成对象)
  params: API.deleteApiMultiTenancyTenantsIdDefaultConnectionStringParams,
  options?: { [key: string]: any }
) {
  const { id: param0, ...queryParams } = params;
  return request<any>(
    `/api/multi-tenancy/tenants/${param0}/default-connection-string`,
    {
      method: "DELETE",
      params: { ...queryParams },
      ...(options || {}),
    }
  );
}
