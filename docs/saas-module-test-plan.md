# SAAS 模块全面测试计划（Playwright GUI 黑盒测试）

> 版本：v1.0（2026-11-01）
> 方法：MCP Playwright 纯 GUI 黑盒测试为主，配合页面内 `fetch()` 做 API 级断言（认证上下文内）。
> 目标：覆盖功能正确性、客户滥用输入、黑客攻击面、权限矩阵、业务状态机、UI 权限门控六个维度。

---

## 1. 被测对象（事实基线）

SAAS 模块 = 三条路由页面 + 六组后端能力。本模块**未使用**商业 Volo.Saas，而是基于
开源 `Volo.Abp.TenantManagement` 自研补齐（Edition 存于 Tenant.ExtraProperties、
套餐为自研聚合、激活/连接串/到期均为自研逻辑）。

### 1.1 前端页面（web/src/pages）

| 路由 | 页面 | 访问权限 | 核心功能 |
|---|---|---|---|
| `/administration/saas/tenants` | 租户管理 | `canManageTenants` | 租户 CRUD、版本分配、套餐应用、功能特性、连接串抽屉、模拟登录、激活状态编辑、Host 功能 |
| `/administration/saas/editions` | 版本管理 | `canManageEditions` | 版本 CRUD、功能特性（E provider）、删除时租户迁移 |
| `/administration/saas/tenant-packages` | 租户套餐 | `canManageTenantPackages` | 套餐 CRUD、菜单树勾选配置、删除 |

旧路由 `/saas/tenants`、`/saas/editions` 有重定向兼容。

### 1.2 后端 API（全部经 `/api/app/*` 自动 API 或 `/api/multi-tenancy/tenants`）

| 服务 | 端点 | 权限 |
|---|---|---|
| EditionAppService | GET/POST `/api/app/edition`，PUT/DELETE `/api/app/edition/{id}`，GET `/{id}/tenant-count`，GET `/lookup`，POST `/set-tenant-edition` | `AbpAdmin.Editions(.Create/.Update/.Delete)`，Host-only |
| TenantAppService（替换 OSS） | `/api/multi-tenancy/tenants` CRUD，POST `/api/app/tenant/{id}/apply-package`，GET/PUT `/{id}/connection-strings`，GET `/available-databases`，POST `/{id}/check-connection-string` | `AbpTenantManagement.Tenants.*` + `.ManageConnectionStrings` |
| TenantPackageAppService | GET/POST `/api/app/tenant-package`，GET/PUT/DELETE `/{id}`，GET/PUT `/{id}/menu-selection` | `AbpAdmin.TenantPackages(.Create/.Update/.Delete)`，Host-only |
| Feature 管理 | GET/PUT/DELETE `/api/feature-management/features?providerName=T|E&providerKey=` | `Tenants.ManageFeatures` / `Editions.ManageFeatures`（provider 策略映射） |

### 1.3 关键业务规则（预期行为的断言依据）

1. **连接串永不回明文**：任何 GET 返回 `********` 掩码；提交掩码/空 = 保持原值；值中含掩码字面量 → 业务异常；明文不进审计日志。
2. **版本删除迁移**：`moveTenantsToEditionId == 本版本 id` → 拒绝；目标不存在 → 404；null → 清空租户版本分配；迁移后租户配置缓存必须失效。
3. **套餐**：名称唯一（64 字符上限）；空套餐不可应用（`TenantPackageMenusEmpty`）；非法菜单 ID 拒绝（错误带计数）；应用套餐 = 破坏性重置租户菜单与角色授权；null 包 = 重置全量菜单。
4. **版本到期**：`EditionEndDateUtc` 过期后生效版本为空但**保留关联**（续期可恢复）；允许最长 1 小时缓存滞后（设计如此）。
5. **租户激活**：Passive / 限时到期租户在 OpenIddict 登录时拒绝（已知名遗留缺口：不带 `__tenant` 的 `/connect/token` 不校验）。
6. **Host-only 双重防线**：权限定义 `MultiTenancySides.Host` + 运行时 `EnsureHostSide()`，租户上下文调用一律拒绝。
7. **连接串管理开关**：设置 `AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement=false` 时，4 个连接串端点全部业务异常，前端隐藏抽屉入口。

---

## 2. 环境与前置准备

### 2.1 被测环境（二选一，推荐 B）

- **A. 本地开发栈**：后端 `https://localhost:44300`（HttpApi.Host），前端 `pnpm start`（Umi 8000 等端口，代理已配）。默认账号 `admin / 1q2w3E*`。
- **B. sectest 隔离栈**（推荐，可随意砸数据）：`WEB_BIND=127.0.0.1 WEB_HOST_PORT=18000 docker compose -p abpadmin-sectest -f docker-compose.yml -f sectest/docker-compose.sectest.yml up -d --build`；web 边缘 `http://127.0.0.1:18000`，host 直连 `http://127.0.0.1:18081`。清理：`down -v`。

### 2.2 测试账号矩阵（登录一次缓存令牌，避免触发 LoginByIp 限流 20 次/15 分钟）

| 账号 | 权限 | 用途 |
|---|---|---|
| `admin` | 全部 | 正常路径基线、数据准备 |
| `sec-edition-ro` | 仅 `AbpAdmin.Editions` | 只读越权测试 |
| `sec-edition-cu` | `AbpAdmin.Editions` + Create + Update（无 Delete） | 缺权限越权 |
| `sec-pkg-mgr` | `AbpAdmin.TenantPackages` 全套（无 Editions） | 模块间水平越权 |
| `sec-none` | 无任何 SAAS 权限 | 全拒基线 |
| `t-admin` | 某租户的管理员用户 | 租户侧用户打 Host API（EnsureHostSide / 权限 Host 限定） |

准备方式：以 admin 登录 → 身份管理 → 角色/用户，创建上述用户并授予对应权限。
账号准备属于一次性行为，全部在测试第 0 阶段完成，令牌用独立浏览器上下文保存。

### 2.3 基础测试数据

- 版本：`基础版`、`专业版`、`企业版`
- 套餐：`入门套餐`（勾选 2~3 个叶子菜单 + 父目录）、`空套餐`（不勾任何菜单）
- 租户：`t-func`（功能测试用）、`t-passive`、`t-expired`（限时已过期）、`t-pkg`（套餐应用用）

---

## 3. 用例集

约定：步骤列为 GUI 操作要点（Playwright 工具标注），预期列含 **UI 断言** 与 **网络断言**（`browser_network_requests` / `browser_evaluate` fetch 状态码与响应体）。

### A. 功能正确性（正常路径）

| ID | 名称 | 步骤要点 | 预期 |
|---|---|---|---|
| A-ED-01 | 新建版本 | 版本页 → 新建 → 输入"基础版"提交 | 列表出现新行；POST `/api/app/edition` 200 |
| A-ED-02 | 编辑版本 | 行内编辑 → 改名提交 | 列表刷新；PUT 200 |
| A-ED-03 | 版本列表/lookup | 打开页面与租户页"版本"下拉 | GET list 与 `/lookup` 均含全部版本 |
| A-ED-04 | 版本下租户数 | 对有租户版本点删除 | 弹窗 Alert 显示"该版本下有 N 个租户"，N 与实际一致 |
| A-ED-05 | 删除版本-迁移 | 选目标版本 → 确认 | DELETE 带 `moveTenantsToEditionId` 200；受影响租户"版本"列变为目标版本 |
| A-ED-06 | 删除版本-清空 | 不选目标 → 确认 | 受影响租户版本列变空 |
| A-ED-07 | 无租户版本删除 | 删除空版本 | 提示"该版本下没有租户"，删除成功 |
| A-TN-01 | 新建租户 | 名称+管理员邮箱+密码，不选套餐 | 创建成功；列表出现；`TenantCreatedEto` 种子数据生效（可用该管理员登录租户门户验证） |
| A-TN-02 | 新建租户-带套餐 | 选套餐创建 | 创建成功；apply-package 调用；租户菜单 = 套餐集合（模拟登录核对菜单树） |
| A-TN-03 | 编辑租户 | 改名/激活状态 | PUT 200，列表同步 |
| A-TN-04 | 版本分配 | TenantEditionForm 选版本 | `set-tenant-edition` 200；列表"版本"列更新 |
| A-TN-05 | 清除版本分配 | 下拉 allowClear 置空 | 版本列清空 |
| A-TN-06 | 删除租户 | Popconfirm 确认 | DELETE 200；该租户 feature 值与菜单被清理（API 级验证 feature 接口无 T+租户 ID 数据） |
| A-TP-01 | 新建套餐 | 名称+备注 | 列表出现；menuCount=0 |
| A-TP-02 | 菜单配置 | 勾选叶子+父目录 → 保存 | PUT menu-selection 200；重开弹窗勾选态一致（含半选父级） |
| A-TP-03 | 编辑/删除套餐 | 改名；删除 | Popconfirm 文案"已应用过的租户不受影响"；删除成功 |
| A-FT-01 | Host 功能 | 租户页"管理 Host 功能" | providerName=T 无 key；能读能存 |
| A-FT-02 | 租户功能 | 行内功能入口 | providerName=T，key=租户 ID；修改 `AbpAdmin.FileManagement.StorageQuotaBytes` 保存后重开回显 |
| A-FT-03 | 版本功能 | 版本页功能入口 | providerName=E，key=版本 ID；读写正常 |
| A-FT-04 | 恢复默认 | 功能弹窗"恢复默认" | DELETE 调用成功；重开显示默认值 |
| A-CS-01 | 查看连接串 | 打开抽屉 | 显示 UseSharedDatabase 开关、Default 掩码 `********`、各模块项掩码 |
| A-CS-02 | 更新连接串 | 填真实 PG 串提交 | PUT 200；重开仍为掩码；触发迁移事件（日志/库创建） |
| A-CS-03 | 测试连接 | 填有效串点"测试连接" | 显示有效；填无效串显示通用失败文案（**不得**回显驱动异常原文） |
| A-CS-04 | 共享数据库 | 开启开关提交 | 所有 item 清空 |
| A-AC-01 | 激活状态编辑 | EditTenantModal 切 限时激活 | `activationEndDate` 变必填（ProFormDependency）；保存成功 |
| A-AC-02 | Passive 租户登录 | 用该租户管理员登录 | 登录被拒，业务错误 `TenantIsPassive` 文案 |
| A-AC-03 | 过期租户登录 | 限时已过期租户登录 | 被拒，`TenantActivationExpired` 文案 |

### B. 客户滥用（输入校验 / 边界 / 误操作）

| ID | 名称 | 步骤要点 | 预期 |
|---|---|---|---|
| B-IN-01 | 空值提交 | 版本/套餐必填项留空提交 | 前端 required 拦截；后端 400 双保险 |
| B-IN-02 | 纯空白 | 输入全空格 | 前端或后端拒绝（domain `Check.NotNullOrWhiteSpace`） |
| B-IN-03 | 版本名 128 边界 | 恰好 128 字符 / 129 字符 | 128 成功；129 被拒（StringLength 400，错误文案含长度） |
| B-IN-04 | 套餐名 64 边界 | 64 / 65 字符 | 64 成功；65 被拒 |
| B-IN-05 | 备注 256 边界 | 256 / 257 字符 | 256 成功；257 被拒 |
| B-IN-06 | Emoji / 生僻字 | 名称含 emoji、生僻字、RTL 控制符 | 保存成功且列表/弹窗/下拉回显不乱码不破版 |
| B-IN-07 | 零宽字符重名绕过 | "套餐A" 与 "套餐A+零宽连接符" | 服务端按去空白/原始串口径处理，两条预期口径需先与开发确认（记录实测行为） |
| B-DUP-01 | 版本重名 | 建"基础版"两次 | 第二次失败（服务端兜底），无重复行 |
| B-DUP-02 | 套餐重名 | 同名套餐 | 业务异常 `TenantPackageNameDuplicate`，文案友好；DB 唯一索引兜底 |
| B-DUP-03 | 改名撞名 | 套餐改名成已有名 | 拒绝（仅名称变化时才查重，自身原名允许） |
| B-CON-01 | 双击/连点提交 | 快速双击保存按钮 | 仅创建一条；无重复行、无 500 |
| B-CON-02 | 乐观锁冲突 | 双 Tab 编辑同一租户先后保存 | 后保存者失败或先保存者被覆盖（按 ABP 并发戳语义），给用户可理解报错而非 500 |
| B-CON-03 | 删除后再删 | 两 Tab 各自删除同一版本 | 第二次得到友好 404/业务错误 |
| B-ID-01 | 不存在 ID 直访 | `browser_evaluate` fetch GET `/api/app/edition/{random-guid}`、套餐同 | 404 / EntityNotFound，非 500 |
| B-ID-02 | 已删除资源续操作 | 删除后 PUT 该版本 | 404/405 类友好错误 |
| B-PG-01 | 分页滥用 | fetch `?MaxResultCount=1000&SkipCount=-1`、非法 sorting 字段 | 不 500：或被约束到默认分页，或 400 |
| B-PG-02 | 排序注入 | sorting=`DisplayName;DROP TABLE` | 正常返回或 400，无异常堆栈 |
| B-FE-01 | 功能值数字边界 | FeatureModal 填 `-1`、`abc`、超大整数、空 | 负数/非数字被 feature validator 拒绝；空恢复默认 |
| B-CS-01 | 连接串乱填 | 填普通字符串、超长 1025、SQL 片段 | 测试连接→通用失败文案；保存→长度校验 400；无堆栈泄露 |
| B-CS-02 | 掩码回提交 | 抽屉原样保存（值为掩码） | PUT 200 且原值保持（不把掩码存为真值） |
| B-UI-01 | 模态残留 | 打开新建→取消→再打开 | 表单无上次残留（tenants 弹窗有 destroyOnHidden，套餐弹窗需实测） |

### C. 黑客攻击（安全）

| ID | 名称 | 步骤要点 | 预期 |
|---|---|---|---|
| C-AUTH-01 | 未认证访问 | 无令牌上下文（隐身上下文）fetch 全部 SAAS 端点 | 302→错误页/401，绝无业务数据（sectest 已知边界：宿主 302 改写） |
| C-AUTHZ-01 | 无权限用户 | `sec-none` 打开三个 SAAS 路由 + fetch 对应 API | 路由 403/重定向；API 403 |
| C-AUTHZ-02 | 只读越权 | `sec-edition-ro` 在 GUI 上尝试创建（按钮应隐藏）+ 直接 fetch POST | GUI 无入口；API 403 |
| C-AUTHZ-03 | 缺删除权限 | `sec-edition-cu` fetch DELETE | 403 |
| C-AUTHZ-04 | 模块间水平越权 | `sec-pkg-mgr` fetch `/api/app/edition*` 全套 | 403（TenantPackages 权限不通行 Editions） |
| C-AUTHZ-05 | 租户用户打 Host API | `t-admin` 登录后 fetch 版本/套餐/连接串端点 | 权限层 403 或 EnsureHostSide 业务异常，绝无数据返回 |
| C-AUTHZ-06 | Feature provider 越权 | 无 `Editions.ManageFeatures` 者调 `providerName=E`；无 `Tenants.ManageFeatures` 者调 `providerName=T` | 403（ProviderPolicies 映射生效，不是 500 "No policy"） |
| C-AUTHZ-07 | providerKey 篡改 | 有租户功能权限者把 providerKey 换成其他租户 ID 读写 | 按 ABP 提供者策略预期行为记录（该权限本质是全局管理权限，验证现状并记录） |
| C-IDOR-01 | ID 遍历 | 低权限+Default 权限账号枚举 tenant/edition/package GUID | 只读集合类接口按权限返回；Default 语义 = 允许读列表（ABP 惯例，记录现状） |
| C-XSS-01 | 存储型 XSS-版本名 | 新建版本名 `"><img src=x onerror=alert(1)>`、`<svg onload=…>`、`<script>` | 列表/编辑弹窗/租户页版本下拉均**原样转义**，无脚本执行、无 DOM 注入（`browser_console_messages` 无报错弹窗） |
| C-XSS-02 | 存储型 XSS-备注 | 套餐备注注入 XSS + 超长触发 ellipsis tooltip | tooltip/ellipsis 渲染转义 |
| C-XSS-03 | 反射型 XSS | 路由/查询参数带 payload 访问列表页 | 无回显执行 |
| C-SQLI-01 | Filter 注入 | 套餐筛选输入 `' OR 1=1--`、`%;` 等 | 按普通文本处理，返回空或精确匹配，无报错泄露 |
| C-SQLI-02 | sorting 注入 | 同 B-PG-02 | 不 500、无堆栈 |
| C-LEAK-01 | 明文连接串泄露 | 抓取全部连接串相关 GET 响应体 | 任何响应不含明文；只含 `********` |
| C-LEAK-02 | 错误信息泄露 | 制造各类 400/404/409 | 响应不含堆栈、SQL、驱动版本、内部路径；连接串测试失败只给本地化通用文案 |
| C-LEAK-03 | 审计日志泄露 | 更新/测试连接串后查审计日志（宿主审计页面或 API） | action parameters 中无明文连接串（`[DisableAuditing]` 生效） |
| C-MASS-01 | 参数篡改-ExtraProperties | 用 update 接口在 extraProperties 里塞 `EditionId`、`ActivationState` 等直改 | 行为按现有反序列化白名单记录（前端 constants 镜像这些键，验证服务端是否校验来源，超权改动需有权限门槛） |
| C-MASS-02 | 越权 set-tenant-edition | 仅 TenantPackages 权限者调 `/set-tenant-edition` | 403 |
| C-CSRF-01 | 跨站伪造 | 外部页面构造对 `/api/app/tenant-package` 的跨站 POST | 按 ABP antiforgery/同站 Cookie 配置被拒（经 18000 nginx 边界实测） |
| C-OPS-01 | 删除轰炸防护 | 连续 DELETE 20 次（脚本循环） | 限流或正常逐条 404；无 500、无雪崩 |

### D. 权限矩阵（GUI 门控 + API 双验证）

| ID | 检查项 |
|---|---|
| D-MX-01 | `canManageEditions=false` 用户访问 `/administration/saas/editions`：路由拦截 + 版本下拉在租户页仍可用性评估（只读 lookup 是否放行，记录现状） |
| D-MX-02 | `canManageTenantPackages.*` 细粒度：仅 Create 者只见"新建"不见编辑/删除/菜单配置 |
| D-MX-03 | `canManageEditionFeatures` 缺失时版本页无"功能"入口，API 403 |
| D-MX-04 | `canManageTenantFeatures` 缺失时租户页无功能入口；`canManageHostFeatures` 同理 |
| D-MX-05 | 连接串抽屉：无 `ManageConnectionStrings` 权限时入口隐藏 + API 403；设置开关关闭时同样隐藏 |
| D-MX-06 | `canImpersonateTenants` 缺失时无模拟登录入口 |

### E. 业务状态机（跨模块联动）

| ID | 名称 | 步骤要点 | 预期 |
|---|---|---|---|
| E-ED-01 | 迁移到自身 | 删除版本时目标选自己 | 业务异常 `CannotMoveTenantsToSameEdition`（GUI 选不了自己，API 直调必拒） |
| E-ED-02 | 迁移目标不存在 | API 直调伪造 target id | EntityNotFound，非 500 |
| E-ED-03 | 迁移后缓存失效 | 删除迁移后立刻访问该租户业务 | 租户配置已按新版本生效（不出现旧版本残留超过合理窗口） |
| E-ED-04 | 到期版本自然失效 | 设 `EditionEndDateUtc` 为过去（DB 直改） | 租户运行时无版本（feature 回退默认），关联仍在（续期恢复）；最多 1h 缓存滞后为**已知设计** |
| E-TP-01 | 空套餐应用 | 应用"空套餐" | 拒绝，`TenantPackageMenusEmpty` 文案 |
| E-TP-02 | 非法菜单 ID | API 直调 PUT menu-selection 塞随机 GUID | 拒绝，错误 data 带非法 ID 计数 |
| E-TP-03 | 套餐应用=破坏性重置 | 应用前租户手工勾了额外菜单 → 应用套餐 | 警示文案明确；应用后菜单与角色授权被重置为套餐集合 |
| E-TP-04 | 置空套餐=全量重置 | ApplyPackageModal 不选套餐 | 警示文案；应用后全量菜单 |
| E-TN-01 | 删除租户清理 | 删除用过的租户后重建同名租户 | 无 unique 索引冲突（菜单/授权硬删生效），重建成功 |
| E-CS-01 | 掩码含值拒绝 | 提交值中含 `********` 字面量 | 业务异常，不落库 |
| E-CS-02 | 未知数据库名 | PUT connection-strings 塞未登记名称 | `InvalidTenantConnectionStringName` |
| E-CS-03 | 开关关闭 | 设置 `AbpAdmin.Saas.EnableTenantBasedConnectionStringManagement=false` | 4 个端点全部 `TenantConnectionStringManagementDisabled`；GUI 抽屉入口消失 |
| E-CS-04 | 默认库触发建库 | 提交新的 Default 串（sectest 栈用 fake creator 观察） | 幂等建库+迁移事件，无重复执行报错 |

### F. UI 交互与路由

| ID | 检查项 |
|---|---|
| F-UI-01 | 旧路由 `/saas/tenants`、`/saas/editions` 重定向到新路径 |
| F-UI-02 | 三页表格加载、分页（默认 10 条）、套餐名筛选生效 |
| F-UI-03 | 删除类操作均有 Popconfirm/警示（版本删除迁移弹窗、套餐删除、租户删除、应用套餐破坏性警示） |
| F-UI-04 | 删除版本弹窗的租户数 Alert 在有/无租户两种状态下文案正确切换 |
| F-UI-05 | 租户页列渲染：版本名、激活状态 Tag 颜色（绿/橙/红）、版本到期时间 |
| F-UI-06 | 租户 ID 可复制；连接串值掩码显示、眼睛切换不出现明文 |

---

## 4. Playwright 执行方案

### 4.1 工具与技巧映射

| 场景 | MCP Playwright 用法 |
|---|---|
| 常规 GUI 操作 | `browser_navigate` → `browser_snapshot`（拿 ref）→ `browser_click` / `browser_type` / `browser_fill_form` |
| 表单校验/边界输入 | `browser_type`（slowly 触发前端校验）+ 提交后 `browser_snapshot` 查错误文案 |
| API 级断言（认证上下文内） | `browser_evaluate` 执行 `fetch('/api/app/edition', {method:'POST',...})`，读取状态码与响应体；**避免反复登录** |
| 多角色隔离 | `browser_tabs` 的 `isolatedContext` 为每个账号开独立上下文，各自登录一次 |
| 网络取证 | `browser_network_requests`（filter `/api/app/(edition|tenant)`）+ `browser_network_request` 看请求/响应体 |
| XSS 取证 | 注入后 `browser_console_messages`（error 级）+ `browser_evaluate` 检查 `document.querySelectorAll('img[src=x]')` 等注入痕迹 |
| 视觉证据 | 关键步骤 `browser_take_screenshot`（尤其破坏性操作弹窗、越权 403 页、XSS 渲染结果） |
| 弹窗对话框 | `browser_handle_dialog`（原生 confirm/alert，若出现） |

### 4.2 执行阶段顺序（降低数据污染与限流影响）

1. **P0 准备**：拉起环境 → admin 登录 → 建账号矩阵与基础数据 → 各角色 isolatedContext 登录并缓存令牌（注意 LoginByIp 20 次/15min 限流；上下文复用令牌，勿反复登出登录）。
2. **P1 功能正确性**（A 组）：只产生干净数据，逐条留截图。租户登录类用例（A-AC-02/03）放在本阶段。
3. **P2 滥用**（B 组）：在 P1 数据基础上做边界与重名；脏数据随建随删。
4. **P3 状态机**（E 组）：跨模块联动，产生大改动，放在功能之后。
5. **P4 安全**（C 组）：产生垃圾 payload 数据与日志噪音，最后跑；结束后用 admin 清理注入型数据。
6. **P5 权限矩阵**（D 组）：切上下文验证 GUI 门控（无数据写入），穿插在 P4 后。
7. **P6 UI 细节**（F 组）：收尾核对。

### 4.3 数据清理

- 每个 payload 实体（XSS 版本名等）用完即删（admin 上下文 DELETE）。
- sectest 栈可用 `down -v` 一键重置；本地栈结束前删除 `t-func`、`t-pkg`、payload 版本/套餐。

---

## 5. 通过标准与缺陷分级

- **通过**：A 组全过；B/C/D/E/F 组无 P0/P1 缺陷，P2/P3 缺陷有Issue记录并评估。
- 缺陷分级：
  - **P0**：越权成功/数据泄露（明文连接串、跨租户读写）、XSS 执行、500 崩溃、破坏性操作无确认。
  - **P1**：业务规则失效（重名、空套餐可应用、掩码被存为真值）、校验旁路。
  - **P2**：文案/回显/门控显隐不一致、友好性差。
  - **P3**：体验建议。

## 6. 与既有测试的关系（避免重复造轮子）

- 后端单元测试已覆盖：版本过期/迁移/计数、功能策略与清理、激活检查、连接串加解密与审计、租户种子、套餐 CRUD/应用（`backend/test/AbpAdmin.Application.Tests/Saas/` 等）。GUI 计划侧重**集成面与攻击面**，后端已证实的领域规则在 GUI 侧只验证"用户可见的表现"。
- `sectest/`（Python 黑盒探针）已覆盖 API 层防线复现。本计划的 C 组与之互补：sectest 出报告优先，GUI 侧重 XSS 渲染、GUI 门控、掩码显示等人机界面攻击面。

## 7. 已知边界与范围外

1. **已知名遗留缺口**：不带 `__tenant` 头的 `/connect/token` 不做租户激活校验（代码注释自认）。测试中若复现按"已知遗留"记录，不算新缺陷。
2. **连接串解密后明文进分布式缓存**：已记录的架构权衡，本计划只验证"不通过 API/日志泄露"。
3. **版本到期 1h 缓存滞后**：设计行为，不按缺陷报。
4. **nginx 丢 `__tenant` 下划线头**（sectest 栈）：跨租户头注入向量只在 18081 直连口测。
5. 不做：压力/性能测试、多浏览器兼容矩阵、租户数据库实际迁移的全量演练（仅验证事件触发）。
