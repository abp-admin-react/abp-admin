#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OCR 修复面 + 不确定面攻击探针（黑盒，abpadmin-sectest 栈）。
覆盖：
  - S1 JSON 重复/歧义槽走私（fileName/FileName、content/Content、fileName+fileNames）
  - 导入面反提权（RoleNames / OU 挂特权角色）→ 行级拒绝
  - OU 交互面 RoleDataScopeEscalation 公开码（回归 a05/a07）
  - 公共授权码客户端 PKCE 要求（swagger 应用）
  - XFF 伪造：非 KnownProxies 来源不得改写限流/审计身份（对照直连）
  - 合法 PNG with-bytes 对照组不得被误伤
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
ROWS = []


def probe(name, ok, detail):
    ROWS.append((name, ok, detail))
    print(f"[{'PASS' if ok else 'FIND'}] {name} :: {detail}")


def setup_fx():
    """复用/补齐 fixtures：ouop + import 权限用户。"""
    S.clear_rate_counters()
    S.setup()
    # 给 ouop 叠加 Import 权限（Users.Update 已有不一定含 Import 子权限）
    import_perm = S.find_perm("Users.Import")
    ou_default = next(x for x in S.Fx.policies
                      if x.endswith(".OrganizationUnits") and "Impersonation" not in x)
    ou_perms = [ou_default, S.find_perm("OrganizationUnits.ManageRoles"),
                S.find_perm("OrganizationUnits.ManageMembers"), import_perm,
                S.find_perm("Users.Update")]
    S.mk_role("sectest-ou-op", ou_perms)
    # 专用导入攻击角色（无 admin、无 IsAll）
    S.mk_role("sectest-import-op", [import_perm, S.find_perm("Users.Update")])
    S.Fx.ids["importop"] = S.mk_user("sectest.importop", ["sectest-import-op"])
    S.clear_rate_counters()
    S.Fx.per["importop"] = S.login("sectest.importop", S.TESTPASS)
    S.Fx.per["ouop"] = S.login("sectest.ouop", S.TESTPASS)


def multipart_file(field, filename, content, ctype="text/csv"):
    boundary = "----ocr" + uuid.uuid4().hex
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="{field}"; filename="{filename}"\r\n'
        f"Content-Type: {ctype}\r\n\r\n"
    ).encode() + content + f"\r\n--{boundary}--\r\n".encode()
    return body, f"multipart/form-data; boundary={boundary}"


def csv_import_bytes(rows):
    """表头与 UserImportColumnNames 中文列同源（见 UserImportExportTests CSV 用例）。"""
    # 列序：用户名,名,姓,邮箱,手机号,密码,角色,组织单元,是否启用,是否外部用户
    lines = ["用户名,名,姓,邮箱,手机号,密码,角色,组织单元,是否启用,是否外部用户"]
    for r in rows:
        lines.append(",".join([
            r["userName"],
            r.get("name", "t"),
            r.get("surname", "t"),
            r["email"],
            r.get("phone", ""),
            r.get("password", S.TESTPASS),
            r.get("roles", ""),
            r.get("ous", ""),
            "是",
            "否",
        ]))
    return ("\r\n".join(lines)).encode("utf-8-sig")


def post_import(token, csv_bytes):
    body, ct = multipart_file("file", "attack.csv", csv_bytes)
    return S.req(WEB, "POST", "/api/app/identity-user-admin/import",
                 token=token, data=body, headers={"Content-Type": ct}, timeout=60)


def post_json_raw(path, raw_json: str, token=None):
    """故意发带重复键的原始 JSON 字符串（json.dumps 会去重）。"""
    return S.req(WEB, "POST", path, token=token,
                 data=raw_json.encode("utf-8"),
                 headers={"Content-Type": "application/json"})


# ---- S1 上传走私 ----
def t_s1_duplicate_filename_case():
    png = base64.b64encode(S.PNG_1x1).decode()
    # 合法 PNG 作首值，恶意名作末值——若只扫首值会放行
    raw = (
        '{"fileContainerName":"admin","fileType":2,"generateUniqueFileName":false,'
        f'"fileName":"a.png","FileName":"evil.svg","content":"{png}"}}'
    )
    r = post_json_raw("/api/file-management/file/with-bytes", raw, token=S.Fx.admin_token)
    ok = r.status == 400
    code = S.error_code(r) or ""
    probe("ocr_s1_dup_fileName_case", ok,
          f"status={r.status} code={code}（须 400 拒歧义槽）")


def t_s1_duplicate_content_case():
    png = base64.b64encode(S.PNG_1x1).decode()
    evil = base64.b64encode(b"<script>alert(1)</script>").decode()
    raw = (
        '{"fileContainerName":"admin","fileType":2,"generateUniqueFileName":false,'
        f'"fileName":"x.svg","content":"{png}","Content":"{evil}"}}'
    )
    r = post_json_raw("/api/file-management/file/with-bytes", raw, token=S.Fx.admin_token)
    ok = r.status == 400
    probe("ocr_s1_dup_content_case", ok, f"status={r.status} code={S.error_code(r)}")


def t_s1_cross_alias_filename_filenames():
    png = base64.b64encode(S.PNG_1x1).decode()
    raw = (
        '{"fileContainerName":"admin","fileType":2,"generateUniqueFileName":false,'
        f'"fileName":"a.png","fileNames":["b.png"],"content":"{png}"}}'
    )
    r = post_json_raw("/api/file-management/file/with-bytes", raw, token=S.Fx.admin_token)
    ok = r.status == 400
    probe("ocr_s1_cross_alias_names", ok, f"status={r.status} code={S.error_code(r)}")


def t_s1_nested_fileinfos_dup():
    png = base64.b64encode(S.PNG_1x1).decode()
    raw = (
        '{"fileContainerName":"admin","fileInfos":[{'
        f'"fileName":"a.png","FileName":"evil.svg","mimeType":"image/png","fileType":1,'
        f'"content":"{png}"}}]}}'
    )
    r = post_json_raw("/api/file-management/file/many/with-bytes", raw, token=S.Fx.admin_token)
    ok = r.status == 400
    probe("ocr_s1_nested_dup_fileName", ok, f"status={r.status} code={S.error_code(r)}")


def t_legit_png_passes():
    png = base64.b64encode(S.PNG_1x1).decode()
    payload = {
        "fileContainerName": "admin", "fileType": 2, "generateUniqueFileName": False,
        "fileName": f"ocr-ok-{uuid.uuid4().hex[:8]}.png", "content": png,
    }
    r = S.req(WEB, "POST", "/api/file-management/file/with-bytes",
              token=S.Fx.admin_token, data=json.dumps(payload).encode(),
              headers={"Content-Type": "application/json"})
    probe("ocr_ctrl_legit_png", r.status in (200, 201), f"status={r.status}（合法 PNG 不得误伤）")


# ---- 导入反提权 ----
def t_import_role_beyond_authority():
    uid = f"ocr.imp.role.{uuid.uuid4().hex[:6]}"
    data = csv_import_bytes([{
        "userName": uid, "email": f"{uid}@sectest.local", "roles": "admin",
    }])
    r = post_import(S.Fx.per["importop"], data)
    text = r.text()
    j = r.json if isinstance(r.json, dict) else {}
    failed = int(j.get("failedCount") or 0)
    errs = j.get("errors") or []
    err_text = " ".join(str(e.get("errorMessage", "")) for e in errs if isinstance(e, dict))
    # 部分成功语义：整单 200，行级 failedCount>=1，文案含「越出」
    msg_hit = any(k in (text + err_text) for k in ("越出", "beyond", "BeyondAuthority", "authority"))
    created = S.api("GET", f"/api/identity/users?Filter={urllib.parse.quote(uid)}").json
    items = [x for x in (created or {}).get("items", []) if x["userName"] == uid]
    leaked = False
    if items:
        roles = S.api("GET", f"/api/identity/users/{items[0]['id']}/roles").json
        role_names = [x.get("name") for x in (roles.get("items") or roles or [])]
        leaked = "admin" in [n.lower() for n in role_names if n]
    # 硬条件：不得授出 admin；软证据：行失败或越权文案（若整行回滚则无用户）
    ok = (not leaked) and (failed >= 1 or msg_hit or not items)
    probe("ocr_import_role_beyond", ok,
          f"status={r.status} failed={failed} created={bool(items)} leaked_admin={leaked} "
          f"msg_hit={msg_hit} err={err_text[:160] or text[:160]}")


def t_import_ou_privileged_beyond():
    """把用户挂进挂着 admin 的 OU —— 受限导入者必须失败（行失败，无半截用户）。"""
    ous = S.api("GET", "/api/app/organization-unit").json
    priv = next((o for o in ous if o["displayName"] == "sectest-privileged"), None)
    if not priv:
        probe("ocr_import_ou_beyond", False, "fixture ou_priv missing")
        return
    # 显示名可解析；有 Code 时优先 Code
    ou_token = priv.get("code") or priv["displayName"]
    uid = f"ocr.imp.ou.{uuid.uuid4().hex[:6]}"
    data = csv_import_bytes([{
        "userName": uid, "email": f"{uid}@sectest.local", "ous": ou_token,
    }])
    r = post_import(S.Fx.per["importop"], data)
    j = r.json if isinstance(r.json, dict) else {}
    failed = int(j.get("failedCount") or 0)
    errs = j.get("errors") or []
    err_text = " ".join(str(e.get("errorMessage", "")) for e in errs if isinstance(e, dict))
    msg_hit = any(k in (r.text() + err_text) for k in ("越出", "beyond", "BeyondAuthority"))
    created = S.api("GET", f"/api/identity/users?Filter={urllib.parse.quote(uid)}").json
    items = [x for x in (created or {}).get("items", []) if x["userName"] == uid]
    attached = False
    if items:
        mem = S.api("GET", f"/api/identity/users/{items[0]['id']}/organization-units").json
        ou_ids = [x.get("id") for x in (mem.get("items") or mem or [])]
        attached = priv["id"] in ou_ids
    ok = (not attached) and (failed >= 1 or msg_hit or not items)
    probe("ocr_import_ou_beyond", ok,
          f"status={r.status} failed={failed} created={bool(items)} attached_priv={attached} "
          f"msg_hit={msg_hit} ou={ou_token} err={err_text[:160] or r.text()[:160]}")


def t_ou_interactive_escalation_public_code():
    """回归：交互面仍 403 + RoleDataScopeEscalation 公开码（目录已登记）。"""
    tok = S.Fx.per["ouop"]
    r = S.req(WEB, "POST", f"/api/app/organization-unit/{S.Fx.ids['ou_normal']}/roles",
              token=tok, json_body=[S.Fx.ids["admin_role"]])
    code = S.error_code(r) or ""
    leak = S.leaks_internal_code(r)
    # 公开码允许 AbpAdmin:DataScope:RoleDataScopeEscalation（已进客户目录）
    ok = r.status == 403 and "RoleDataScopeEscalation" in code and not (
        leak and "INTERNAL_ONLY" in r.text())
    probe("ocr_ou_escalation_public_code", ok, f"status={r.status} code={code} leak_scan={leak}")


# ---- PKCE ----
def t_pkce_required_on_public_auth_code():
    """种子面：AbpAdmin_Swagger / AbpAdmin_App 须 requirePkce=true；
    运行面：无 code_challenge 的 authorize 不得发码（OpenIddict 拒 PKCE 缺失）。"""
    r = S.api("GET", "/api/app/open-iddict-application?SkipCount=0&MaxResultCount=50")
    if r.status != 200 or not r.json:
        probe("ocr_pkce_seed_apps", False, f"list apps status={r.status}")
        return
    items = r.json.get("items") or []
    needed = ("AbpAdmin_Swagger", "AbpAdmin_App")
    found = {}
    for app in items:
        cid = app.get("clientId") or app.get("ClientId")
        if cid in needed:
            found[cid] = bool(app.get("requirePkce"))
    missing = [k for k in needed if k not in found]
    bad = [k for k, v in found.items() if not v]
    seed_ok = not missing and not bad

    # 运行面：公共客户端 Authorization Code 无 PKCE → 应失败（非 302 带 code=）
    q = urllib.parse.urlencode({
        "client_id": "AbpAdmin_App",
        "response_type": "code",
        "redirect_uri": "http://localhost:8004/callback",
        "scope": "openid AbpAdmin",
        "state": "ocr-pkce",
        # 故意不带 code_challenge / code_challenge_method
    })
    auth = S.req(HOST, "GET", "/connect/authorize?" + q, token=S.Fx.admin_token)
    loc = auth.headers.get("Location") or auth.headers.get("location") or ""
    # 成功走私会 302 到 redirect 且带 code=；拒 PKCE 常见 400 或 error= 重定向
    leaked_code = "code=" in loc and "error=" not in loc
    runtime_ok = auth.status in (400, 403) or ("error=" in loc) or (auth.status == 302 and not leaked_code) \
        or (auth.status == 200 and "error" in (auth.text() or "").lower())
    # 若仍停在登录页（未登录）不算 PKCE 走私成功；有 token 时应直接判
    ok = seed_ok and runtime_ok and not leaked_code
    probe("ocr_pkce_seed_apps", ok,
          f"seed={found} missing={missing} bad={bad} authorize={auth.status} "
          f"loc={loc[:120]} leaked_code={leaked_code}")


# ---- XFF / KnownProxies ----
def t_xff_spoof_ignored_from_non_proxy():
    """客户端直连 host 时自带 X-Forwarded-For 不得被当成真实 IP（KnownProxies 未匹配）。
    对照：两次不同 XFF 的匿名健康检查应仍可达；限流桶不因伪造 XFF 分裂——
    用 LoginByIp 敏感面：同一对端用不同 XFF 应共享计数（难精确断言），
    这里断言：伪造 XFF 不能让未认证管理面变成 200。"""
    r1 = S.req(HOST, "GET", "/api/identity/users?MaxResultCount=1",
               headers={"X-Forwarded-For": "1.2.3.4"})
    r2 = S.req(HOST, "GET", "/api/identity/users?MaxResultCount=1",
               headers={"X-Forwarded-For": "8.8.8.8"})
    ok = r1.status in (401, 302) and r2.status in (401, 302)
    probe("ocr_xff_spoof_no_auth_bypass", ok,
          f"xff1={r1.status} xff2={r2.status}（须仍未认证）")


def t_bad_known_proxies_would_fail_closed():
    """配置面：当前栈 KnownProxies=172.16.0.0/12 必须可解析（容器已健康即证据）。
    再确认 compose 环境变量仍钉着网段（非空）。"""
    out = subprocess.run(
        ["docker", "inspect", "abpadmin-sectest-host-1",
         "--format", "{{range .Config.Env}}{{println .}}{{end}}"],
        capture_output=True, text=True, timeout=15).stdout
    line = next((l for l in out.splitlines() if l.startswith("ForwardedHeaders__KnownProxies=")), "")
    ok = "172.16.0.0/12" in line or "KnownProxies=" in line and line.split("=", 1)[-1].strip() != ""
    probe("ocr_known_proxies_configured", ok, f"env={line or '(missing)'}")


def t_privilege_case_sensitivity_admin_role():
    """不确定面：操作者角色名大小写与 admin 比较须 IgnoreCase。
    导入面尝试授 'Admin'（大小写变体）——受限导入者仍须行失败（与 admin 同槽）。"""
    uid = f"ocr.imp.case.{uuid.uuid4().hex[:6]}"
    data = csv_import_bytes([{
        "userName": uid, "email": f"{uid}@sectest.local", "roles": "Admin",
    }])
    r = post_import(S.Fx.per["importop"], data)
    j = r.json if isinstance(r.json, dict) else {}
    # 角色名大小写：库中是 'admin'；'Admin' 可能 RoleNotFound 或 BeyondAuthority——都算拦住
    failed = int(j.get("failedCount") or 0)
    created = S.api("GET", f"/api/identity/users?Filter={urllib.parse.quote(uid)}").json
    items = [x for x in (created or {}).get("items", []) if x["userName"] == uid]
    leaked = False
    if items:
        roles = S.api("GET", f"/api/identity/users/{items[0]['id']}/roles").json
        role_names = [x.get("name") for x in (roles.get("items") or roles or [])]
        leaked = any(n and n.lower() == "admin" for n in role_names)
    ok = (not leaked) and (failed >= 1 or not items)
    probe("ocr_privilege_case_admin", ok,
          f"status={r.status} failed={failed} created={bool(items)} leaked={leaked} "
          f"body={(r.text() or '')[:180]}")


def t_public_error_catalog_has_escalation():
    """公共错误目录须登记 RoleDataScopeEscalation / IdentityClaimsCannotCarryRole（OCR 对齐）。
    源文件为准（web SPA catch-all 会对未知路径回 HTML 假 200）。"""
    body, hit = "", None
    for rel in (
        "web/src/abp/customer/public-error-catalog.json",
        "frontend/apps/web/public/public-error-catalog.json",
    ):
        p = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", rel))
        if os.path.isfile(p):
            with open(p, encoding="utf-8") as f:
                body = f.read()
            hit = p
            break
    need = ("RoleDataScopeEscalation", "IdentityClaimsCannotCarryRole")
    missing = [k for k in need if k not in body]
    # 可解析 JSON 且含 codes
    parse_ok = False
    try:
        parse_ok = isinstance(json.loads(body), (dict, list))
    except Exception:
        parse_ok = False
    ok = bool(body) and parse_ok and not missing
    probe("ocr_public_error_catalog", ok,
          f"src={hit} missing={missing} parse_ok={parse_ok} bytes={len(body)}")


def main():
    print("=== OCR fix / attack probes on abpadmin-sectest ===")
    if not S.wait_stack(180):
        print("stack not healthy")
        sys.exit(2)
    setup_fx()
    tests = [
        t_s1_duplicate_filename_case,
        t_s1_duplicate_content_case,
        t_s1_cross_alias_filename_filenames,
        t_s1_nested_fileinfos_dup,
        t_legit_png_passes,
        t_import_role_beyond_authority,
        t_import_ou_privileged_beyond,
        t_ou_interactive_escalation_public_code,
        t_pkce_required_on_public_auth_code,
        t_xff_spoof_ignored_from_non_proxy,
        t_bad_known_proxies_would_fail_closed,
        t_privilege_case_sensitivity_admin_role,
        t_public_error_catalog_has_escalation,
    ]
    for t in tests:
        try:
            t()
        except Exception as e:
            probe(t.__name__, False, f"EXC {type(e).__name__}: {e}")

    passed = sum(1 for _, ok, _ in ROWS if ok)
    failed = [(n, d) for n, ok, d in ROWS if not ok]
    print(f"\n=== SUMMARY {passed}/{len(ROWS)} PASS ===")
    for n, d in failed:
        print(f"  FAIL {n}: {d}")

    report = os.path.join(os.path.dirname(__file__), "REPORT-ocr-fix.md")
    with open(report, "w", encoding="utf-8") as f:
        f.write("# OCR 修复面攻击探针报告\n\n")
        f.write(f"栈：abpadmin-sectest · {time.strftime('%Y-%m-%d %H:%M:%S')}\n\n")
        for n, ok, d in ROWS:
            f.write(f"- {'✅' if ok else '❌'} **{n}** — {d}\n")
        f.write(f"\n合计 {passed}/{len(ROWS)} PASS\n")
    print(f"wrote {report}")
    sys.exit(0 if not failed else 1)


if __name__ == "__main__":
    main()
