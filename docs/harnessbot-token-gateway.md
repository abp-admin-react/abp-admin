# HarnessBot 路线 C · TokenGateway 联调说明

模块：`backend/modules/AbpAdmin.TokenGateway`（挂在 HttpApi.Host）。测试：`backend/test/modules/AbpAdmin.TokenGateway.Tests`。

> 第二批起全部落库（默认连接串 Default，独立迁移 History 表 `__TokenGateway_EFMigrationsHistory`，宿主 AutoMigrateOnStartup 自动建表）。
> 表：TgWallets / TgLedgerEntries / TgReservations / TgApiKeys / TgSessions / TgOrders / TgUsage / TgPlans（种子四档）。
> 原子性：钱包计数器只用条件 UPDATE（PG 行锁），预占不足 0 行即断供；预占单 Open→Settled/Released 条件翻转，只结一次；每步写流水，同一事务。
> 对账恒等式：可用(available) = Σ充值 − Σ已结算 − 当前冻结；`balance` 字段 = Σ充值 − Σ已结算。`Wallet.ReconcileAsync` 用流水重算核对。
> 充值幂等：流水 (Kind, Ref) 唯一，Ref = 订单号；订单状态翻转与入账同事务。

## 起服务

本机开发宿主（44300）：

```powershell
cd backend\framework\AbpAdmin.HttpApi.Host
$env:TokenGateway__Upstream__Mode='Mock'                 # 不接 one-api，回联调桩并照常 reserve→settle
dotnet run --launch-profile AbpAdmin.HttpApi.Host
```

不设 `Upstream.Mode=Mock` 且 `Upstream.BaseUrl` 为空 → 对话返回 `UPSTREAM_UNAVAILABLE`（余额全额解冻）。

## 前端 BaseURL

- 登录/钱包：`{host}/api/auth/*`、`{host}/api/wallet/*`，Bearer = 登录返回的 `session`（hbs-）或 `apiKey`（hb-）
- 对话（OpenAI SDK）：`baseURL = {host}/v1`，`apiKey = hb-xxx`（会话令牌不认）
- 本机：`https://localhost:44300`

## 接口

| 方法 | 路径 | 说明 |
|---|---|---|
| POST | /api/auth/login | body `{username, password}` → `{session, sessionExpiresAt, apiKey, user}`；同一账号复用同一把 hb- |
| GET | /api/wallet/balance | `{percent, low, balance, frozen, available, periodUsed, updatedAt}`；从未到账 → percent=0, low=true |
| GET | /api/wallet/plans | `{items:[{id,name,priceCny,tokens}]}` 四档 p30/p98/p198/p498 |
| POST | /api/wallet/orders | body `{planId, method:"wechat"\|"alipay"}` → `{orderId, amountCny, tokens, method, qrUrl, expiresAt, status}` |
| GET | /api/wallet/orders/{id} | status：pending / paid / expired / failed |
| POST | /api/wallet/orders/{id}/refresh-qr | expired→pending，新 qrUrl/expiresAt；paid/failed 拒 |
| POST | /api/wallet/orders/{id}/simulate-paid | **仅 Development 环境**且 `Payment.Mode=Stub`；其他环境一律 404，任何配置都打不开 |
| GET | /api/wallet/usage?take=50 | `{summary:{periodUsed,balance,frozen,available}, items:[{time,alias,bot,promptTokens,completionTokens,cost,success}]}` |
| GET | /v1/models | 只列对外别名（hb-chat） |
| POST | /v1/chat/completions | OpenAI 兼容，支持 stream（同一请求各帧 `id` 相同，均为 hb-）；可带 `X-HB-Bot` 头写进用量 |
| GET | /api/token-gateway/admin/plans | 价表（含下架），宿主登录态 + admin 角色 |
| PUT | /api/token-gateway/admin/plans/{id} | body `{name?, priceCny?, tokens?, sortOrder?, isActive?}`；已建订单按建单时快照，不受改价影响 |

错误体统一：`{"error":{"code","message","type"}}`。对话码：INSUFFICIENT_BALANCE(402) / INVALID_KEY(401) / RATE_LIMITED(429，单用户并发>4) / UPSTREAM_UNAVAILABLE(503) / INTERNAL(500)。登录失败 INVALID_CREDENTIALS(401)。stream 已开始后出错以 `data: {"error":{...}}` 帧下发。

口径：percent = floor((余额−冻结) ÷ 累计到账 × 100)；本期 = 北京时间自然月；qrUrl 当前是桩 SVG。

## 配置（`TokenGateway` 段，全部有默认值）

```json
"TokenGateway": {
  "TrialTokens": 0,
  "Aliases": [ { "Alias": "hb-chat", "Model": "CHANGE_ME", "PromptRate": 1, "CompletionRate": 1, "DefaultMaxTokens": 4096 } ],
  "Payment": { "Mode": "Stub", "OrderTtlSeconds": 300,
               "WechatMchId": "CHANGE_ME", "WechatApiV3Key": "CHANGE_ME", "AlipayAppId": "CHANGE_ME", "AlipayPrivateKey": "CHANGE_ME" },
  "Upstream": { "Mode": "OneApi", "BaseUrl": "", "InternalToken": "CHANGE_ME", "TimeoutSeconds": 120 }
}
```

真实商户：`Payment.Mode=Live` 前需实现 `/api/wallet/notify/wechat|alipay` 验签 → `OrderBook.MarkPaid`（现在返回 501；Live 下建单返回 503「支付暂未开通」）。密钥只走环境变量/密钥库，不进仓库。

## one-api 内网草图（本批未起）

```yaml
services:
  one-api:
    image: justsong/one-api:latest   # 官方镜像，不改源码
    restart: unless-stopped
    environment:
      - TZ=Asia/Shanghai
      - SQL_DSN=            # 可留空用内置 sqlite
    volumes: [ "./one-api-data:/data" ]
    networks: [ internal ]  # 不映射 ports，不对公网
networks:
  internal: { internal: true }
```

宿主同网后设 `TokenGateway__Upstream__BaseUrl=http://one-api:3000`、`TokenGateway__Upstream__InternalToken=<one-api 内部令牌>`；one-api 里一个内部用户、一把令牌、额度无限；别名 `Model` 填 one-api 的模型名。
