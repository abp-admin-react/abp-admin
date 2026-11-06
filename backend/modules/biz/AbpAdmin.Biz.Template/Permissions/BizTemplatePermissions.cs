namespace AbpAdmin.Biz.Template.Permissions;

/// <summary>模块自带权限组，与框架权限组（AbpAdminPermissions）互不混杂。</summary>
public static class BizTemplatePermissions
{
    public const string GroupName = "BizTemplate";

    public static class Projects
    {
        public const string Default = GroupName + ".Projects";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
    }

    /// <summary>数据权限示例（消费框架数据范围基建的最小业务面）</summary>
    public static class DataScopeDemo
    {
        public const string Default = GroupName + ".DataScopeDemo";
    }

    /// <summary>
    /// 客户错误协议接入样例。基线不向任何角色播种；仅当 EnableClientErrorSample=true
    /// 时由种子贡献者授给 admin，供本机联调。
    /// </summary>
    public static class ClientErrorSample
    {
        public const string Default = GroupName + ".ClientErrorSample";
    }
}
