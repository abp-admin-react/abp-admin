# sectest 严格重测报告（第三轮 · 从零重建 + 变体加压 + 缺口修复复验）

时间：2026-10-31（本轮）　栈：abpadmin-sectest（web http://127.0.0.1:18000 · host 直连 http://127.0.0.1:18081）

## 0. 方法

1. **从零重建**：`down -v` 删数据卷 + 删令牌缓存，全新 DB 全新迁移（消除前两轮任何状态残留与 a17 自污染风险）。
2. **全量复跑** harness（A 组 17 修复面防线 + B 组 8 漏洞复现）。
3. **变体加压**（`sectest/probes_extra.py`，32 项）：对 b01–b08 每个修复面做 harness 未覆盖的变体攻击——换边界（HOST 直连 / Cookie 租户 / 无租户头）、换路由形态（大小写 / 百分号编码 / 双斜杠 / 尾斜杠 / 姊妹路由）、换通道（JSON with-bytes）、换参数（停用账户 / 混合通知列表 / 全金面端点）+ 每面的**对照组**（合法操作不得被误伤）。
4. **实盘验证** b08 fail-fast（一次性隔离 compose 项目 + 独立 DB 卷）。

## 1. 结果总览

| 轮次 | A 组（防线） | B 组（复现） | 加压探测 | 结论 |
|---|---|---|---|---|
| 重建后首轮 | 17/17 PASS | 0/8 复现 | — | 前 14 项修复全部在全新栈上成立 |
| 变体加压 | — | — | 30 项 → **2 个真实缺口** | 见 §2 |
| 修复+复验 | 17/17 PASS | 0/8 复现 | 32 项 → 27 PASS + 4 已定性非漏洞 + 1 观察 | **全部关闭** |

单测：AbpAdmin.Ledger.Tests **86/86**，AbpAdmin.HttpApi.Host.Tests **123/123**（含新增 9 个回归锁用例）。

## 2. 加压轮新发现的缺口（已修复并复验）

### g1 · with-bytes JSON 通道绕过上传内容守卫（b02 修复面残余，已实锤+已修）

- **攻击**：`POST /api/file-management/file/with-bytes`，JSON body `{"fileName":"evil-wb.jpg","content":"<base64 MZ>"}`（DTO 字段名经 DLL 元数据核实为 `content`/`fileContents`）→ **200 落库**，下载直链可取回 80 字节原始 MZ 内容。守卫中间件只扫 `multipart/form-data`，前缀收口只解决了路由枚举面，没解决 Content-Type 面。
- **修复**：`FileUploadContentGuardMiddleware` 增补 JSON 通道扫描——`content`/`fileContents`（base64 字节，单值或数组）× `fileName`/`fileNames` 与 multipart 共用同一套魔数/脚本判定；有界读入（64MiB 上限，超限 fail-closed 拒绝，防匿名 JSON DOM 内存放大）；非法 JSON 交由模型绑定报错。
- **复验**：MZ-jpg → 400/ContentMismatch 零落库；合法 PNG → 200 落库（不过度封锁）。单测 +8。

### g2 · FinReconcile RunAsync 缺切片 fail-closed（NV-02 残余，已实锤+已修）

- **攻击**：切片主体（Ledger.View + Scope.Agent）被授予 `Reconcile.Run` 后 `POST /api/app/fin-reconcile/run` → **200**，响应直接携带租户级全量汇总（DiffJson：平台借贷合计 + 全部子户投影差），且落库租户级 run 行。「Run 权限不授予切片操作员」只剩管理纪律、没有强制不变式；全 Ledger 模块审计确认 RunAsync 是唯一漏挂守卫的聚合面。
- **修复**：`FinReconcileAppService.RunAsync` 顶部补 `ResolveAgentScopeAsync() != null` → `AbpAuthorizationException`（与 GetList/FinOverview 同口径）。
- **复验**：agent+Run POST run → 302 拒绝（授权回收后）；admin 照常可读。单测 +1（`Reconcile_Run_Is_Fail_Closed_For_Agent_Slice_Even_With_Run_Granted`，受测主体 TestUsers.AgentWithRun）。

## 3. 变体探测已定性为「非漏洞」的形态（防误报记录）

| 探测 | 形态 | 定性 |
|---|---|---|
| p24 | 租户 admin 读 prepayment transaction/withdrawal-record 列表与 by-id → **404** | 钉 Host 权限使端点对租户侧**不可见**（比 403 更早的拒绝层），零数据 |
| p25 | 租户 admin 读 withdrawal-request 列表 → **200** | 跨租户对照实验：插入宿主行后租户列表不可见 + by-id 404，宿主可见 ⇒ 租户过滤的自有数据（by design） |
| p35 | 宿主 admin → 租户用户定向通知 → **403** | 宿主上下文用户查询同样被租户过滤 → EmptyUserIds（保守拒绝，无跨租投递） |
| p01 残余形态 | 错误字段名（bytes/name）→ 400/500 | 模型绑定失败，无落库 |

其余 24 项变体全部 PASS：停用/锁定账户双边界拒绝且还原后可用（b01）；Cookie 租户/无租户头/宿主目标三种跨租户铸造全拒 + 宿主→租户运维语义保留 200（b04）；4 条金面路由 × 双边界全 302 + 宿主对照组过门（b03）；大小写/百分号/双斜杠/尾斜杠路径全进守卫（b02）；fin-demo-event 环境门连宿主 admin 全权限也拦（b06）；通知三通道 EmptyUserIds + 混合列表「A 收到、B 零幽灵行」落库证据（b07）。

## 4. b08 fail-fast 实盘验证（隔离栈）

一次性 compose 项目（独立项目名/网络/卷，默认 `DOTNET_ENVIRONMENT=Production`，不注入 `Identity__AdminPassword`）：

- 迁移器**非零退出（139）**，日志明示：「Identity:AdminPassword 未配置：种子 admin 将使用模板公开默认密码（全网已知），已拒绝继续…」
- 随后直查该栈 DB：`AbpUsers` **0 行**、`OpenIddictApplications` TestCli **0 行**——零开发凭据落地。
- 已 `down -v` 清理。

## 5. 产物与边界

- harness 报告：`sectest/REPORT-sectest.md`（修复后最新一轮）；加压报告：`sectest/REPORT-probes-extra.md`（32 项）。
- 复跑：`python sectest/sectest.py` + `python sectest/probes_extra.py`（sectest 栈仍在运行）。
- 变更文件：`FileUploadContentGuardMiddleware.cs`（+JSON 通道）、`FinReconcileAppService.cs`（+RunAsync 守卫）、`FileUploadContentGuardTests.cs`（+8 用例）、`ReconcileAndBizDocTests.cs` + `LedgerTestModule.cs`（+回归锁与受测主体）、`sectest/probes_extra.py`（新）。
- **边界提示**：维护栈 `abp-admin-*` 容器仍是修复前镜像，下次重建（`up -d --build`）后才会带上 g1/g2 修复；存量租户的 `EasyAbp.PaymentService.*` 授权行清理仍按 owner 动作执行（本次从零重建的新租户不受影响）。
