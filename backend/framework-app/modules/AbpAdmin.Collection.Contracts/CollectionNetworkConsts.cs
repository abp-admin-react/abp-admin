namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// BSC 网络名常量（DB ColNetworks.Name 事实源头的同值镜像）。放契约区做单一出处：
/// Ordering 只引用契约工程、却要把 PayNetwork 写进订单与 Collection 的网络行关联——
/// 常量分居两个实现工程时，改一处漏一处即静默分叉。
/// 注意：改名等于数据迁移，必须同步 ColNetworks 种子与既有订单行，不能只改常量。
/// </summary>
public static class CollectionNetworkConsts
{
    public const string BscNetworkName = "BSC";
}
