using Xunit;

// 共享 PG 库靠 Respawn 清表隔离：程序集内用例串行（同 Ordering.Tests）。并发用例在单个用例内部自己起多 UoW。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
