# HarnessBot 路线 C · TokenGateway 联调说明（第一批 MVP）

模块：`backend/modules/AbpAdmin.TokenGateway`（挂在 HttpApi.Host）。测试：`backend/test/modules/AbpAdmin.TokenGateway.Tests`。

> MVP 账本/Key/订单在**进程内存**，宿主重启清零。落库（行锁、对账流水表）是下一批。

## 起服务

本机开发宿主（44300）：

```powershell
cd backend\framework\AbpAdmin.HttpApi.Host
$env:TokenGateway__Upstream__Mode='Mock'                 # 不接 one-api，回联调桩并照常 reserve→settle
$env:TokenGateway__Payment__EnableSimulatePay='true'     # 仅测试环境！
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
| POST | /api/wallet/orders/{id}/simulate-paid | **仅测试**：`Payment.Mode=Stub` 且 `EnableSimulatePay=true`，否则 404 |
| GET | /api/wallet/usage?take=50 | `{summary:{periodUsed,balance,frozen,available}, items:[{time,alias,bot,promptTokens,completionTokens,cost,success}]}` |
| GET | /v1/models | 只列对外别名（hb-chat） |
| POST | /v1/chat/completions | OpenAI 兼容，支持 stream；可带 `X-HB-Bot` 头写进用量 |

错误体统一：`{"error":{"code","message","type"}}`。对话码：INSUFFICIENT_BALANCE(402) / INVALID_KEY(401) / RATE_LIMITED(429，单用户并发>4) / UPSTREAM_UNAVAILABLE(503) / INTERNAL(500)。登录失败 INVALID_CREDENTIALS(401)。stream 已开始后出错以 `data: {"error":{...}}` 帧下发。

口径：percent = floor((余额−冻结) ÷ 累计到账 × 100)；本期 = 北京时间自然月；qrUrl 当前是桩 SVG。

## 配置（`TokenGateway` 段，全部有默认值）

```json
"TokenGateway": {
  "TrialTokens": 0,
  "Plans": [ { "Id": "p30", "Name": "体验包", "PriceCny": 30, "Tokens": 3000000 } ],
  "Aliases": [ { "Alias": "hb-chat", "Model": "CHANGE_ME", "PromptRate": 1, "CompletionRate": 1, "DefaultMaxTokens": 4096 } ],
  "Payment": { "Mode": "Stub", "OrderTtlSeconds": 300, "EnableSimulatePay": false,
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
