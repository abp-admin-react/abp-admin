# cloudflare-worker（模板遗留 · demo）

ant-design-pro 上游模板自带的演示 mock API（wrangler 名 `pro-api`，静态 serving
`/api/dashboard`、`/api/table` 等演示数据，CORS 兜底 `https://preview.pro.ant.design`）。

**本仓库前端（web/src）没有任何地方调用它**；后端 Turnstile 人机验证集成在
`backend/src/AbpAdmin.Domain/Captcha/TurnstileCaptchaValidator.cs`，与本目录无关。

保留仅为对齐上游模板的预览形态。fork/派生新项目时可**整体删除本目录**，
不影响框架任何能力（见 README「派生一个新项目」与 docs/framework-contracts.md §6）。
