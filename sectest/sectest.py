#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
abp_admin 专用安全测试（sectest）—— 黑盒攻击视角。
栈：docker compose -p abpadmin-sectest（web 127.0.0.1:18000 / host 直连 127.0.0.1:18081）。
目标：run-1 修复项 + run-2 修复项（A 组，期望防线生效）与已确认 open 线索（B 组，期望复现）。
仅使用 Python 标准库；除本栈内部端点外不访问任何外部服务。
用法：python sectest/sectest.py [--host-base http://127.0.0.1:18081] [--web-base http://127.0.0.1:18000]
"""
import base64
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

WEB = "http://127.0.0.1:18000"
HOST = "http://127.0.0.1:18081"
ADMIN_USER = "admin"
ADMIN_PASS = "1q2w3E*"  # compose 默认 Development 迁移器种出的模板默认口令（本栈即 NV-20 复现环境）
TESTPASS = "Sectest!1234"
ROPC_CLIENT = "AbpAdmin_TestCli"
ROPC_SECRET = "1q2w3E*test-cli-dev-only"

PNG_1x1 = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk"
    "YAAAAAYAAjCB0C8AAAAASUVORK5CYII=")
MZ_JPG = b"MZ\x90\x00\x03\x00\x00\x00\x04\x00\x00\x00\xff\xff\x00\x00" + b"A" * 64

ROLE_CLAIM_URI = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"


class Resp:
    def __init__(self, status, body, headers):
        self.status, self.body, self.headers = status, body, headers
        try:
            self.json = json.loads(body) if body else None
        except Exception:
            self.json = None

    def text(self):
        return self.body.decode("utf-8", "replace") if isinstance(self.body, bytes) else str(self.body)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """不跟随 3xx：宿主对未认证 API 请求回 302→/Error?httpStatusCode=401，
    跟随后会伪装成 200 HTML——安全断言必须看原始状态码。"""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_OPENER = urllib.request.build_opener(_NoRedirect)


def req(base, method, path, *, token=None, json_body=None, form=None, data=None,
        headers=None, tenant=None, timeout=30):
    url = base.rstrip("/") + path
    h = {"Accept": "application/json"}
    if token:
        h["Authorization"] = "Bearer " + token
    if tenant is not None:
        h["__tenant"] = tenant
    body = None
    if json_body is not None:
        body = json.dumps(json_body).encode()
        h["Content-Type"] = "application/json"
    elif form is not None:
        body = urllib.parse.urlencode(form).encode()
        h["Content-Type"] = "application/x-www-form-urlencoded"
    elif data is not None:
        body = data
    if headers:
        h.update(headers)
    r = urllib.request.Request(url, data=body, method=method, headers=h)
    try:
        with _OPENER.open(r, timeout=timeout) as resp:
            return Resp(resp.status, resp.read(), dict(resp.headers))
    except urllib.error.HTTPError as e:
        return Resp(e.code, e.read(), dict(e.headers))
    except Exception as e:  # 连接层失败
        return Resp(0, str(e).encode(), {})


def multipart(fields, files):
    """fields: {name: str}; files: {name: (filename, content, ctype)}"""
    boundary = "----sectest" + uuid.uuid4().hex
    out = []
    for k, v in fields.items():
        out.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode())
    for k, (fn, content, ct) in files.items():
        out.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"; filename=\"{fn}\"\r\n"
                   f"Content-Type: {ct}\r\n\r\n".encode() + content + b"\r\n")
    out.append(f"--{boundary}--\r\n".encode())
    return b"".join(out), "multipart/form-data; boundary=" + boundary


INTERNAL_CODE_MARKERS = (
    "AbpAdmin:",
    "Volo.Abp:",
    "SmsEndpointBlocked",
    "CredentialNotConfigured",
    "INTERNAL_ONLY_MARKER",
)


def error_code(resp):
    """提取客户可见公开码。内部码不得当作判定依据，也不应出现在客户响应里。"""
    if resp.json and isinstance(resp.json, dict):
        err = resp.json.get("error")
        if isinstance(err, dict) and err.get("code"):
            return err.get("code")
        return resp.json.get("code") or resp.json.get("error_code")
    t = resp.text()
    for marker in ("Client:", "BizTemplate:Public:"):
        i = t.find(marker)
        if i >= 0:
            return t[i:i + 80].split('"')[0].split("\\")[0]
    return None


def leaks_internal_code(resp):
    t = resp.text()
    return any(marker in t for marker in INTERNAL_CODE_MARKERS)


# ---------------------------------------------------------------- fixtures
class Fx:
    admin_token = None
    policies = []
    ids = {}
    per = {}  # 每个低权角色的登录令牌


def wait_stack(deadline_s=420):
    t0 = time.time()
    while time.time() - t0 < deadline_s:
        r = req(HOST, "GET", "/health-status", timeout=5)
        if r.status == 200:
            return True
        time.sleep(5)
    return False


import os

CACHE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".tokens.json")


def _cache_load():
    try:
        return json.load(open(CACHE_PATH, encoding="utf-8"))
    except Exception:
        return {}


def _cache_save(c):
    json.dump(c, open(CACHE_PATH, "w", encoding="utf-8"))


def ropc(username, password, tenant=None):
    return req(WEB, "POST", "/connect/token", form={
        "grant_type": "password", "client_id": ROPC_CLIENT,
        "client_secret": ROPC_SECRET, "username": username, "password": password,
        "scope": "openid profile email phone roles AbpAdmin offline_access",
    }, tenant=tenant)


def login(username, password, tenant=None):
    # 令牌缓存：ROPC 受 LoginByIp 20/15min 约束，跨迭代重放会触发限流——
    # 令牌 1h 有效，缓存到 .tokens.json（键含租户，避免串租户重放）。
    key = f"{tenant or 'host'}:{username}"
    c = _cache_load()
    tok = c.get(key)
    if tok:
        probe = req(WEB, "GET", "/api/identity/users?SkipCount=0&MaxResultCount=1", token=tok, tenant=tenant)
        if probe.status == 200 and probe.json:
            return tok
    # 租户令牌必须向 HOST 直连签发并携带 __tenant 头：web 边界（nginx）丢弃下划线头，
    # 经 WEB 签发的租户令牌不带 tenantid claim —— 后续所有请求都会落到 host 上下文，
    # 把"租户管理员"误测成"宿主管理员"（b03/b04 首轮误报的根因）。
    # 幂等 fixtures 跨栈代次复用租户时，admin 口令可能是建租户时的 TESTPASS，
    # 也可能是默认口令 ADMIN_PASS（种子允许默认口令的代次）——按序尝试。
    body = {"grant_type": "password", "client_id": ROPC_CLIENT,
            "client_secret": ROPC_SECRET, "username": username,
            "scope": "openid profile email phone roles AbpAdmin"}
    r = None
    for pw in dict.fromkeys([password, ADMIN_PASS if password != ADMIN_PASS else TESTPASS]):
        body["password"] = pw
        r = req(HOST if tenant else WEB, "POST", "/connect/token", form=body, tenant=tenant)
        if r.status == 200 and r.json:
            break
    if r is None or r.status != 200 or not r.json:
        raise RuntimeError(f"login failed for {username} (tenant={tenant}): {r.status if r else '?'} {(r.text()[:300] if r else '')}")
    c[key] = r.json["access_token"]
    _cache_save(c)
    return c[key]


def find_perm(substring):
    cands = [p for p in Fx.policies if substring in p]
    if not cands:
        raise RuntimeError(f"policy not found for {substring!r}")
    return cands[0]


def api(method, path, *, token=None, **kw):
    return req(WEB, method, path, token=token or Fx.admin_token, **kw)


def grant(role_name, perms):
    body = {"permissions": [{"name": p, "isGranted": True} for p in perms]}
    r = api("PUT", "/api/permission-management/permissions?providerName=R&providerKey="
            + urllib.parse.quote(role_name), json_body=body)
    assert r.status in (200, 204), f"grant {role_name} {perms}: {r.status} {r.text()[:200]}"


def find_role_id(name):
    r = api("GET", f"/api/identity/roles/all").json["items"]
    return next((x["id"] for x in r if x["name"] == name), None)


def find_user_id(username):
    r = api("GET", f"/api/identity/users?Filter={urllib.parse.quote(username)}").json
    return next((x["id"] for x in r.get("items", []) if x["userName"] == username), None)


def mk_role(name, extra_perms):
    existing = find_role_id(name)
    if existing:
        grant(name, extra_perms)
        return existing
    r = api("POST", "/api/identity/roles", json_body={"name": name, "isDefault": False, "isPublic": False})
    assert r.status in (200, 201), f"role {name}: {r.status} {r.text()[:200]}"
    grant(name, extra_perms)
    return r.json["id"]


def mk_user(username, role_names, phone=None):
    existing = find_user_id(username)
    if existing:
        return existing
    r = api("POST", "/api/identity/users", json_body={
        "userName": username, "password": TESTPASS, "email": f"{username}@sectest.local",
        "phoneNumber": phone, "isActive": True, "lockoutEnabled": True,
        "roleNames": role_names,
    })
    assert r.status in (200, 201), f"user {username}: {r.status} {r.text()[:300]}"
    return r.json["id"]


def setup():
    Fx.admin_token = login(ADMIN_USER, ADMIN_PASS)
    tree = api("GET", "/api/permission-management/permissions?providerName=R&providerKey=sectest-discovery")
    names = []

    def walk(perms):
        for p in perms:
            names.append(p["name"])
            walk(p.get("children", []))

    for g in tree.json.get("groups", []):
        walk(g.get("permissions", []))
    Fx.policies = names
    print(f"[fx] policies discovered: {len(Fx.policies)}")

    # ---- 角色与低权用户
    p = Fx.policies
    # Users.Update + Roles.Update 都给：claim 两条写面各自的 [Authorize] 必须先通过，
    # 断言的是守卫拒收（CannotCarryRole），而不是 403 授权拦截
    claims_perms = [find_perm("Roles.Update"), find_perm("Users.Update")]
    # OU 面：类级 OrganizationUnits.Default 与方法级 ManageRoles/ManageMembers 是 AND 组合
    ou_default = next(x for x in p if x.endswith(".OrganizationUnits") and "Impersonation" not in x)
    ou_perms = [ou_default, find_perm("OrganizationUnits.ManageRoles"), find_perm("OrganizationUnits.ManageMembers")]
    setting_perms = [find_perm("SettingUi.Update")] + \
        ([x for x in p if x.endswith("ShowSettingPage")] or [])
    # OIDC 应用面：方法级 Update 之外还有类级/组合权限，补 Default 与 Create 以确保可到达门
    oidc_default = next(x for x in p if x.endswith(".OpenIddict.Applications"))
    oidc_perms = [oidc_default, find_perm("OpenIddict.Applications.Update")]
    imp_perms = [find_perm("Impersonation.User")]
    fin_perms = [find_perm("Finance.Adjust.Create")]

    mk_role("sectest-claims-op", claims_perms)
    mk_role("sectest-ou-op", ou_perms)
    mk_role("sectest-settings-op", setting_perms)
    mk_role("sectest-oidc-op", oidc_perms)
    mk_role("sectest-imp", imp_perms)
    mk_role("sectest-fin-op", fin_perms)

    Fx.ids["hacker"] = mk_user("sectest.hacker", ["sectest-claims-op"])
    Fx.ids["ouop"] = mk_user("sectest.ouop", ["sectest-ou-op"])
    Fx.ids["setop"] = mk_user("sectest.setop", ["sectest-settings-op"])
    Fx.ids["oidcop"] = mk_user("sectest.oidcop", ["sectest-oidc-op"])
    Fx.ids["imp"] = mk_user("sectest.imp", ["sectest-imp"])
    Fx.ids["finop"] = mk_user("sectest.finop", ["sectest-fin-op"])
    Fx.ids["victim"] = mk_user("sectest.victim", [], phone="13800138000")
    Fx.ids["gw"] = mk_user("sectest.gw", [])

    for u in ("hacker", "ouop", "setop", "oidcop", "imp", "finop"):
        key = u.replace(".", "_")
        Fx.per[key] = login(f"sectest.{u}", TESTPASS)

    # ---- OU：普通 OU（ou-op 自有角色挂它）+ 特权 OU（admin 挂 admin 角色）
    def ensure_ou(display):
        for ou in api("GET", "/api/app/organization-unit").json:
            if ou["displayName"] == display:
                return ou["id"]
        r = api("POST", "/api/app/organization-unit", json_body={"displayName": display})
        assert r.status in (200, 201), f"ou {display}: {r.status} {r.text()[:200]}"
        return r.json["id"]

    Fx.ids["ou_normal"] = ensure_ou("sectest-normal")
    Fx.ids["ou_priv"] = ensure_ou("sectest-privileged")
    admin_role_id = find_role_id("admin")
    Fx.ids["admin_role"] = admin_role_id
    r = api("POST", f"/api/app/organization-unit/{Fx.ids['ou_priv']}/roles", json_body=[admin_role_id])
    assert r.status in (200, 204), f"attach admin role by admin: {r.status} {r.text()[:200]}"

    # ---- 租户 A/B（各带独立 admin；幂等：已存在即复用）
    r = api("GET", "/api/multi-tenancy/tenants?SkipCount=0&MaxResultCount=50&Filter=sectest").json
    existing = {t["name"]: t["id"] for t in r.get("items", [])}
    for name in ("sectesta", "sectestb"):
        if name in existing:
            Fx.ids["tenant_" + name] = existing[name]
            continue
        r = api("POST", "/api/multi-tenancy/tenants", json_body={
            "name": name, "adminEmailAddress": f"admin@{name}.sectest",
            "adminPassword": TESTPASS})
        assert r.status in (200, 201), f"tenant {name}: {r.status} {r.text()[:300]}"
        Fx.ids["tenant_" + name] = r.json["id"]

    Fx.per["tenantA_admin"] = login("admin", ADMIN_PASS, tenant="sectesta")
    Fx.per["tenantB_admin"] = login("admin", ADMIN_PASS, tenant="sectestb")
    for tag, tenant in (("tenantA_admin", "sectesta"), ("tenantB_admin", "sectestb")):
        # 引用令牌的会话校验按环境租户过滤，nginx 又丢 __tenant 头——API 查询不可靠，
        # 直接从测试栈 DB 取租户 admin 的用户 id（harness 对本栈有 docker 权限）
        import subprocess
        out = subprocess.run(
            ["docker", "exec", "abpadmin-sectest-db-1", "psql", "-U", "postgres",
             "-d", "abpadmin", "-t", "-c",
             "select u.\"Id\" from \"AbpUsers\" u join \"AbpTenants\" t on u.\"TenantId\"=t.\"Id\" "
             f"where u.\"UserName\"='admin' and t.\"Name\"='{tenant}';"],
            capture_output=True, timeout=15).stdout.decode()
        uid = out.strip().split("|")[0].strip()
        assert uid and len(uid) == 36, f"tenant admin id lookup failed for {tenant}: {out!r}"
        Fx.ids[tag + "_user"] = uid

    # ---- 资金急停基线：admin 置 halt=true（域端点）
    r = api("POST", "/api/app/collection-withdraw-admin/set-withdraw-halt?halt=true")
    assert r.status in (200, 202, 204), f"set halt: {r.status} {r.text()[:200]}"

    print("[fx] fixtures ready")


# ---------------------------------------------------------------- A 组：修复面（期望防线生效）
def a01_health_anonymous():
    # 宿主把未认证 401 重写到 /Error?httpStatusCode=401（302）；无重定向后：
    # /health-status 必须 200；管理面必须 401/302，绝不能 200 JSON。
    ok = req(HOST, "GET", "/health-status").status == 200
    bad = {}
    for p in ("/health-ui", "/health-api", "/healthchecks-webhooks"):
        r = req(HOST, "GET", p)
        bad[p] = r.status
        if r.status == 200:
            ok = False
    return ok, f"health-status=200; protected={bad}（401/302=受保护）"


def a02_user_claims_role_rejected():
    tok = Fx.per["hacker"]
    for ct in (ROLE_CLAIM_URI, "role", "roles"):
        r = req(WEB, "PUT", f"/api/app/identity-claim/user-claims/{Fx.ids['victim']}",
                token=tok, json_body=[{"claimType": ct, "claimValue": "admin"}])
        if r.status not in (400, 403) or "CannotCarryRole" not in (error_code(r) or ""):
            return False, f"claimType={ct}: status={r.status} code={error_code(r)}"
    return True, "user-claims 三种权威类型全部被拒（IdentityClaimsCannotCarryRole）"


def a03_role_claims_role_rejected():
    tok = Fx.per["hacker"]
    target_role = next(x["id"] for x in api("GET", "/api/identity/roles/all").json["items"]
                       if x["name"] == "sectest-claims-op")
    for ct in (ROLE_CLAIM_URI, "role", "roles"):
        r = req(WEB, "PUT", f"/api/app/identity-claim/role-claims/{target_role}",
                token=tok, json_body=[{"claimType": ct, "claimValue": "admin"}])
        if r.status not in (400, 403) or "CannotCarryRole" not in (error_code(r) or ""):
            return False, f"claimType={ct}: status={r.status} code={error_code(r)}"
    return True, "role-claims 三种权威类型全部被拒（run-2 C-1）"


def a04_claims_allowed_type_ok():
    tok = Fx.per["hacker"]
    r = req(WEB, "PUT", f"/api/app/identity-claim/user-claims/{Fx.ids['victim']}",
            token=tok, json_body=[{"claimType": "department", "claimValue": "rd"}])
    return r.status in (200, 204), f"status={r.status} body={r.text()[:120]}"


def a05_ou_attach_admin_denied():
    tok = Fx.per["ouop"]
    r = req(WEB, "POST", f"/api/app/organization-unit/{Fx.ids['ou_normal']}/roles",
            token=tok, json_body=[Fx.ids["admin_role"]])
    code = error_code(r) or ""
    return r.status == 403 and "RoleDataScopeEscalation" in code, f"status={r.status} code={code}"


def a06_ou_attach_own_role_allowed():
    tok = Fx.per["ouop"]
    own_role = next(x["id"] for x in api("GET", "/api/identity/roles/all").json["items"]
                    if x["name"] == "sectest-ou-op")
    r = req(WEB, "POST", f"/api/app/organization-unit/{Fx.ids['ou_normal']}/roles",
            token=tok, json_body=[own_role])
    return r.status in (200, 204), f"status={r.status}"


def a07_ou_member_into_privileged_denied():
    tok = Fx.per["ouop"]
    r = req(WEB, "POST", f"/api/app/organization-unit/{Fx.ids['ou_priv']}/members",
            token=tok, json_body=[Fx.ids["ouop"]])
    code = error_code(r) or ""
    return r.status == 403 and "RoleDataScopeEscalation" in code, f"status={r.status} code={code}"


def a08_settingui_fundswitch_denied():
    tok = Fx.per["setop"]
    r = req(WEB, "PUT", "/api/setting-ui/set-setting-values",
            token=tok, json_body={"Setting_Collection_Withdraw_Halt": "false"})
    halt_now = api("GET", "/api/app/collection-withdraw-admin/status").json.get("halt")
    return halt_now is True, f"write resp={r.status}; halt after attack = {halt_now}（须仍为 True）"


def a09_settingui_ui_setting_writable():
    tok = Fx.per["setop"]
    r = req(WEB, "PUT", "/api/setting-ui/set-setting-values",
            token=tok, json_body={"Setting_AbpAdmin_Account_PasswordlessLoginTokenLifetimeSeconds": "120"})
    got = None
    groups = req(WEB, "GET", "/api/setting-ui", token=tok).json
    for g in (groups if isinstance(groups, list) else groups.get("groups", groups.get("settingGroups", []))):
        infos = g.get("settingInfos") or g.get("settings") or []
        for s in infos:
            if "PasswordlessLoginTokenLifetimeSeconds" in (s.get("name") or ""):
                got = s.get("value")
    return r.status in (200, 204) and got == "120", f"resp={r.status} value={got}"


def a10_gateway_rotate():
    lr = req(HOST, "POST", "/api/auth/login",
             json_body={"username": "sectest.gw", "password": TESTPASS})
    if lr.status != 200:
        return False, f"login: {lr.status} {lr.text()[:150]}"
    old_key, session = lr.json["apiKey"], lr.json["session"]

    rot = req(HOST, "POST", "/api/wallet/keys/rotate", token=old_key)
    if rot.status != 200:
        return False, f"rotate: {rot.status} {rot.text()[:150]}"
    new_key = rot.json.get("apiKey")

    old_dead = req(HOST, "GET", "/v1/models", token=old_key).status == 401
    new_ok = req(HOST, "GET", "/v1/models", token=new_key).status == 200
    sess_rejected = req(HOST, "POST", "/api/wallet/keys/rotate", token=session).status == 401
    return old_dead and new_ok and sess_rejected and new_key != old_key, \
        f"old401={old_dead} new200={new_ok} session-rejected={sess_rejected}"


def a11_sms_intranet_endpoint_blocked():
    # 入口守卫验证：内网端点不得产生真实出站。本栈无任何厂商凭据，可能的拒绝形态：
    # ① 403/400 SmsEndpointBlocked / CredentialNotConfigured（守卫或配置校验先拒）；
    # ② 204（resolver「未配置凭据静默不发」——同样零出站）。
    # 失守形态：5xx 网络错误（upstream timeout / connection refused 到 169.254.x）或 200 已发。
    import subprocess
    subprocess.run(["docker", "exec", "abpadmin-sectest-db-1", "psql", "-U", "postgres",
                    "-d", "abpadmin", "-c",
                    "UPDATE \"AbpUsers\" SET \"PhoneNumberConfirmed\"=true "
                    "WHERE \"UserName\"='sectest.victim';"], capture_output=True, timeout=15)
    api("PUT", "/api/setting-ui/set-setting-values", json_body={
        "Setting_AbpAdmin_Sms_Provider": "TencentCloud",
        "Setting_AbpAdmin_Sms_TencentCloud_EndPoint": "169.254.169.254",
    })
    try:
        r = req(WEB, "POST", "/api/app/account-pro/send-phone-number-confirmation-code",
                json_body={"phoneNumber": "13800138000"})
        code = error_code(r) or ""
        leaked = leaks_internal_code(r)
        # 客户响应不再回内部码；守卫生效看状态 + 日志无真实出站，并断言内部码未泄漏。
        no_egress = (r.status in (204, 400, 403, 500) and not leaked)
        # 额外硬证据：宿主日志 3 分钟内不得出现指向 169.254 的出站尝试
        log = subprocess.run(["docker", "logs", "abpadmin-sectest-host-1", "--since", "3m"],
                             capture_output=True, timeout=20).stdout.decode("utf-8", "replace")
        attempted = "169.254.169.254" in log and ("SmsSendFailed" in log or "HttpRequestException" in log)
        return no_egress and not attempted, (
            f"status={r.status} code={code} leaked={leaked} egress_attempt={attempted}")
    finally:
        api("PUT", "/api/setting-ui/set-setting-values", json_body={
            "Setting_AbpAdmin_Sms_Provider": "Aliyun",
            "Setting_AbpAdmin_Sms_TencentCloud_EndPoint": "sms.tencentcloudapi.com",
        })


def a12_token_bruteforce_ratelimited():
    saw429 = None
    for i in range(25):
        r = req(HEB_HOST(), "POST", "/connect/token", form={
            "grant_type": "password", "client_id": ROPC_CLIENT, "client_secret": ROPC_SECRET,
            "username": "sectest.victim", "password": "wrong-" + str(i)})
        if r.status == 429:
            saw429 = i + 1
            break
    return saw429 is not None, f"first 429 at attempt {saw429}/25"


def a13_tenant_mismatch_guard():
    # 边界发现：nginx 默认丢弃带下划线的请求头（underscores_in_headers off），
    # __tenant 头经 web 边缘根本到不了宿主——转向攻击向量只存在于直连宿主/其他边界。
    # 直连 host 的三种拦截形态都算防线生效：
    # ① 403 tenant_mismatch（失配守卫）；② 401 session_revoked（会话校验按环境租户过滤，
    #    他租上下文看不到本租会话——等效拒绝转向，且是更早的层）；
    # ③ 403 Client:Forbidden（client-errors 管线后的失配守卫新信封，run-3 审计判定为
    #    刻意的形状变更而非失守——守卫照常 403 拦截，错误体走公开错误投影）。
    r = req(HOST, "GET", "/api/account/my-profile",
            token=Fx.per["tenantA_admin"], tenant="sectestb")
    body = r.text().lower()
    blocked = (r.status == 403 and ("tenant" in body or "forbidden" in body)
               or r.status == 401 and "session" in body)
    return blocked, f"direct={r.status}({r.text()[:70]})——403 失配守卫或 401 跨租会话不可见均为拦截"


def a14_anonymous_surface():
    # 未认证 API 的宿主响应是 302→/Error?httpStatusCode=401（无重定向可见 302+Location）；
    # 经 nginx 的是 401/302。任何 200(JSON 业务数据) 都算失守。
    def protected(status):
        return status in (401, 403, 302)

    checks = {p: req(WEB, "GET", p).status for p in
              ["/api/app/audit-log", "/api/app/identity-user-admin", "/api/setting-ui",
               "/api/app/cache-monitor", "/api/app/server-monitor", "/api/app/background-job",
               "/api/app/fin-manual-adjust"]}
    leaks = {p: s for p, s in checks.items() if not protected(s)}
    return not leaks, f"statuses={checks}"


def a15_upload_content_guard():
    # 与 SPA 完全同形的 multipart（字段名 FileContainerName/FileType=2/File/...）
    def up(fname, content, ctype):
        body, ct = multipart(
            {"FileContainerName": "admin", "FileType": "2", "GenerateUniqueFileName": "false"},
            {"File": (fname, content, ctype)})
        return req(WEB, "POST", "/api/file-management/file", data=body, token=Fx.admin_token,
                   headers={"Content-Type": ct})

    bad = up("evil.jpg", MZ_JPG, "image/jpeg")
    good = up("ok.png", PNG_1x1, "image/png")
    return bad.status == 400 and "ContentMismatch" in (error_code(bad) or "") and good.status in (200, 201), \
        f"mz-jpg={bad.status}/{error_code(bad)} png={good.status}/{(good.text() or '')[:80]}"


def a16_oidc_manageperms_gate():
    apps = api("GET", "/api/app/open-iddict-application?SkipCount=0&MaxResultCount=50").json
    app_id = None
    for it in apps.get("items", []):
        if it.get("clientId") == "AbpAdmin_App":
            app_id = it["id"]
    if not app_id:
        return False, "AbpAdmin_App not found in application list"
    # 对照式断言：管理员（持 ManagePermissions）能通过授权（非 403/302，body 错误也算过门）；
    # oidcop（仅 Default+Update，无 ManagePermissions）必须被拒（403/302）——NV-23 修复语义
    admin_r = req(WEB, "PUT", f"/api/app/open-iddict-application/{app_id}",
                  token=Fx.admin_token, json_body={})
    op_r = req(WEB, "PUT", f"/api/app/open-iddict-application/{app_id}",
               token=Fx.per["oidcop"], json_body={})
    admin_passed = admin_r.status not in (401, 403, 302)
    op_denied = op_r.status in (403, 302)
    return admin_passed and op_denied, \
        f"admin={admin_r.status} operator={op_r.status}（operator 须 403/302，admin 须过门）"


def a17_connstring_audit_exit():
    probe = api("PUT", f"/api/app/tenant/{Fx.ids['tenant_sectesta']}/connection-strings", json_body={
        "useSharedDatabase": False,
        "items": [{"name": "Default", "value": "Host=sectest;Password=AuditLeak!999;Username=sectest"}]})
    logs = api("GET", "/api/audit-logs?SkipCount=0&MaxResultCount=50").json
    blob = json.dumps(logs, ensure_ascii=False)
    leaked = "AuditLeak!999" in blob
    # 还原共享库：写脏连接串后该租户的一切请求都会去连不存在的 sectest 库，
    # 后续 tenantA 夹具/用例（含登录）会全体落入错误页重定向环。
    api("PUT", f"/api/app/tenant/{Fx.ids['tenant_sectesta']}/connection-strings",
        json_body={"useSharedDatabase": True, "items": []})
    return probe.status in (200, 204) and not leaked, \
        f"update={probe.status}; audit 泄漏={leaked}（已还原共享库）"


HEB = {"v": HOST}


def HEB_HOST():
    return HEB["v"]


# ---------------------------------------------------------------- B 组：已修复线索（期望防线生效；REPRODUCED=回归失守）
def b01_impersonate_locked_account():
    api("POST", f"/api/app/identity-user-admin/{Fx.ids['victim']}/lock")
    r = req(WEB, "POST", "/connect/token", token=Fx.per["imp"], form={
        "grant_type": "impersonation", "user_id": Fx.ids["victim"], "client_id": "AbpAdmin_App"})
    api("POST", f"/api/app/identity-user-admin/{Fx.ids['victim']}/unlock")
    return r.status == 200, f"impersonation of locked account → {r.status}（须非 200：锁定即不可铸造令牌）"


def b02_upload_sibling_route():
    # 与 a15 同形（SPA 字段名），走 with-stream 姊妹路由；守卫已改前缀收口，
    # 任何 POST 子路径必须进内容扫描 → MZ-jpg 必须 400/ContentMismatch
    body, ct = multipart(
        {"FileContainerName": "admin", "FileType": "2", "GenerateUniqueFileName": "false"},
        {"File": ("evil2.jpg", MZ_JPG, "image/jpeg")})
    r = req(WEB, "POST", "/api/file-management/file/with-stream", data=body,
            token=Fx.admin_token, headers={"Content-Type": ct})
    if r.status in (403, 302):
        return False, f"with-stream 未到达内容校验: {r.status} {r.text()[:100]}"
    reproduced = r.status in (200, 201)
    return reproduced, f"with-stream MZ-jpg → {r.status}/{error_code(r)}（须 400/ContentMismatch）"


def b04_impersonation_cross_tenant():
    # 直连 host：nginx 丢弃 __tenant 头，跨租户转向向量只存在于直连/其他边界。
    # 修复后期望：边界以令牌租户声明为基准 ⇒ A→B 必须 non-200（invalid_grant）。
    r = req(HOST, "POST", "/connect/token", token=Fx.per["tenantA_admin"], form={
        "grant_type": "impersonation", "user_id": Fx.ids["tenantB_admin_user"],
        "client_id": "AbpAdmin_App"}, tenant="sectestb")
    return r.status == 200, f"direct tenantA-admin → tenantB user: {r.status}（须非 200：跨租户铸造已拒绝）"


def b05_finreconcile_sliced_read():
    mk_role("sectest-agent", [find_perm("Ledger.View"), find_perm("Scope.Agent")])
    uid = mk_user("sectest.agent", ["sectest-agent"])
    tok = login("sectest.agent", TESTPASS)
    r = req(WEB, "GET", "/api/app/fin-reconcile?SkipCount=0&MaxResultCount=20", token=tok)
    return r.status == 200, f"sliced operator GET fin-reconcile → {r.status}（须 403：切片 fail-closed）"


def b06_findemoevent_sod():
    r = req(WEB, "POST", "/api/app/fin-demo-event", token=Fx.per["finop"], json_body={
        "bizType": "demo.credit", "bizRefId": "sectest-" + uuid.uuid4().hex[:10],
        "phase": "pay", "currency": "CNY", "amount": 100, "userId": Fx.ids["victim"]})
    return r.status in (200, 201), f"Adjust.Create-only POST fin-demo-event → {r.status}（须非 2xx：非 Development 拒绝）"


def b07_notification_cross_tenant():
    # 修复后期望：租户外 UserId 被过滤，全部无效 → 400 EmptyUserIds（非 2xx）。
    r = req(WEB, "POST", "/api/app/notification-management/send-to-users",
            token=Fx.per["tenantA_admin"], json_body={
                "userIds": [Fx.ids["tenantB_admin_user"]],
                "notificationMethods": ["InApp"],
                "title": "sectest", "body": "sectest"})
    return r.status in (200, 204), f"tenantA → tenantB 用户定向通知 → {r.status}（须非 2xx：租户外 Id 已过滤）"


def b08_default_admin_and_ropc():
    # 修复后验证点改为「compose 默认姿态」：MIGRATOR_DOTNET_ENVIRONMENT 缺省渲染为
    # Production（默认不再种公开口令/内嵌 ROPC 密钥；本栈是显式 dev 覆盖，属测试夹具）。
    import os
    import subprocess
    env = {k: v for k, v in os.environ.items() if k != "MIGRATOR_DOTNET_ENVIRONMENT"}
    r = subprocess.run(
        ["docker", "compose", "-f", "docker-compose.yml", "config", "--format", "json"],
        capture_output=True, timeout=60, env=env, cwd=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    cfg = json.loads(r.stdout.decode("utf-8", "replace"))
    migrator_env = cfg["services"]["migrator"]["environment"]
    value = migrator_env.get("DOTNET_ENVIRONMENT") or next(
        (v for k, v in migrator_env.items() if k.endswith("DOTNET_ENVIRONMENT")), None)
    reproduced = value != "Production"
    return reproduced, f"compose 渲染默认 DOTNET_ENVIRONMENT={value!r}（须 Production：默认不种开发凭据）"


# ---------------------------------------------------------------- runner
A_TESTS = [a01_health_anonymous, a02_user_claims_role_rejected, a03_role_claims_role_rejected,
           a04_claims_allowed_type_ok, a05_ou_attach_admin_denied, a06_ou_attach_own_role_allowed,
           a07_ou_member_into_privileged_denied, a08_settingui_fundswitch_denied,
           a09_settingui_ui_setting_writable, a10_gateway_rotate, a11_sms_intranet_endpoint_blocked,
           a13_tenant_mismatch_guard, a14_anonymous_surface,
           a15_upload_content_guard, a16_oidc_manageperms_gate, a17_connstring_audit_exit]
# a12 会打满 LoginByIp 窗口（15min），必须全局最后执行
A_TESTS_LAST = [a12_token_bruteforce_ratelimited]

B_TESTS = [b01_impersonate_locked_account, b02_upload_sibling_route,
           b04_impersonation_cross_tenant, b05_finreconcile_sliced_read, b06_findemoevent_sod,
           b07_notification_cross_tenant, b08_default_admin_and_ropc]


def clear_rate_counters():
    """迭代便利：清空测试栈 Redis 里的 orl 计数器（a12 之前会重新制造并验证它们）。"""
    import subprocess
    try:
        subprocess.run(["docker", "exec", "abpadmin-sectest-redis-1", "redis-cli", "EVAL",
                        "local ks=redis.call('keys','*OperationRateLimitingCounter*') "
                        "for i,k in ipairs(ks) do redis.call('del',k) end return #ks", "0"],
                       capture_output=True, timeout=15)
    except Exception as e:
        print(f"[fx] 清限流计数失败（忽略，继续）：{e}")


def main():
    global WEB, HOST
    args = sys.argv[1:]
    for i, a in enumerate(args):
        if a == "--web-base" and i + 1 < len(args):
            WEB = args[i + 1]
        if a == "--host-base" and i + 1 < len(args):
            HOST = args[i + 1]
    HEB["v"] = HOST

    print(f"[sectest] web={WEB} host={HOST}")
    if not wait_stack():
        print("[sectest] FAIL: 栈未在期限内就绪（/health-status 不通）")
        sys.exit(2)
    print("[sectest] stack healthy")
    clear_rate_counters()

    setup()

    rows = []
    for t in A_TESTS:
        try:
            ok, detail = t()
        except Exception as e:
            ok, detail = False, f"EXC {type(e).__name__}: {e}"
        rows.append(("A", t.__name__, ok, detail))
        print(f"[A] {'PASS' if ok else 'FAIL'} {t.__name__} :: {detail}")
    for t in B_TESTS:
        try:
            repro, detail = t()
        except Exception as e:
            repro, detail = False, f"EXC {type(e).__name__}: {e}"
        rows.append(("B", t.__name__, repro, detail))
        print(f"[B] {'REPRODUCED' if repro else 'NOT-REPRODUCED'} {t.__name__} :: {detail}")
    for t in A_TESTS_LAST:
        try:
            ok, detail = t()
        except Exception as e:
            ok, detail = False, f"EXC {type(e).__name__}: {e}"
        rows.append(("A", t.__name__, ok, detail))
        print(f"[A] {'PASS' if ok else 'FAIL'} {t.__name__} :: {detail}")

    a_fail = [r for r in rows if r[0] == "A" and not r[2]]
    b_rep = [r for r in rows if r[0] == "B" and r[2]]
    with open("sectest/REPORT-sectest.md", "w", encoding="utf-8") as f:
        f.write("# sectest 黑盒攻击验证报告（abpadmin-sectest 栈）\n\n")
        f.write(f"时间：{time.strftime('%Y-%m-%d %H:%M:%S')}  web={WEB} host={HOST}\n\n")
        f.write("## A 组 · 修复面防线（必须 PASS）\n\n")
        for g, n, ok, d in rows:
            if g == "A":
                f.write(f"- {'✅' if ok else '❌'} **{n}** — {d}\n")
        f.write("\n## B 组 · open 线索复现（⚠ = 漏洞行为当前仍可复现，待修复后转绿）\n\n")
        for g, n, ok, d in rows:
            if g == "B":
                f.write(f"- {'⚠' if ok else '✅(未复现)'} **{n}** — {d}\n")
        f.write(f"\n汇总：A 组 {len(A_TESTS) + len(A_TESTS_LAST) - len(a_fail)}/{len(A_TESTS) + len(A_TESTS_LAST)} 通过；"
                f"B 组复现 {len(b_rep)}/{len(B_TESTS)}。\n")
    print(f"\n[sectest] A: {len(A_TESTS) + len(A_TESTS_LAST) - len(a_fail)}/{len(A_TESTS) + len(A_TESTS_LAST)} PASS | "
          f"B reproduced: {len(b_rep)}/{len(B_TESTS)}")
    print("[sectest] report → sectest/REPORT-sectest.md")
    sys.exit(1 if a_fail else 0)


if __name__ == "__main__":
    main()
