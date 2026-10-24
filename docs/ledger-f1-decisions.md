# Ledger F1 实现决定（定稿未写清处）

日期：2026-10-14（Asia/Shanghai）

| 项 | 决定 |
|----|------|
| HTTP 前缀 | ABP 约定 `/api/app/...`，不另开 `/api/finance` |
| Integration 服务 | `[RemoteService(IsEnabled=false)]`，仅进程内；演示过账统一走 FinDemoEventAppService：`POST /api/app/fin-demo-event`（仅 `demo.*`；FinLedgerAppService 上的同构副本已删，防双契约漂移） |
| 调账五态枚举 | 重排：Draft=0, Pending=1, Rejected=2, Posted=3, Reversed=4（F0 无 Pending、无存量） |
| 人工调账规则 | Template v2：debit=`*` credit=`*`，取调账单借贷科目；v1 Deprecated |
| 红冲 BatchKey | `rev:{原凭证Id:N}`；业务 BatchKey 禁止此前缀 |
| 幂等内容指纹 | SHA-256(Amount|Currency|UserId|AgentId|From|To)；同键同指纹返回原凭证，异指纹 409 |
| 并发 | `pg_advisory_xact_lock(hashtextextended)` + 子账户 `FOR UPDATE`（Id 排序）+ `lock_timeout=5s`；UK `NULLS NOT DISTINCT` |
| 非负 | 仅 User/Agent 子户且科目 `AllowNegative=false` 时拒负余额 |
| 账户解析 | 2201/2202/2205→User；2203→Agent；1002→Channel(dimensions.channel\|\|default)；其余→Platform |
| 代理 Scope | F1：`Finance.Scope.Agent` 时 OwnerId=当前用户 Id（代理绑定表 F5 再接） |
| Prepayment 停写 | 本地实体事件拦 Account/Transaction/Withdrawal* 的 Changed；设置 `Finance.Prepayment.FreezeWrites` |
| 开关 | 配置 `Ledger:Enabled` ∧ 设置 `Finance.Ledger.Enabled`；`Ledger:BizTypes:{type}` 灰度；manual-adjust 不受业务开关限制 |
| A2 科目 CRUD | F1 已做：FinAccountAppService（系统科目只读；有分录不可删/不可改 code·type·direction）；A4 关账/A6 业务联查属后续 |
| 调账职责分离 | 2026-10-14 海峰定：不写死，改权限配置。删除"申请人==审批人一律拒绝"；建/提交/审批/驳回/冲销分别只看 `Finance.Adjust.Create/Submit/Approve/Reject/Reverse`；新增 `Finance.Adjust.ApproveOwn`（可审批自己提交的调账单），审批/驳回自己的单没有它 → 403 `Finance:Adjust:SelfApproveNotAllowed`（替代 `SelfApproveForbidden`）。admin 不绕过。改/删/提交/reopen 仍限申请人本人（单据归属）。默认：fin_accountant=Create+Submit+Approve+Reject+Reverse，fin_boss=Approve+Reject（无 Create/Submit/Reverse），ApproveOwn 不授任何人；老板可在角色权限页自行勾选。 |
| 调账 reason | 2026-10-14 产品定：建/改草稿可不填；**提交审批时必填**。空/空白提交 → 400 `Finance:Adjust:InvalidInput`，`data.Fields=reason`（与建单非法字段同形）。 |

## 2026-10-25 提现 M2/M3（首个真实 F3 消费者：Collection 模块）

- BizType 新增 `deposit`（phase=pay，借 1001/贷 2201）与 `withdraw`（freeze/unfreeze/approve/fee-take/payout/refund/fee-refund 七 phase）；`ExpensePhases` 加入 `payout`（真实资金流出时刻）。
- **待付中转科目修正**：提现 approve/payout 用 **2241 应付账款**，不用 1002 渠道清算——1002 是资产类借方科目，审核时贷记它等于链上资产提前减少（原方案表方向错误，实施时修正）。
- ILedgerSeedProvider + LedgerAccountSeed/LedgerRuleSeed 移入 `AbpAdmin.Ledger.Integration.Contracts`：业务模块接入只引用契约工程，不引用 Ledger 实现（原先接口在实现工程里，业务模块被迫整包引用）。
- 科目 6003 提现手续费收入由 CollectionLedgerSeedProvider 播种（业务码，框架不种）。
- 充值入账过账随提现一并落地（归集确认同事务，BizRefId=进款单 Id）；存量差额走 wallet/post 开账回填（管理端幂等接口）。
- 币种码统一大写（USDT/USDC），收口在 WithdrawLedgerPoster.CurrencyCode——枚举 ToString 是 "Usdt"，大小写错位会让账本子账户写入与查询互不可见。
