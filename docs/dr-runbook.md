# 多机房灾备运行手册（方案一：单主多备）

> 定位：A 省主机房承载全部写流量（PG 主库 + Redis 主），B 省机房热备（PG 流复制备库 +
> Redis 从），应用实例两机房都部署、业务流量经 DNS/GSLB 指向 A。这是**灾备不是双活**
> （异步复制，RPO > 0）；升级到方案二（按租户分区多活）的路径见 §6。
> 会踩代码契约的硬约定登记在 [framework-contracts.md §7](framework-contracts.md)（铁律清单），
> 本文是操作面：怎么搭、怎么切、怎么回切。

## 1. 铁律（违反即事故）

1. **OpenIddict issuer 单域名**：`AuthServer:Authority` 作为 issuer 被写进所有签发的
   token（passkey ServerDomain 同源）。双机房必须共用同一个对外域名——同一 issuer
   多实例是合法形态；任何"按机房拆 issuer"的想法都会让另一机房验签全挂。
   **切换机房 = 切 DNS，永远不改 issuer。**
2. **连接串一律 DNS 别名，禁止裸 IP**：`ConnectionStrings:Default`、`Redis:Configuration`
   使用别名（如 `pg-primary.internal` / `redis-primary.internal`）。切换 = 改别名解析，
   应用零改动。这也是升级方案二最省力的前置习惯。历史遗留的裸 IP 配置须在首次上生产前替换。
3. **签名与密钥材料两机房一致**：`openiddict.pfx` 同一份（离线通道分发，不入仓库、
   离线备份）；生产姿态打开 `DataProtection:CertificatePath`/`CertificatePassword`
   （密钥环静态加密，见 framework-contracts §2）——同一证书，切换后旧令牌仍可验。
4. **DbMigrator 只对主库跑**：备库 schema 由流复制天然同步，对备库跑迁移 = 制造分叉。

## 2. PostgreSQL 流复制（异步）

**主库（A 省）**：`postgresql.conf` 设 `wal_level = replica`、`max_wal_senders >= 3`，
建复制账号（仅 REPLICATION 权限）；建议同时开 WAL 归档（`archive_mode = on`），
作为备库重建与 PITR 的补充。

**备库（B 省）**：`pg_basebackup -R` 拉起（自动写 `standby.signal` 与 `primary_conninfo`），
`hot_standby = on` 供只读查询。备库升级大版本/重建时同样走 basebackup，不跑迁移。

**监控与告警最低配置**：
- `pg_stat_replication` 的 `replay_lag` 持续增长 = 复制断/跟不上，告警；
- 主库 `pg_wal` 目录膨胀 = 复制断开且复制槽未清，告警（磁盘风险）；
- 备库每日抽样只读查询，确认 `pg_is_in_recovery() = true` 且数据新鲜。

**RPO 预期**：异步复制，主库整机崩溃可能丢失最近秒级未传 WAL——与 §4 的锁/密钥窗口同源。

## 3. Redis 主从（异步）与角色拆分

从库 `replicaof <主>`，两机房都配 `masterauth`/`requirepass` 与网络 ACL。

**关键认知——Redis 的两种角色对一致性的要求完全不同**：

| 角色 | 承载 | 一致性要求 | 切换后表现 |
|---|---|---|---|
| 缓存 | `IDistributedCache`（框架缓存/对象缓存） | 可重建，无强一致要求 | 键缺失按需重建，无损 |
| 协调 | 分布式锁、DataProtection 密钥环、SignalR backplane、（Quartz 在 PG 不在 Redis） | 单一事实来源 | 异步复制窗口内最近写入可能丢，见 §4.5 |

推荐起步形态：**B 机房纯热备**——正常态不承接业务流量，B 省实例不挂负载或仅做探活；
切换时整体接管（§4）。"B 机房承接读流量"（就近读）需要应用侧读写分离支持
（读本省备库 = stale read、缓存写跨机房），当前代码未做读写分离，属有意的暂不做——
真有就近读需求时再立项，勿在无需求时预建。

## 4. 机房级故障切换 Runbook

前置判断：A 机房是 PG 故障、Redis 故障还是整机故障——**PG 与 Redis 可独立切换**，
不必整机故障才动作。

1. **切流量**：DNS/GSLB 把业务域名指向 B 机房。issuer 不变（铁律 1），已签发 token 继续有效。
2. **PG 提升**：备库 `pg_ctl promote`（或 patroni 等自动提升）；`pg-primary` 别名改指
   B 省新主。**旧主恢复后禁止直接写**（timeline 已分叉），需 `pg_rewind` 或重建为新备库。
3. **Redis 提升**：从库 `REPLICAOF NO ONE`；`redis-primary` 别名改指。
4. **应用实例**：别名已改则滚动重启 B 机房实例即完成切换。确认生产三件套生效
   （`SignalR:UseRedisBackplane` / `Redis:IsEnabled` / `Quartz:UsePersistentStore`，
   见 appsettings.Production.json）；Quartz 集群表在主库，随 PG 一起切换，无需单独处理。
   SignalR 客户端断线自动重连，无需干预。
5. **事后复核**（异步复制窗口的已知缺口，切完必做）：
   - **分布式锁**：窗口内最近获取的锁可能随主库数据丢失。锁有 TTL 兜底，人工确认
     长周期后台任务/作业无重复执行（Quartz 集群本身有行锁兜底，重点看自研锁场景）；
   - **DataProtection 密钥环**：同窗口丢失 = 极小概率新签 cookie 失效（重新登录，可接受）；
   - **审计核对**：对照 §2 的 lag 监控估算实际丢失窗口，业务侧知会。
6. **回切（A 机房恢复后）**：反向执行 2→3→1；A 库降级为新备、追平后选低峰切回。
   回切前确认旧主已 `pg_rewind`/重建，杜绝双写。

## 5. 备份（与复制互补，防误删防坏数据）

- 流复制**防不了**误删/坏数据（会忠实复制过去）：定期基础备份（`pg_basebackup` 或
  `pg_dump`）+ WAL 归档保留策略（建议 ≥ 7 天），恢复演练至少演练一次；
- Redis 不需要备份（缓存可重建；协调态见上）；
- **pfx 证书本体必须离线备份**：丢失 = 全员重新登录（签名密钥更换，存量签名验证失败），
  且无法从任何运行环境恢复。

## 6. 升级路径：方案一 → 方案二（按租户分区多活）

方案一的 B 省备库 = 方案二的 B 省主库，升级是"转正"不是"新建"：

1. **租户归属标记**（本仓库已内置）：租户 `ExtraProperties` 的
   `AbpAdminTenantConsts.RegionPropertyName`（新建租户默认写 `DefaultRegion`；读侧
   `TenantRegionExtensions.GetRegion` 缺省回落，存量租户无需迁移）。标记只存实体、
   不进解析缓存——方案二的运行期路由读取点本就是每租户连接串。
2. **B 备库转正**：按归属把 B 省租户的每租户连接串指向 B 省库——TenantManagement
   的连接串管理 + `TenantConnectionStringProtector` 加密存储 +
   `TenantDatabaseMigrationNeededEto` 运行期迁移事件全链路已就绪（见
   TenantAppService 连接串端点）。
3. **复制收缩**：停止 B 库的全量流复制，改为 PG 逻辑复制只同步全局小表（租户注册表、
   权限定义、设置等）——表级 publication，PG 原生能力。
4. **逐租户搬迁**（可选平滑路线）：一次迁一个租户——维护窗口内按 TenantId 抽数到 B 库 +
   改该租户连接串，不必一刀切。
5. **Redis 拆分原则**：缓存各机房本地一份（读本省、可重建）；协调类（锁/密钥环/
   backplane）跟随写入侧机房。

业务大表按归属分区后每片只有一个写入点，从根上消除双写冲突——这是方案二相对
"PG 双主 + Redis CRDT 双活"的根本优势，也是不推荐后者的原因。
