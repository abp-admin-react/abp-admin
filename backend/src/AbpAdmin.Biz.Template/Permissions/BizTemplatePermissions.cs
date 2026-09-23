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
}
