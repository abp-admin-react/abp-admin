#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
abp-admin 基础栈（web 边缘 http://127.0.0.1:8000）黑盒加压。
不依赖 host 直连口；经 nginx 反代打业务面。攻击视角：未认证乱撞、注入、路径变形、信息泄漏。
用法：python sectest/probes_base_stack.py [--base http://127.0.0.1:8000]
"""
from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field

BASE = "http://127.0.0.1:8000"
ADMIN_USER = "admin"
ADMIN_PASS = "1q2w3E*"


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


_OPENER = urllib.request.build_opener(_NoRedirect)


@dataclass
class Resp:
    status: int
    body: bytes
    headers: dict

    def text(self) -> str:
        return self.body.decode("utf-8", "replace") if isinstance(self.body, bytes) else str(self.body)

    @property
    def json(self):
        try:
            return json.loads(self.body) if self.body else None
        except Exception:
            return None


def req(method: str, path: str, *, token=None, json_body=None, form=None, headers=None, timeout=20) -> Resp:
    url = BASE.rstrip("/") + path
    h = {"Accept": "application/json", "User-Agent": "abp-admin-base-probe/1.0"}
    if token:
        h["Authorization"] = "Bearer " + token
    body = None
    if json_body is not None:
        body = json.dumps(json_body).encode()
        h["Content-Type"] = "application/json"
    elif form is not None:
        body = urllib.parse.urlencode(form).encode()
        h["Content-Type"] = "application/x-www-form-urlencoded"
    if headers:
        h.update(headers)
    r = urllib.request.Request(url, data=body, method=method, headers=h)
    try:
        with _OPENER.open(r, timeout=timeout) as resp:
            return Resp(resp.status, resp.read(), dict(resp.headers))
    except urllib.error.HTTPError as e:
        return Resp(e.code, e.read(), dict(e.headers))
    except Exception as e:
        return Resp(0, str(e).encode(), {})


INTERNAL_MARKERS = (
    "Npgsql",
    "StackExchange.Redis",
    "System.NullReferenceException",
    "at Volo.Abp.",
    "ConnectionString",
    "Password=",
    "BEGIN;",
    "SELECT ",
)


@dataclass
class Case:
    id: str
    title: str
    ok: bool
    detail: str
    expect: str = ""


RESULTS: list[Case] = field(default_factory=list) if False else []


def check(cid: str, title: str, ok: bool, detail: str, expect: str = ""):
    RESULTS.append(Case(cid, title, ok, detail, expect))
    mark = "PASS" if ok else "FAIL"
    print(f"[{mark}] {cid} {title} — {detail}")


def no_internal_leak(r: Resp) -> bool:
    t = r.text()
    return not any(m in t for m in INTERNAL_MARKERS)


def main(argv: list[str]) -> int:
    global BASE
    if "--base" in argv:
        i = argv.index("--base")
        BASE = argv[i + 1]

    print(f"== abp-admin base probes against {BASE} ==")
    t0 = time.time()

    # --- surface / liveness ---
    r = req("GET", "/")
    check("p01", "SPA root reachable", r.status in (200, 304), f"status={r.status}", "200/304")

    r = req("GET", "/health-status")
    check(
        "p02",
        "health anonymous OK, no stack leak",
        r.status == 200 and no_internal_leak(r),
        f"status={r.status} body_snip={r.text()[:120]!r}",
        "200 + no internal markers",
    )

    # --- swagger / actuator exposure via edge ---
    for path in ("/swagger", "/swagger/index.html", "/swagger/v1/swagger.json"):
        r = req("GET", path)
        check(
            f"p03-{path}",
            "Swagger not exposed on web edge",
            r.status in (404, 401, 403, 302),
            f"status={r.status}",
            "non-200 business doc",
        )

    for path in ("/health-ui", "/health-api", "/healthchecks-webhooks"):
        r = req("GET", path)
        # UI requires auth after hardening; anonymous must not get panel data
        check(
            f"p04-{path}",
            "health UI/api not anonymously usable",
            r.status in (401, 403, 302, 404) or (r.status == 200 and "login" in r.text().lower()),
            f"status={r.status}",
            "401/403/302/404 or login page",
        )

    # --- unauthenticated API abuse ---
    sensitive = [
        "/api/identity/users",
        "/api/identity/roles",
        "/api/abp/application-configuration",
        "/api/account/my-profile",
        "/api/permission-management/permissions",
        "/api/setting-management",
        "/api/app/audit-log",
        "/api/file-management/file",
        "/api/app/fin-reconcile/run",
    ]
    for path in sensitive:
        r = req("GET", path)
        ok = r.status in (401, 403, 302, 404) and no_internal_leak(r)
        # application-configuration is intentionally reachable anonymously but trimmed
        if path.endswith("application-configuration"):
            ok = r.status == 200 and no_internal_leak(r)
            detail = f"status={r.status} keys={list((r.json or {}).keys())[:8]}"
            check("p05-appconfig", "anonymous app-config trimmed / no secrets", ok, detail, "200 trimmed")
            if r.json:
                settings = (r.json.get("setting") or {}).get("values") or {}
                leak = [k for k in settings if any(x in k.lower() for x in ("password", "secret", "connection", "apikey"))]
                check("p05b-appconfig-secrets", "anonymous settings hide secrets", not leak, f"suspect={leak}", "no secret keys")
            continue
        check(f"p06-{path}", "auth required", ok, f"status={r.status}", "401/403/302/404")

    # --- path traversal / weird methods ---
    weird_paths = [
        "/api/../../../etc/passwd",
        "/api/identity/users/%2e%2e/%2e%2e/etc/passwd",
        "/api/identity/users/..%2f..%2f",
        "//api/identity/users",
        "/API/identity/users",
        "/api/identity/users/",
        "/api/identity/users?;",
    ]
    for path in weird_paths:
        r = req("GET", path)
        check(
            f"p07-{path[:40]}",
            "path abuse does not dump data",
            r.status in (400, 401, 403, 404, 302, 405) and no_internal_leak(r),
            f"status={r.status}",
            "deny / not found",
        )

    for method in ("TRACE", "TRACK", "DEBUG"):
        r = req(method, "/api/identity/users")
        check(
            f"p08-{method}",
            f"{method} disabled",
            r.status in (0, 400, 404, 405, 501) and no_internal_leak(r),
            f"status={r.status}",
            "405/501/404",
        )

    # --- injection-ish login / token ---
    for payload in (
        {"username": "admin' OR '1'='1", "password": "x"},
        {"username": "admin", "password": "' OR '1'='1"},
        {"username": "<script>alert(1)</script>", "password": "x"},
        {"username": "admin\x00", "password": "1q2w3E*"},
        {"username": "../../admin", "password": "1q2w3E*"},
    ):
        r = req(
            "POST",
            "/connect/token",
            form={
                "grant_type": "password",
                "client_id": "AbpAdmin_App",
                "username": payload["username"],
                "password": payload["password"],
                "scope": "AbpAdmin openid profile offline_access",
            },
        )
        got_token = bool(r.json and r.json.get("access_token"))
        check(
            f"p09-{payload['username'][:24]}",
            "malicious login rejected",
            (not got_token) and r.status in (400, 401, 403, 404, 415, 422) and no_internal_leak(r),
            f"status={r.status} token={got_token}",
            "no token",
        )

    # brute-ish wrong passwords (expect fail, optionally rate-limit later)
    fails = 0
    for i in range(6):
        r = req(
            "POST",
            "/connect/token",
            form={
                "grant_type": "password",
                "client_id": "AbpAdmin_App",
                "username": "admin",
                "password": f"WrongPass!{i}",
                "scope": "AbpAdmin openid profile offline_access",
            },
        )
        if r.status in (400, 401, 403) and not (r.json or {}).get("access_token"):
            fails += 1
    check("p10-bruteforce-fail", "wrong passwords fail closed", fails == 6, f"fails={fails}/6", "6/6 fail")

    # --- legitimate admin login (sanity) ---
    r = req(
        "POST",
        "/connect/token",
        form={
            "grant_type": "password",
            "client_id": "AbpAdmin_App",
            "username": ADMIN_USER,
            "password": ADMIN_PASS,
            "scope": "AbpAdmin openid profile offline_access",
        },
    )
    token = (r.json or {}).get("access_token")
    check(
        "p11-admin-login",
        "admin password grant works (or SPA-only client blocks ROPC — then 400 expected)",
        bool(token) or r.status in (400, 401),
        f"status={r.status} has_token={bool(token)} err={(r.json or {}).get('error')}",
        "token or client_not_allowed",
    )

    if token:
        r = req("GET", "/api/identity/users", token=token)
        check("p12-admin-users", "admin can list users", r.status == 200 and no_internal_leak(r), f"status={r.status}")

        # IDOR-ish random guids
        for guid in (
            "00000000-0000-0000-0000-000000000001",
            "ffffffff-ffff-ffff-ffff-ffffffffffff",
            "' OR 1=1--",
        ):
            r = req("GET", f"/api/identity/users/{urllib.parse.quote(guid, safe='')}", token=token)
            check(
                f"p13-{guid[:18]}",
                "bad user id does not 500/leak",
                r.status in (400, 404) and no_internal_leak(r),
                f"status={r.status}",
                "400/404",
            )

        # privilege confusion: create role with wild claims as random junk
        r = req(
            "POST",
            "/api/identity/roles",
            token=token,
            json_body={"name": f"probe_role_{int(time.time())}", "isDefault": False, "isPublic": False},
        )
        check(
            "p14-role-create",
            "role create either succeeds (admin) or rejects cleanly",
            r.status in (200, 201, 400, 403) and no_internal_leak(r),
            f"status={r.status}",
        )

    # --- CORS / header injection noise ---
    r = req(
        "GET",
        "/api/abp/application-configuration",
        headers={"Origin": "https://evil.example", "X-Forwarded-For": "1.2.3.4", "X-Forwarded-Host": "evil.example"},
    )
    acao = r.headers.get("Access-Control-Allow-Origin") or r.headers.get("access-control-allow-origin")
    check(
        "p15-cors-evil-origin",
        "evil Origin not reflected as * with credentials casually",
        acao not in ("https://evil.example",) or True,  # presence alone is weak; just record
        f"status={r.status} ACAO={acao!r}",
        "no evil reflect preferred",
    )
    if acao == "https://evil.example":
        RESULTS[-1].ok = False

    # --- oversized / empty bodies ---
    r = req("POST", "/connect/token", form={})
    check("p16-empty-token", "empty token body rejected", r.status in (400, 415, 422) and no_internal_leak(r), f"status={r.status}")

    r = req(
        "POST",
        "/api/file-management/file/with-bytes",
        json_body={"fileName": "x.jpg", "content": "TVpQ"},  # MZ base64 prefix-ish
    )
    check(
        "p17-anon-upload",
        "anonymous with-bytes upload denied",
        r.status in (401, 403, 302, 404) and no_internal_leak(r),
        f"status={r.status}",
        "auth required",
    )

    elapsed = time.time() - t0
    passed = sum(1 for c in RESULTS if c.ok)
    failed = [c for c in RESULTS if not c.ok]
    print(f"\n== summary: {passed}/{len(RESULTS)} PASS in {elapsed:.1f}s ==")
    if failed:
        print("FAILED:")
        for c in failed:
            print(f"  - {c.id}: {c.title} | {c.detail}")
    report = {
        "base": BASE,
        "passed": passed,
        "total": len(RESULTS),
        "failed": [{"id": c.id, "title": c.title, "detail": c.detail} for c in failed],
        "cases": [{"id": c.id, "title": c.title, "ok": c.ok, "detail": c.detail} for c in RESULTS],
    }
    out = "sectest/REPORT-base-stack-probes.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print(f"wrote {out}")
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
