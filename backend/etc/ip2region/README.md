# IP 归属地离线库（ip2region）

操作日志 / 审计日志列表的 IP 归属地展示使用 [ip2region](https://github.com/lionsoul2014/ip2region)（Apache-2.0）离线库，无外部网络调用。

## 启用步骤

1. 下载 xdb 数据文件（约 11 MB，IPv4+IPv6）：

   - GitHub: <https://github.com/lionsoul2014/ip2region/blob/master/data/ip2region.xdb>
   - Gitee 镜像: <https://gitee.com/lionsoul/ip2region/blob/master/data/ip2region.xdb>

2. 放置为本目录下的 `ip2region.xdb`（`backend/etc/ip2region/ip2region.xdb`）。

3. 配置 `IpRegion:DbPath`（Host `appsettings.json` 默认已指向 `../../etc/ip2region/ip2region.xdb`，
   相对路径按应用 ContentRoot 解析；生产部署请按实际部署位置改为绝对路径或环境变量）。

## 行为说明

- 库文件缺失 / 未配置 / 加载失败：功能整体禁用（启动时告警一次），归属地列显示为空，**不影响应用启动**。
- xdb 以 `CachePolicy.Content` 全量驻留内存（约 11 MB），加载后查询线程安全、纳秒级。
- 解析结果按 IP 缓存 7 天（`IpLocationCacheItem`，跨租户共享）；内网/回环地址直接显示「内网IP」。
- xdb 数据文件不随仓库提交（数据会过期，按需更新即可；重新下载覆盖后重启应用生效）。
