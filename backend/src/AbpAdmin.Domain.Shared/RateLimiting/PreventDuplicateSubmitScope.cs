namespace AbpAdmin.RateLimiting;

/// <summary>
/// 防重复提交的幂等键作用域（对标 ruoyi-vue-pro @Idempotent 的三种 KeyResolver）。
/// 刻意闭集：作用域组合只是字符串拼接，不需要 DI 扩展点；将来若需要按客户端 IP 等
/// 环境维度分桶，应参考限流模块的分区键解析器（PartitionKeyResolvers/），而不是往本枚举加成员。
/// </summary>
public enum PreventDuplicateSubmitScope
{
    /// <summary>
    /// 用户级（对标 UserIdempotentKeyResolver）：当前用户 + 方法 + 参数。
    /// 默认值；不同用户互不影响。要求调用方已认证——未登录调用全部折叠进同一个
    /// anonymous 桶（匿名端请改用 Global 或 Argument，否则可被同参数刷请求占窗干扰）。
    /// </summary>
    User = 0,

    /// <summary>
    /// 全局级（对标 DefaultIdempotentKeyResolver）：方法 + 参数，不含用户。
    /// 注意缓存键带 ABP 租户前缀，多租户下"全局"指当前租户内全局，跨租户互不影响。
    /// 适合匿名可调、且租户内只允许一个实例在飞的入口（如支付回调）。
    /// </summary>
    Global = 1,

    /// <summary>
    /// 业务键级（对标 ExpressionIdempotentKeyResolver）：方法 + 指定参数名的实参值，不含用户。
    /// 适合"同一业务单号只处理一次"的场景（跨用户/回调方互斥），需同时设置 <c>ArgumentName</c>；
    /// 实参为 null 会在拦截时抛 ArgumentException。
    /// </summary>
    Argument = 2,
}
