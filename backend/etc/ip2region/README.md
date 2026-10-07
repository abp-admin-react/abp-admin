# IP 归属地离线库（ip2region）

操作日志 / 审计日志列表的 IP 归属地展示使用 [ip2region](https://github.com/lionsoul2014/ip2region)（Apache-2.0）离线库，无外部网络调用。

## 启用步骤

1. 数据文件已随仓库提交（基础数据，克隆即用）：`backend/etc/ip2region/ip2region_v4.xdb`（IPv4，
   约 11 MB）与 `ip2region_v6.xdb`（IPv6，约 37 MB）。上游 2025-09 起 data 目录拆分为这两份，
   旧的合并版 `ip2region.xdb` 已不再发布。

2. 数据会过期；需要更新时从上游重新下载并同名覆盖、随代码一起提交：

   - IPv4: <https://github.com/lionsoul2014/ip2region/blob/master/data/ip2region_v4.xdb>
   - IPv6: <https://github.com/lionsoul2014/ip2region/blob/master/data/ip2region_v6.xdb>
   - Gitee 镜像: <https://gitee.com/lionsoul/ip2region/blob/master/data/>

3. 配置 `IpRegion:DbPath`（IPv4）与 `IpRegion:DbPathV6`（IPv6）（Host `appsettings.json` 默认已指向
   `../../etc/ip2region/` 下两个文件，相对路径按应用 ContentRoot 解析；生产部署请按实际部署位置
   改为绝对路径或环境变量）。

## 行为说明

- 单栈文件缺失 / 未配置 / 加载失败：只降级该栈（该栈查询返回 null），另一栈照常工作，
  **不影响应用启动**；两栈全缺时功能整体关闭（启动时 Info 一次）。
- xdb 以 `CachePolicy.Content` 全量驻留内存（v4 约 11 MB、v6 约 37 MB），加载后查询线程安全、纳秒级。
- 按查询 IP 的 AddressFamily 自动分流：IPv4 → v4 库、IPv6 → v6 库；IPv4-mapped IPv6
  （`::ffff:a.b.c.d`，手机经 NAT64/DNS64 访问时日志里的常见形态）归 v4 库。
- 解析结果按 IP 缓存 7 天（`IpLocationCacheItem`，跨租户共享）；内网/回环地址（含 IPv6
  链路本地 fe80::/10 与唯一本地 fc00::/7）直接显示「内网IP」，不打库。
- 上游新数据的语言为混合制：中国 IP 返回中文（`中国|江苏省|南京市|0|CN`）、海外 IP 返回
  英文（`United States|California|0|Google LLC|US`），`IpLocationResolver.FormatRegion`
  按 `|` 分段拼展示串，与语言无关。
- xdb 数据文件随仓库提交（基础数据，克隆即用）；数据会过期，更新时从上游重新下载覆盖
  并提交，重启应用生效。
