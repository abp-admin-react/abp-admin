# Collection 提现（M2/M3 实现说明 + 运维手册）

参考 E:\code\exchange\exchange 的提现闭环（冻结→审核→广播→对账）落在 Collection 模块内，
账本走 `ILedgerIntegrationService`（首个真实 F3 业务消费者）。

## 状态机（ColWithdrawOrders.Status，WithdrawStatus）

```
Pending(0) ──审批(小额自动/人工)──→ Processing(1) ──取件──→ Broadcasting(2) ──回执成功──→ Completed(3)
   │ 用户撤单                                    │                    │
   ↓                                            │              回执失败(未超限) → 回 Processing（RetryCount+1）
Cancelled(6)                                    │              回执失败(超限)   → BroadcastFailed(5)
   │                                            │              无哈希超时        → BroadcastFailed「需人工核查」
Pending ──拒绝──→ Rejected(4)                    └── 发送异常（结果未知）──→ 保持 Broadcasting 无哈希
BroadcastFailed ──重试(验回执/双花门)──→ Processing
BroadcastFailed ──退款拒绝──→ Rejected（净额+手续费退回可用）
```

- 所有迁移 CAS 条件 UPDATE，0 行 = `WithdrawInvalidStatus`。
- **双花防线**：发送结果未知（无哈希超时）绝不重播，转 BroadcastFailed 等人工；
  重试前有哈希先验回执、无哈希过「近期已向该地址付款」双花门；nonce 每次广播取热钱包
  pending nonce，不复用缓存（杜绝替换重签）。

## 余额两桶（ColBalances，M1）

`Balance = 可用`、`Frozen = 提现冻结`，合计 = Balance + Frozen（金库核对/开账回填按合计）。
原语：`CollectionBalanceManager.Freeze/Unfreeze/DeductFrozen/Refund`，幂等键
`W:{提现单 Id:N}:{动作}`（BalanceIdempotencyKeys 唯一定义处）。
两桶审计不变量见 `BalanceAuditJobHandler`（可用桶剔除 WithdrawSettle 行；冻结桶按动作方向合计）。

## 账本过账（BizType=withdraw，七 phase）

| 动作 | Phase | 借 | 贷 | 金额口径 |
|---|---|---|---|---|
| 提交冻结 | freeze | 2201 | 2202 | 总额（净+费） |
| 撤单/拒绝 | unfreeze | 2202 | 2201 | 总额 |
| 审核通过 | approve | 2202 | **2241 应付** | 净额 |
| 手续费实现 | fee-take | 2202 | 6003 | Fee（0 跳过） |
| 打款完成 | payout | 2241 | 1001 | 净额（计入 ExpensePhases） |
| 失败退款 | refund | 2241 | 2201 | 净额 |
| 退手续费 | fee-refund | 6003 | 2201 | Fee（0 跳过） |

- 待付中转用 **2241 应付账款**（框架已种）而非 1002 渠道清算：1002 是资产类借方科目，
  审核时贷记它等于链上资产提前减少（原计划表的账务方向修正，已记 ledger-f1-decisions）。
- 充值入账（BizType=deposit, phase=pay，借 1001 / 贷 2201）在归集确认同事务过账（SweepManager）。
- 科目/模板种子：`CollectionLedgerSeedProvider`（ILedgerSeedProvider 扩展点，宿主 DependsOn 即播种）。
- 币种码统一大写 `USDT`/`USDC`（`WithdrawLedgerPoster.CurrencyCode`——枚举 ToString 是 "Usdt"，
  大小写错位会让账本写入与查询互不可见，实测踩过）。
- 存量开账回填：管理端 `POST /api/app/collection-withdraw-admin/backfill-opening`
  （BizType=wallet, phase=post，BizRefId=`opening:{userId}:{currency}`，幂等可重跑）。

## 护栏（WithdrawGuard）

三道检查：审批端点/自动审前 → 广播前 → 签名临界区内发送前（总闸，关 TOCTOU）：
- 热钱包代币余额 ≥ 净额，否则 `HotWalletInsolvent`；
- 低于 `Collection.Withdraw.HotWalletUsdtFloor`（默认 1000）= **熔断**：阻断所有审批并告警
 （`[Alert:WithdrawHotWallet]`），补币后自动恢复；
- BNB < `Collection.Withdraw.MinHotWalletBnb`（默认 0.005）= `HotWalletGasShort`；
- 运行时总闸 `Collection.Withdraw.Halt`（SettingUi 可切）。

## 配置与开关

- 宿主配置（appsettings `Collection:Withdraw`）：`Enabled`（默认 **false**：提交拒绝、worker 睡眠）、
  `BroadcastIntervalSeconds`(15)、`RoundBudgetSeconds`(120)、`BroadcastBatchSize`(5)、`AutoApproveBatchSize`(20)。
- 运行期设置（SettingUi，`Collection.Withdraw.*`）：MinAmount(10)、FeeRate(0.004，8 位截断)、
  MaxDailyAmount(50000，空=不限)、MaxDailyCount(3)、AutoApproveMaxAmount(100)、Halt(false)、
  BroadcastTimeoutMinutes(30)、MaxRetries(3)、HotWalletUsdtFloor(1000)、MinHotWalletBnb(0.005)。
- 权限：`AbpAdmin.Collection.WithdrawAdmin`（Host 专用；管理端服务 AssertHost 双保险）。
- worker 注册：宿主 `AddCollectionChainWorkers`（Redis 锁守卫同扫链/归集）；DbMigrator 不注册。

## 上线步骤（运维手册）

1. 迁移落库（Collection 迁移链 +1：AddBalanceFrozen / AddWithdrawOrders / AddWithdrawHotWallet）。
2. 账本种子已随 IDataSeeder 播种（1001/2201/2202/6003 + 七条模板）；确认 `Ledger:Enabled=true`。
3. 生成独立热钱包（**与归集助记词不同源**），管理端
   `POST /api/app/collection-withdraw-admin/set-withdraw-vault`（body 传地址+私钥，
   服务端校验私钥与地址匹配，密文入库）。
4. 执行 `backfill-opening` 一次性开账（把存量 ColBalance 合计差额过账进 2201）。
5. SettingUi 调限额/费率/地板值 → appsettings 开 `Collection:Withdraw:Enabled=true` 重启。
6. 日常：管理页 `/collection/withdrawals`（状态卡 + 四态审核）；熔断告警走
   `[Alert:WithdrawHotWallet]` 日志 + ChainAlertService 站内信。
7. 卡单处置：BroadcastFailed 无哈希 → 先链上核实热钱包近期出站 → 再重试或人工完结（补哈希，
   服务端强制回执成功）或退款拒绝。

## 已知边界（后续里程碑）

- 人工完结只核对哈希+回执状态，金额级核对依赖人工（回执日志不含金额解析）。
- 三方对账（链上 vs ColBalance vs Fin 投影）归 P1（VaultAudit/BalanceAudit 扩展）。
- 提现侧用户 API 已备（/api/app/collection-withdraw），用户端 H5 未在本仓库。
