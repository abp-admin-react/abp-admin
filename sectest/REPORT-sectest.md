# sectest 黑盒攻击验证报告（abpadmin-sectest 栈）

时间：2026-11-08 10:11:09  web=http://127.0.0.1:18000 host=http://127.0.0.1:18081

## A 组 · 修复面防线（必须 PASS）

- ✅ **a01_health_anonymous** — health-status=200; protected={'/health-ui': 302, '/health-api': 302, '/healthchecks-webhooks': 302}（401/302=受保护）
- ✅ **a02_user_claims_role_rejected** — user-claims 三种权威类型全部被拒（IdentityClaimsCannotCarryRole）
- ✅ **a03_role_claims_role_rejected** — role-claims 三种权威类型全部被拒（run-2 C-1）
- ✅ **a04_claims_allowed_type_ok** — status=204 body=
- ✅ **a05_ou_attach_admin_denied** — status=403 code=AbpAdmin:DataScope:RoleDataScopeEscalation
- ✅ **a06_ou_attach_own_role_allowed** — status=204
- ✅ **a07_ou_member_into_privileged_denied** — status=403 code=AbpAdmin:DataScope:RoleDataScopeEscalation
- ✅ **a08_settingui_fundswitch_denied** — write resp=204; halt after attack = True（须仍为 True）
- ✅ **a09_settingui_ui_setting_writable** — resp=204 value=120
- ✅ **a10_gateway_rotate** — old401=True new200=True session-rejected=True
- ✅ **a11_sms_intranet_endpoint_blocked** — status=400 code=Client:OperationFailed leaked=False egress_attempt=False
- ✅ **a13_tenant_mismatch_guard** — direct=403({"error":{"code":"Client:Forbidden","message":"You do not have permiss)——403 失配守卫或 401 跨租会话不可见均为拦截
- ✅ **a14_anonymous_surface** — statuses={'/api/app/audit-log': 401, '/api/app/identity-user-admin': 401, '/api/setting-ui': 401, '/api/app/cache-monitor': 401, '/api/app/server-monitor': 401, '/api/app/background-job': 401, '/api/app/fin-manual-adjust': 401}
- ✅ **a15_upload_content_guard** — mz-jpg=400/AbpAdmin:Files:ContentMismatch png=200/{"fileInfo":{"parentId":null,"fileContainerName":"admin","fileName":"ok(6).png",
- ✅ **a16_oidc_manageperms_gate** — admin=500 operator=403（operator 须 403/302，admin 须过门）
- ✅ **a17_connstring_audit_exit** — update=204; audit 泄漏=False（已还原共享库）
- ✅ **a12_token_bruteforce_ratelimited** — first 429 at attempt 6/25

## B 组 · open 线索复现（⚠ = 漏洞行为当前仍可复现，待修复后转绿）

- ✅(未复现) **b01_impersonate_locked_account** — impersonation of locked account → 400（须非 200：锁定即不可铸造令牌）
- ✅(未复现) **b02_upload_sibling_route** — with-stream MZ-jpg → 400/AbpAdmin:Files:ContentMismatch（须 400/ContentMismatch）
- ✅(未复现) **b03_prepayment_change_balance** — tenantB-admin change/balance(随机账户) → 403 {"error":{"code":"Client:Forbidden","message":"You do not have permission to per（须 403/302：钱面权限已钉 Host）
- ✅(未复现) **b04_impersonation_cross_tenant** — direct tenantA-admin → tenantB user: 400（须非 200：跨租户铸造已拒绝）
- ✅(未复现) **b05_finreconcile_sliced_read** — sliced operator GET fin-reconcile → 403（须 403：切片 fail-closed）
- ✅(未复现) **b06_findemoevent_sod** — Adjust.Create-only POST fin-demo-event → 400（须非 2xx：非 Development 拒绝）
- ✅(未复现) **b07_notification_cross_tenant** — tenantA → tenantB 用户定向通知 → 400（须非 2xx：租户外 Id 已过滤）
- ✅(未复现) **b08_default_admin_and_ropc** — compose 渲染默认 DOTNET_ENVIRONMENT='Production'（须 Production：默认不种开发凭据）

汇总：A 组 17/17 通过；B 组复现 0/8。
