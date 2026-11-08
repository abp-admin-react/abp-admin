#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sectest 加压轮：harness 未覆盖的变体攻击面（黑盒，全部以"防线必须成立"断言）。
复用 sectest.py 的请求基座与令牌缓存（避免 ROPC 限流）。
每个探测独立 try/except，探后还原状态。
"""
import base64
import json
import os
import subprocess
import sys
import time
import urllib.parse
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sectest as S  # noqa: E402

WEB, HOST = S.WEB, S.HOST
admin = S.login(S.ADMIN_USER, S.ADMIN_PASS)
imp = S.login("sectest.imp", S.TESTPASS)
finop = S.login("sectest.finop", S.TESTPASS)
agent = S.login("sectest.agent", S.TESTPASS)
tenantA = S.login("admin", S.ADMIN_PASS, tenant="sectesta")
tenantB = S.login("admin", S.ADMIN_PASS, tenant="sectestb")

MZ_JPG = S.MZ_JPG
PNG = S.PNG_1x1


def victim_id():
    r = S.api("GET", "/api/identity/users?Filter=" + urllib.parse.quote("sectest.victim"), token=admin)
    return next(x["id"] for x in r.json["items"] if x["userName"] == "sectest.victim")


def psql(sql):
    return subprocess.run(["docker", "exec", "abpadmin-sectest-db-1", "psql", "-U", "postgres",
                           "-d", "abpadmin", "-t", "-c", sql], capture_output=True, timeout=15).stdout.decode()


def tenant_admin_uid(tenant):
    out = psql("select u.\"Id\" from \"AbpUsers\" u join \"AbpTenants\" t on u.\"TenantId\"=t.\"Id\" "
               f"where u.\"UserName\"='admin' and t.\"Name\"='{tenant}';")
    return out.strip().split("|")[0].strip()


VID = victim_id()
BUID = tenant_admin_uid("sectestb")
AUID = tenant_admin_uid("sectesta")

ROWS = []


def probe(name, ok, detail):
    ROWS.append((name, ok, detail))
    print(f"[{'PASS' if ok else 'FIND'}] {name} :: {detail}")


# ---------------------------------------------------------------- b02 变体：上传守卫收口面
def up_json(path, payload):
    return S.req(WEB, "POST", path, token=admin,
                 json_body=payload) if False else S.req(
        WEB, "POST", path, token=admin, data=json.dumps(payload).encode(),
        headers={"Content-Type": "application/json"})


def p01_withbytes_json_mz():
    """疑点：守卫只对 multipart 生效；with-bytes 走 JSON body 承载字节。
    若 JSON 通道能存入 MZ 改名文件 → 前缀收口的残余缺口。"""
    b64 = base64.b64encode(MZ_JPG).decode()
    for shape in (
        {"fileContainerName": "admin", "fileType": 2, "generateUniqueFileName": False,
         "fileName": "evil-wb.jpg", "bytes": b64},
        {"fileContainerName": "admin", "fileType": 2, "generateUniqueFileName": False,
         "name": "evil-wb.jpg", "bytes": b64},
        {"fileContainerName": "admin", "fileType": 2, "generateUniqueFileName": False, "bytes": b64},
    ):
        r = up_json("/api/file-management/file/with-bytes", shape)
        if r.status in (200, 201):
            # 落库确认：查 admin 容器文件列表有没有 MZ 内容文件
            lst = S.req(WEB, "GET", "/api/file-management/file?FileContainerName=admin&MaxResultCount=50",
                        token=admin)
            names = json.dumps(lst.json, ensure_ascii=False) if lst.json else ""
            probe("p01_withbytes_json_mz", False,
                  f"JSON with-bytes 存入成功 status={r.status} body={r.text()[:120]} 列表={names[:200]}")
            return
        print(f"      shape keys={list(shape)} -> {r.status} {S.error_code(r) or r.text()[:90]}")
    probe("p01_withbytes_json_mz", True, "三种 JSON 形态全部非 2xx（with-bytes JSON 通道未成缺口）")


def _mz_multipart():
    return S.multipart({"FileContainerName": "admin", "FileType": "2", "GenerateUniqueFileName": "false"},
                       {"File": ("evil.jpg", MZ_JPG, "image/jpeg")})


def p03_many_withstream():
    body, ct = _mz_multipart()
    r = S.req(WEB, "POST", "/api/file-management/file/many/with-stream", data=body, token=admin,
              headers={"Content-Type": ct})
    probe("p03_many_withstream", r.status == 400 and "ContentMismatch" in (S.error_code(r) or ""),
          f"{r.status}/{S.error_code(r) or r.text()[:60]}")


def p04_many_form():
    body, ct = _mz_multipart()
    r = S.req(WEB, "POST", "/api/file-management/file/many", data=body, token=admin,
              headers={"Content-Type": ct})
    probe("p04_many_form", r.status == 400 and "ContentMismatch" in (S.error_code(r) or ""),
          f"{r.status}/{S.error_code(r) or r.text()[:60]}")


def p05_case_path():
    body, ct = _mz_multipart()
    r = S.req(HOST, "POST", "/API/FILE-MANAGEMENT/FILE/with-stream", data=body, token=admin,
              headers={"Content-Type": ct})
    ok = r.status not in (200, 201)
    probe("p05_case_path", ok, f"大写路径 → {r.status} {S.error_code(r) or r.text()[:60]}（须非 2xx）")


def p06_percent_path():
    body, ct = _mz_multipart()
    r = S.req(HOST, "POST", "/api/file-management/%66ile/with-stream", data=body, token=admin,
              headers={"Content-Type": ct})
    ok = r.status not in (200, 201)
    probe("p06_percent_path", ok, f"%66ile 编码 → {r.status} {S.error_code(r) or r.text()[:60]}（须非 2xx）")


def p07_double_slash():
    body, ct = _mz_multipart()
    r = S.req(HOST, "POST", "/api/file-management//file/with-stream", data=body, token=admin,
              headers={"Content-Type": ct})
    ok = r.status not in (200, 201)
    probe("p07_double_slash", ok, f"双斜杠 → {r.status}（须非 2xx）")


def p08_trailing_slash():
    body, ct = _mz_multipart()
    r = S.req(HOST, "POST", "/api/file-management/file/with-stream/", data=body, token=admin,
              headers={"Content-Type": ct})
    ok = r.status not in (200, 201)
    probe("p08_trailing_slash", ok, f"尾斜杠 → {r.status} {S.error_code(r) or r.text()[:60]}（须非 2xx）")


# ---------------------------------------------------------------- b01 变体：停用/锁定/恢复
def p09_impersonate_deactivated():
    u = S.req(WEB, "GET", f"/api/identity/users/{VID}", token=admin).json
    u["isActive"] = False
    u["roleNames"] = []
    r = S.req(WEB, "PUT", f"/api/identity/users/{VID}", token=admin, json_body=u)
    if r.status not in (200, 204):
        probe("p09_impersonate_deactivated", False, f"停用失败 {r.status} {r.text()[:120]}")
        return
    rr = S.req(WEB, "POST", "/connect/token", token=imp, form={
        "grant_type": "impersonation", "user_id": VID, "client_id": "AbpAdmin_App"})
    # 还原
    u2 = S.req(WEB, "GET", f"/api/identity/users/{VID}", token=admin).json
    u2["isActive"] = True
    u2["roleNames"] = []
    S.req(WEB, "PUT", f"/api/identity/users/{VID}", token=admin, json_body=u2)
    ok = rr.status != 200
    probe("p09_impersonate_deactivated", ok,
          f"停用账户 impersonation → {rr.status}（须非 200），已还原激活")


def p10_impersonate_locked_host_direct():
    S.api("POST", f"/api/app/identity-user-admin/{VID}/lock", token=admin)
    r = S.req(HOST, "POST", "/connect/token", token=imp, form={
        "grant_type": "impersonation", "user_id": VID, "client_id": "AbpAdmin_App"})
    S.api("POST", f"/api/app/identity-user-admin/{VID}/unlock", token=admin)
    ok = r.status != 200
    probe("p10_impersonate_locked_host_direct", ok, f"HOST 直连锁定账户 → {r.status}（须非 200），已解锁")


def p11_impersonate_restored_ok():
    r = S.req(WEB, "POST", "/connect/token", token=imp, form={
        "grant_type": "impersonation", "user_id": VID, "client_id": "AbpAdmin_App"})
    ok = r.status == 200
    probe("p11_impersonate_restored_ok", ok, f"还原后 impersonation → {r.status}（须 200：未过度封锁）")


# ---------------------------------------------------------------- b04 变体：租户边界换形态
def _imp_form(uid, tok):
    return S.req(HOST, "POST", "/connect/token", token=tok, form={
        "grant_type": "impersonation", "user_id": uid, "client_id": "AbpAdmin_App"})


def p12_cross_no_tenant_header():
    r = _imp_form(BUID, tenantA)
    ok = r.status != 200
    probe("p12_cross_no_tenant_header", ok, f"tenantA→B 用户无租户头 → {r.status}（须非 200）")


def p13_tenant_admin_to_host_user():
    r = _imp_form(VID, tenantA)
    ok = r.status != 200
    probe("p13_tenant_admin_to_host_user", ok, f"tenantA→宿主用户 → {r.status}（须非 200）")


def p14_cross_cookie_tenant_host():
    r = S.req(HOST, "POST", "/connect/token", token=tenantA, form={
        "grant_type": "impersonation", "user_id": BUID, "client_id": "AbpAdmin_App"},
        headers={"Cookie": "__tenant=sectestb"})
    ok = r.status != 200
    probe("p14_cross_cookie_tenant_host", ok, f"HOST+Cookie __tenant=sectestb → {r.status}（须非 200）")


def p15_cross_cookie_tenant_web():
    r = S.req(WEB, "POST", "/connect/token", token=tenantA, form={
        "grant_type": "impersonation", "user_id": BUID, "client_id": "AbpAdmin_App"},
        headers={"Cookie": "__tenant=sectestb"})
    ok = r.status != 200
    probe("p15_cross_cookie_tenant_web", ok, f"WEB+Cookie __tenant=sectestb → {r.status}（须非 200）")


def p16_host_to_tenant_still_works():
    r = _imp_form(BUID, admin) if False else S.req(
        HOST, "POST", "/connect/token", token=admin, form={
            "grant_type": "impersonation", "user_id": BUID, "client_id": "AbpAdmin_App"},
        tenant="sectestb")
    ok = r.status == 200
    probe("p16_host_to_tenant_still_works", ok, f"宿主admin→B 用户（宿主运维语义） → {r.status}（须 200）")


# ---------------------------------------------------------------- b03 变体：全部金面路由 × 双边界
PREPAY_ROUTES = [
    ("/api/payment-service/prepayment/account/{g}/change/balance", {"changedBalance": 1.0}),
    ("/api/payment-service/prepayment/account/{g}/change/locked-balance", {"changedLockedBalance": 1.0}),
    ("/api/payment-service/prepayment/account/{g}/top-up", {"amount": 1.0}),
    ("/api/payment-service/prepayment/account/{g}/withdraw", {"amount": 1.0}),
]


def p17_22_prepayment_all():
    g = str(uuid.uuid4())
    details = []
    ok = True
    for path, body in PREPAY_ROUTES:
        p = path.format(g=g)
        for base, tag in ((HOST, "host"), (WEB, "web")):
            r = S.req(base, "POST", p, token=tenantB, json_body=body)
            denied = r.status in (401, 403, 302)
            ok = ok and denied
            details.append(f"{tag}:{p.split('/account/')[1].split('/')[0]}={r.status}")
    probe("p17_22_prepayment_all", ok,
          "租户B admin 全金面路由×双边界: " + ", ".join(details) + "（须全 403/302）")


def p23_prepayment_host_admin_control():
    g = str(uuid.uuid4())
    details = []
    ok = True
    for path, body in PREPAY_ROUTES[:2]:
        p = path.format(g=g)
        r = S.req(HOST, "POST", p, token=admin, json_body=body)
        passed = r.status not in (401, 403, 302)
        ok = ok and passed
        details.append(f"{p.rsplit('/', 1)[1]}={r.status}")
    probe("p23_prepayment_host_admin_control", ok,
          "宿主admin 对照组（须过授权门）: " + ", ".join(details))


def p24_prepayment_transaction_read():
    """缺 AccountId：包原实现对 Guid.Empty GetAsync→500；修复后须 400 校验。
    不得再冒成 500（Client:Unexpected）。跨用户流水门槛 Transaction.Manage 已钉 Host，
    另由 p17 写面与权限钉单测覆盖。"""
    for base, tag in ((HOST, "host"), (WEB, "web")):
        r = S.req(base, "GET", "/api/payment-service/prepayment/transaction?SkipCount=0&MaxResultCount=5",
                  token=tenantB)
        ok = r.status == 400
        probe(f"p24_prepayment_tx_read[{tag}]", ok,
              f"租户B admin 无 AccountId 读交易列表 → {r.status}（须 400，不得 500）")


def p25_prepayment_withdrawal_request_read():
    """无过滤 GetList = 租户内全量枚举，须 Manage（钉 Host）⇒ 租户 admin 403/302。"""
    for base, tag in ((HOST, "host"), (WEB, "web")):
        r = S.req(base, "GET", "/api/payment-service/prepayment/withdrawal-request?SkipCount=0&MaxResultCount=5",
                  token=tenantB)
        denied = r.status in (401, 403, 302)
        probe(f"p25_prepayment_wr_read[{tag}]", denied,
              f"租户B admin 无过滤读提现申请列表 → {r.status}（须 403/302：Manage 钉 Host）")


# ---------------------------------------------------------------- b05 变体：切片操作员换面
def p26_agent_reconcile_host():
    r = S.req(HOST, "GET", "/api/app/fin-reconcile?SkipCount=0&MaxResultCount=20", token=agent)
    ok = r.status in (401, 403, 302)
    probe("p26_agent_reconcile_host", ok, f"agent HOST 直连 GET → {r.status}（须 403/302）")


def p27_agent_reconcile_run_granted():
    """缺口疑点：RunAsync 只有 Reconcile.Run 权限门，无 agent-scope fail-closed。
    给切片角色授 Run 后 POST run——若 2xx 返回 DiffJson = 租户级汇总泄露（仅策略门）。"""
    r = S.api("GET", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
              token=admin)
    tree = r.json
    run_perm = None
    for g_ in tree.get("groups", []):
        for p_ in g_.get("permissions", []):
            if p_["name"].endswith("Reconcile.Run") or ".Run" in p_["name"] and "Reconcile" in p_["name"]:
                run_perm = p_["name"]
    if not run_perm:
        probe("p27_agent_reconcile_run_granted", None, "未找到 Reconcile.Run 权限名，跳过")
        return
    S.api("PUT", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
          token=admin, json_body={"permissions": [{"name": run_perm, "isGranted": True}]})
    r = S.req(WEB, "POST", "/api/app/fin-reconcile/run", token=agent, json_body={
        "includeDemo": False, "runType": "sectest-probe",
        "rangeStart": "2026-01-01T00:00:00Z", "rangeEnd": "2026-12-31T00:00:00Z"})
    # 还原：撤销 Run
    S.api("PUT", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
          token=admin, json_body={"permissions": [{"name": run_perm, "isGranted": False}]})
    if r.status in (200, 201):
        probe("p27_agent_reconcile_run_granted", False,
              f"agent+Run POST run → {r.status} DiffJson 泄露租户级汇总（{r.text()[:150]}）——NV-02 残余面")
    else:
        probe("p27_agent_reconcile_run_granted", True,
              f"agent+Run POST run → {r.status}（Run 未泄露或 fail-closed）")


def p29_admin_reconcile_ok():
    r = S.req(WEB, "GET", "/api/app/fin-reconcile?SkipCount=0&MaxResultCount=5", token=admin)
    ok = r.status == 200
    probe("p29_admin_reconcile_ok", ok, f"admin GET fin-reconcile → {r.status}（须 200：未过度封锁）")


# ---------------------------------------------------------------- b06 变体：环境门双边界 × admin
def p30_findemo_host_direct():
    r = S.req(HOST, "POST", "/api/app/fin-demo-event", token=finop, json_body={
        "bizType": "demo.credit", "bizRefId": "sectest-" + uuid.uuid4().hex[:10],
        "phase": "pay", "currency": "CNY", "amount": 100, "userId": VID})
    ok = r.status not in (200, 201, 202, 204)
    probe("p30_findemo_host_direct", ok, f"finop HOST 直连 POST → {r.status}（须非 2xx）")


def p31_findemo_host_admin():
    r = S.req(WEB, "POST", "/api/app/fin-demo-event", token=admin, json_body={
        "bizType": "demo.credit", "bizRefId": "sectest-" + uuid.uuid4().hex[:10],
        "phase": "pay", "currency": "CNY", "amount": 100, "userId": VID})
    ok = r.status not in (200, 201, 202, 204)
    probe("p31_findemo_host_admin", ok, f"宿主admin（全权限）POST → {r.status}（须非 2xx：环境门不分权限）")


# ---------------------------------------------------------------- b07 变体：三通道 × 混合列表 × 落库证据
def _send(methods, uids, tok=tenantA):
    return S.req(WEB, "POST", "/api/app/notification-management/send-to-users", token=tok, json_body={
        "userIds": uids, "notificationMethods": methods,
        "title": "sectest-probe", "body": "sectest-probe"})


def p32_email_cross():
    r = _send(["Email"], [BUID])
    ok = r.status not in (200, 204)
    probe("p32_email_cross", ok, f"tenantA→B 用户 Email 通道 → {r.status}（须非 2xx：EmptyUserIds）")


def p33_sms_cross():
    r = _send(["Sms"], [BUID])
    ok = r.status not in (200, 204)
    probe("p33_sms_cross", ok, f"tenantA→B 用户 Sms 通道 → {r.status}（须非 2xx）")


def p34_mixed_delivery():
    """混合列表（A 自己 + B 跨租户）→ 发送须成功且只落在 A；
    B 侧不得出现本探针标题行（幽灵行消除；不按时间窗计数，避免被 p35 宿主合法投递污染）。"""
    marker = "sectest-probe-mixed-" + uuid.uuid4().hex[:8]
    r = S.req(WEB, "POST", "/api/app/notification-management/send-to-users", token=tenantA, json_body={
        "userIds": [AUID, BUID], "notificationMethods": ["InApp"],
        "title": marker, "body": marker})
    if r.status not in (200, 204):
        probe("p34_mixed_delivery", False, f"混合列表发送被拒 {r.status} {r.text()[:120]}")
        return
    time.sleep(3)
    # 列表 DTO 无 title：用唯一 marker 查 NotificationInfo.ExtraProperties（避免时间窗被 p35 污染）
    qa = psql(
        "select count(*) from \"EasyAbpNotificationServiceNotificationInfos\" "
        f"where \"ExtraProperties\"::text like '%{marker}%' "
        f"and \"TenantId\"=(select \"Id\" from \"AbpTenants\" where \"Name\"='sectesta');")
    qb = psql(
        "select count(*) from \"EasyAbpNotificationServiceNotificationInfos\" "
        f"where \"ExtraProperties\"::text like '%{marker}%' "
        f"and \"TenantId\"=(select \"Id\" from \"AbpTenants\" where \"Name\"='sectestb');")
    na = int((qa or "0").strip().split()[0] or 0)
    nb = int((qb or "0").strip().split()[0] or 0)
    ok = na >= 1 and nb == 0
    probe("p34_mixed_delivery", ok,
          f"混合发送 → A 侧标题行={na}（须≥1）B 侧标题行={nb}（须 0）——幽灵行消除落库证据")


def tenant_id(name):
    out = psql(f"select \"Id\" from \"AbpTenants\" where \"Name\"='{name}';")
    return out.strip().split("|")[0].strip()


def p35_host_to_tenant_abp_change():
    """ABP 官网口径：宿主须显式 TenantId（服务端 CurrentTenant.Change），裸 UserId 仍 EmptyUserIds。"""
    tid = tenant_id("sectestb")
    bare = S.req(WEB, "POST", "/api/app/notification-management/send-to-users", token=admin, json_body={
        "userIds": [BUID], "notificationMethods": ["InApp"],
        "title": "sectest-host-bare", "body": "sectest-host-bare"})
    bare_ok = bare.status not in (200, 201, 204)
    r = S.req(WEB, "POST", "/api/app/notification-management/send-to-users", token=admin, json_body={
        "userIds": [BUID], "notificationMethods": ["InApp"],
        "title": "sectest-host-tid", "body": "sectest-host-tid",
        "tenantId": tid})
    scoped_ok = r.status in (200, 204)
    probe("p35_host_to_tenant_abp_change", bare_ok and scoped_ok,
          f"裸发={bare.status}（须非 2xx）+ TenantId={r.status}（须 200/204）tid={tid[:8]}…")


# ---------------------------------------------------------------- 修复回归锁（加压轮缺口关闭验证）
def p36_withbytes_json_mz_guarded():
    """修复回归：with-bytes JSON 通道（content+fileName）MZ 改名文件必须 400/ContentMismatch，
    合法 PNG 必须放行落库（防线不过度封锁）。"""
    import base64
    b64mz = base64.b64encode(MZ_JPG).decode()
    b64png = base64.b64encode(PNG).decode()
    r = S.req(WEB, "POST", "/api/file-management/file/with-bytes", token=admin,
              data=json.dumps({"fileContainerName": "admin", "fileType": 2,
                               "generateUniqueFileName": False, "fileName": "evil-wb.jpg",
                               "content": b64mz}).encode(),
              headers={"Content-Type": "application/json"})
    ok = r.status == 400 and "ContentMismatch" in (S.error_code(r) or "")
    lst = S.req(WEB, "GET", "/api/file-management/file?FileContainerName=admin&MaxResultCount=100",
                token=admin).json
    no_store = not any("evil" in (x.get("fileName") or "") for x in lst.get("items", []))
    r2 = S.req(WEB, "POST", "/api/file-management/file/with-bytes", token=admin,
               data=json.dumps({"fileContainerName": "admin", "fileType": 2,
                                "generateUniqueFileName": False, "fileName": "ok-wb.png",
                                "content": b64png}).encode(),
               headers={"Content-Type": "application/json"})
    legit_ok = r2.status in (200, 201)
    if legit_ok:
        lst2 = S.req(WEB, "GET", "/api/file-management/file?FileContainerName=admin&MaxResultCount=100",
                     token=admin).json
        fid = next((x["id"] for x in lst2.get("items", []) if x.get("fileName") == "ok-wb.png"), None)
        if fid:
            S.req(WEB, "DELETE", f"/api/file-management/file/{fid}", token=admin)
    probe("p36_withbytes_json_mz_guarded", ok and no_store and legit_ok,
          f"MZ-jpg={r.status}/{S.error_code(r) or r.text()[:40]} 落库={not no_store} 合法PNG={r2.status}（须 400/无落库/200）")


def p37_agent_run_failclosed():
    """修复回归：切片操作员即使被授予 Reconcile.Run，POST run 也必须 fail-closed 拒绝；
    无切片 admin 照常可读列表。"""
    tree = S.api("GET", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
                 token=admin).json
    run_perm = None
    for g_ in tree.get("groups", []):
        for p_ in g_.get("permissions", []):
            if p_["name"].endswith("Reconcile.Run"):
                run_perm = p_["name"]
    if not run_perm:
        probe("p37_agent_run_failclosed", False, "未找到 Reconcile.Run 权限名")
        return
    S.api("PUT", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
          token=admin, json_body={"permissions": [{"name": run_perm, "isGranted": True}]})
    r = S.req(WEB, "POST", "/api/app/fin-reconcile/run", token=agent, json_body={
        "includeDemo": False, "runType": "sectest-probe",
        "rangeStart": "2026-01-01T00:00:00Z", "rangeEnd": "2026-12-31T00:00:00Z"})
    denied = r.status in (401, 403, 302)
    S.api("PUT", "/api/permission-management/permissions?providerName=R&providerKey=sectest-agent",
          token=admin, json_body={"permissions": [{"name": run_perm, "isGranted": False}]})
    probe("p37_agent_run_failclosed", denied,
          f"agent+Run POST run → {r.status}（须 403/302：Run 面切片 fail-closed），已撤销授权")


# ---------------------------------------------------------------- runner
ALL = [p01_withbytes_json_mz, p03_many_withstream, p04_many_form, p05_case_path,
       p06_percent_path, p07_double_slash, p08_trailing_slash,
       p09_impersonate_deactivated, p10_impersonate_locked_host_direct, p11_impersonate_restored_ok,
       p12_cross_no_tenant_header, p13_tenant_admin_to_host_user, p14_cross_cookie_tenant_host,
       p15_cross_cookie_tenant_web, p16_host_to_tenant_still_works,
       p17_22_prepayment_all, p23_prepayment_host_admin_control,
       p24_prepayment_transaction_read, p25_prepayment_withdrawal_request_read,
       p26_agent_reconcile_host, p27_agent_reconcile_run_granted, p29_admin_reconcile_ok,
       p30_findemo_host_direct, p31_findemo_host_admin,
       p32_email_cross, p33_sms_cross, p34_mixed_delivery, p35_host_to_tenant_abp_change,
       p36_withbytes_json_mz_guarded, p37_agent_run_failclosed]

if __name__ == "__main__":
    for t in ALL:
        try:
            t()
        except Exception as e:
            probe(t.__name__, False, f"EXC {type(e).__name__}: {e}")
    fails = [r for r in ROWS if r[1] is False]
    unknown = [r for r in ROWS if r[1] is None]
    print(f"\n[probes] {len(ROWS) - len(fails) - len(unknown)}/{len(ROWS)} PASS，"
          f"{len(fails)} FIND（潜在缺口），{len(unknown)} 观察项")
    with open("sectest/REPORT-probes-extra.md", "w", encoding="utf-8") as f:
        f.write("# sectest 加压轮：变体攻击面探测报告\n\n")
        for n, ok, d in ROWS:
            mark = "✅" if ok is True else ("❌" if ok is False else "ℹ️")
            f.write(f"- {mark} **{n}** — {d}\n")
    sys.exit(1 if fails else 0)
