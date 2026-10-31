# sectest 专用安全测试栈 — 运行手册

独立项目 `abpadmin-sectest`（与维护栈 `abp-admin-*` 完全隔离：独立容器/网络/卷/端口）。
黑盒攻击视角验证 run-1/run-2 全部修复项 + 复现已确认 open 线索。

## 拉起 / 清理

```bash
# 构建并启动（含当前工作区全部修复代码）
WEB_BIND=127.0.0.1 WEB_HOST_PORT=18000 docker compose -p abpadmin-sectest \
  -f docker-compose.yml -f sectest/docker-compose.sectest.yml up -d --build

# 改了后端代码后只重建 host（DB 已迁移，跳过 migrator）
WEB_BIND=127.0.0.1 WEB_HOST_PORT=18000 docker compose -p abpadmin-sectest \
  -f docker-compose.yml -f sectest/docker-compose.sectest.yml up -d --no-deps --build host

# 清理（连数据卷一起删）
docker compose -p abpadmin-sectest -f docker-compose.yml \
  -f sectest/docker-compose.sectest.yml down -v
```

端口：web 边缘 `127.0.0.1:18000`（nginx）· host 直连 `127.0.0.1:18081`（绕过 nginx）。

## 运行测试

```bash
python sectest/sectest.py            # 全量（A 组防线验证 + B 组 open 线索复现）
```

产物：`sectest/REPORT-sectest.md` + 退出码（A 组全过=0）。

## 迭代便利

- 令牌缓存 `sectest/.tokens.json`（ROPC 受 LoginByIp 20/15min 限制；删缓存会强制重登录）
- 清限流计数（迭代期专用，测试 a12 会重新制造并验证它们）：
  `docker exec abpadmin-sectest-redis-1 redis-cli EVAL "local ks=redis.call('keys','*OperationRateLimitingCounter*') for i,k in ipairs(ks) do redis.call('del',k) end return #ks" 0`
- fixtures 幂等（角色/用户/OU/租户已存在即复用）

## 已知边界事实（测试断言依赖）

1. **nginx 丢弃带下划线的请求头**（未开 `underscores_in_headers`）：`__tenant` 头经 web
   边界到不了宿主。租户转向向量只在直连 host（18081）测；经边界的跨租户请求实际落在
   host 上下文（另见 REPORT 的 a13/b04 说明）。SPA 若依赖 `__tenant` 头选租户，经此
   nginx 也会失效（现状未观察到 SPA 依赖；如需头选租户，nginx 加 `underscores_in_headers on;`）。
2. 未认证 API 请求：宿主回 302→`/Error?httpStatusCode=401`（错误页改写），nginx 侧 401/302
   ——断言按"非 200 业务数据"判定。
3. 测试栈默认 compose 姿态 = NV-20 复现环境（migrator 默认 Development 种出
   admin/1q2w3E* + AbpAdmin_TestCli ROPC 内嵌密钥）——这正是 b08 要证明的事实。
