using System;

namespace AbpAdmin.Data;

/// <summary>
/// 请求为租户建库并迁移 schema（T2.8 SaaS Pro 缺口：运行期给租户配独立连接串后，
/// 此前库/schema 都不存在，租户请求直接失败，只能离线重跑 DbMigrator）。
/// 语义对齐 Pro SaaS 的 ApplyDatabaseMigrationsEto 触发路径（连接串变更 → 请求迁移）。
///
/// 必须经 IDistributedEventBus 发布（当前配置下回落进程内 LocalDistributedEventBus）：
/// 它缓冲到 UoW 提交后投递，处理器读到的是已提交的新连接串、缓存也已完成失效；
/// 经 ILocalEventBus 发布则请求未提交时即执行，会读到旧值。
/// </summary>
public class TenantDatabaseMigrationNeededEto
{
    public Guid TenantId { get; set; }
}
