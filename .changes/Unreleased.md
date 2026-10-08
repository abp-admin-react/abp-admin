# Unreleased(尚未随版本发布)

> 发布时把本文件内容整理为 `Migration {旧} - {新}.md` 并清空。随手累加,信息全优先。

## 模块变更

- `backend/NuGet.Config`:`<packageSources>` 增加 `<clear />`——屏蔽用户级/机器级源,包只允许来自本文件声明源(供应链加固,借鉴 abp-next-admin)。
- 新增 `.changes/` 结构化升级说明约定(本目录):发布版本时把 `Unreleased.md` 整理为 `Migration X.Y - X.Z.md`。
- `AbpAdmin.HttpApi.Host`:新增 `LogUniqueIdEnricher`——每条日志附唯一短 ID(`LogUniqueId` 属性),报障单点定位,覆盖后台作业/启动期等无请求上下文的日志。
- `AbpAdmin.Application.Contracts/Permissions`:审计日志组新增 `AbpAdmin.AuditLogs.Restore` 权限(实体变更回滚;能看变更历史 ≠ 能改写业务数据)。
- `AbpAdmin.Application/AuditLogs`:`AuditLogAppService.RestoreEntityChangeAsync`——按一条实体变更记录把 OriginalValue 写回实体(借鉴 abp-next-admin EntityRestoreAppService)。仅支持 Updated 型变更;并发戳不参与;无法还原的属性逐项跳过并说明;前端在审计日志页「实体变更历史」抽屉对 Updated 行提供「回滚」按钮(权限 `canRestoreEntityChange`)。
- 新增 `AbpAdmin.DynamicQueryable` 工程(零 ABP/NuGet 依赖的表达式树动态查询库,借鉴 abp-next-admin framework/dynamic-queryable):条件组翻译为 `Expression<Func<T,bool>>`,字段必须真实存在(fail-closed);配套纯单测工程。
- 新增 `IIdentityUserSearchAppService`(`/api/app/identity-user-search`):用户动态搜索落地——`GetAvailableFieldsAsync` 返回字段白名单元数据(名称/值类型/可用操作符),`SearchAsync` 按条件组分页搜索;字段白名单外直接拒绝,排序走既有白名单。
- `AbpAdmin.Application/AuditLogs`:OCR 修复——回滚反射从声明类型取 MethodInfo（Castle 代理子类不继承 private 方法，运行期 NRE）；跳过脱敏属性与 `[REDACTED]` 掩码值（防掩码写回凭据字段）；实体类型收敛到 `AuditLoggingEntityTypes.ChangeHistoryEnabled` 白名单；拒绝路径带错误码（`AbpAdminDomainErrorCodes.EntityRestore.*`，测试按 Code 断言）。
- `AbpAdmin.Application/PermissionManagement`:OCR 修复——六个写方法补 `[OperationLog]` 与 `EnsureHostSide()`；静态定义镜像记录禁止删除；重名/遮蔽检查重排（先动态重名精确归因）；`GetDefinitionsAsync` 分页下推数据库。
- `AbpAdmin.Application/Identity`:OCR 修复——动态搜索校验每字段操作符集（非法组合转 400）；表达式构建异常转业务错；计数在无序查询上执行；`IdentityUserSearchItemDto.Email/PhoneNumber` 补 `[Masked]` 脱敏（与 IdentityUserDto 同口径）。
- `AbpAdmin.DynamicQueryable`:清理——删除零引用 `DynamicConditionGroup`；`BuildPredicate` 收为 private 并对空条件 fail-closed。
- `web/src/pages/permission-definitions`:OCR 修复——列头筛选改用第三参 filter（此前静默无效）；编辑预填原始 displayName（防本地化串被覆盖成明文）；请求体显式 PascalCase；更新/删除路由修正为 `/{id}/group`、`/{id}/definition`（此前 405）；提交失败保持弹窗打开。
- `web/src/pages/audit-logs`:回滚按钮加进行中防重；抽屉按 entityId remount（防串实体旧数据）。
- (并行进行中的迁移机制重构——EF 迁移替代内嵌 Sql 脚本、PostgreSQL 单提供程序——由另一会话负责,此处不记录其条目,以该会话自己的说明为准。)
- **角色管理吸收包(借鉴 Admin.NET + ABP 上游机制)**:
  - 角色重命名级联:新增 `RoleRenamedCascadeHandler` 订阅 `IdentityRoleNameChangedEto`(与上游 PermissionManagement 的 `RoleUpdateEventHandler` 同构),同步改写 `MenuGrant.ProviderKey` 与 `RoleDataScope.RoleName`——偿还两表以角色名为键、重命名即静默失配的已登记技术债。
  - 数据范围防越权:`RoleDataScopeAppService` Create/Update 增加 `EnsureOperatorCanGrantAsync`——授「全部数据」要求操作者自身快照 IsAll、自定义 OU 须逐个落在操作者授权范围内;`CurrentDataScopeProvider` 对 `admin` 角色直接返回 IsAll(出厂不为 admin 配 RoleDataScope 行,不豁免则任何 IHasDataScope 实体对 admin 零行可见)。
  - 删除保护:`AbpAdminRoleAppService` 按 ABP 服务替换模式顶替 `IdentityRoleAppService`(`/api/identity/roles` 路由不变),角色下仍有用户时拒绝删除(原生删除会静默清掉用户-角色关联)。
  - 角色下用户只读查看:新增 `GET /api/app/role-user-admin?roleId=` + 前端角色页「用户」抽屉;`RoleDataScopeAppService` 三个写入口补 `[OperationLog]`,新增 `role(id)` 日志解析函数。
  - 前端配套:`DataScopeModal` 未配置语义如实展示(fail-closed 零行可见)且仅 404 走默认表单、其它读取失败关弹窗防默认值覆盖;`RoleGrantModal` 补「授权按节点生效」说明。
  - 新错误码:`AbpAdmin:Identity:RoleHasUsers`、`AbpAdmin:DataScope:RoleDataScopeEscalation`、`AbpAdmin:DataScope:RoleDataScopeCustomOuOutOfScope`(zh-Hans/en 已配)。

## 依赖项变更

| 库 | 原版本 | 现版本 |
| --- | --- | --- |
| (无主动调整) | | |

## 数据库迁移

| 项目 | 迁移/脚本 | 说明 |
| --- | --- | --- |
| (本批无新增;迁移机制重构的迁移条目见该重构自己的说明) | | |

## 配置变更

| 键 | 变更 | 默认值 | 必填 |
| --- | --- | --- | --- |
| `Serilog:WriteTo[File].Args.configure[0].Args.outputTemplate` | 变更 | File sink 默认模板 + 行尾 ` {LogUniqueId}` | 否 |
| `Serilog:WriteTo[Console].Args.configure[0].outputTemplate` | 新增 | `[HH:mm:ss Level] Message LogUniqueId` | 否 |
