using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Posts;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// 组织×岗位×角色×用户组合演示种子的结构锁（DemoIdentitySeedContributor.SeedOrgComboDemoAsync）：
/// 四个演示人物的「部门/岗位/角色来源」对照是页面问号说明的活样例，结构漂移会让说明与数据脱节。
/// 权限「来自 OU 绑定角色」的声明解析是 ABP 框架行为（登录时把 OU 角色并入 claims），
/// 这里锁数据面：绑定关系存在 + 李工无直接角色 + 张智多部门/王财复用财务角色/赵营销直接分配。
/// </summary>
public abstract class DemoOrgComboSeedTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIdentityUserRepository _users;
    private readonly IIdentityRoleRepository _roles;
    private readonly IOrganizationUnitRepository _ous;
    private readonly OrganizationUnitManager _ouManager;
    private readonly IPostRepository _posts;
    private readonly IdentityUserManager _userManager;

    /// <summary>直调种子并连跑两遍：测试宿主环境钉 Production，自动播种被门控关闭
    /// （见 LedgerTestModule 注释），幂等锁必须在用例内自证。</summary>
    private async Task SeedTwiceAsync()
    {
        // 直调不用 WithUnitOfWorkAsync 包裹：根容器解析的 contributor 与包裹 scope 的
        // DbContext 混用会让 OU 父子链在同一批未提交插入里互相不可见（实测 010010）；
        // 直调时各 manager 调用自带 UoW 逐次提交，跨调用走已提交状态——migrator 真实
        // 路径是单一环境 UoW（同上下文跟踪可见），两条路径语义一致。
        await GetRequiredService<DemoIdentitySeedContributor>().SeedOrgComboDemoAsync();
        await GetRequiredService<DemoIdentitySeedContributor>().SeedOrgComboDemoAsync();
    }

    protected DemoOrgComboSeedTests()
    {
        _users = GetRequiredService<IIdentityUserRepository>();
        _roles = GetRequiredService<IIdentityRoleRepository>();
        _ous = GetRequiredService<IOrganizationUnitRepository>();
        _ouManager = GetRequiredService<OrganizationUnitManager>();
        _posts = GetRequiredService<IPostRepository>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public async Task Org_Tree_And_Ou_Role_Binding_Should_Be_Seeded()
    {
        await SeedTwiceAsync();
        var all = await _ous.GetListAsync();
        var byName = all.ToDictionary(x => x.DisplayName);
        byName.ContainsKey("演示集团").ShouldBeTrue();
        byName.ContainsKey("研发部").ShouldBeTrue();

        var rd = byName["研发部"];
        // OU 树父子：研发部挂在技术中心下
        rd.ParentId.ShouldBe(byName["技术中心"].Id);

        // 研发部绑定了「普通员工」——李工的权限来源（组合玩法的关键演示点）
        var employee = await _roles.FindByNormalizedNameAsync("EMPLOYEE");
        employee.ShouldNotBeNull();
        var rdRoles = await _ous.GetRolesAsync(rd);
        rdRoles.ShouldContain(x => x.Id == employee.Id);
    }

    [Fact]
    public async Task Li_Dev_Has_No_Direct_Role_But_In_Rd_Ou_With_Two_Posts()
    {
        await SeedTwiceAsync();
        var li = await _users.FindByNormalizedUserNameAsync("LI.DEV");
        li.ShouldNotBeNull();

        // 仓储层 includeOURoles:false 只看直接分配——李工确实一个直接角色都没有；
        // userManager 全量角色含「普通员工」——证明 OU 绑定的角色进了用户的角色解析（教学点的行为证据）
        // ABP 10.7 仓储/管理器的角色查询都并入了 OU 角色（登录声明解析口径）——「直接分配」
        // 的判据换成按角色反查直接成员名单（GetUserIdListByRoleIdAsync 只看 UserRoles 表）：
        // 李工不在「普通员工」直接名单（他的该角色来自研发部 OU 绑定），对照组赵营销在
        var employeeRole = await _roles.FindByNormalizedNameAsync("EMPLOYEE");
        employeeRole.ShouldNotBeNull();
        var directEmployeeUserIds = await _users.GetUserIdListByRoleIdAsync(employeeRole.Id);
        directEmployeeUserIds.ShouldNotContain(li.Id, "李工无直接角色分配——employee 来自研发部 OU 绑定");
        var zhao = await _users.FindByNormalizedUserNameAsync("ZHAO.MKT");
        directEmployeeUserIds.ShouldContain(zhao.Id, "赵营销是直接分配对照组");
        (await _userManager.GetRolesAsync(li)).ShouldContain("employee", "OU 绑定角色的解析证据");

        var ousOfLi = await _userManager.GetOrganizationUnitsAsync(li);
        ousOfLi.ShouldContain(x => x.DisplayName == "研发部");

        var postsOfLi = await _posts.GetListByUserIdAsync(li.Id);
        postsOfLi.Select(x => x.Code).OrderBy(x => x).ToArray()
            .ShouldBe(new[] { "DEV", "SRE" }, "一人兼岗：开发工程师 + 运维工程师");
    }

    [Fact]
    public async Task Zhang_Multi_Ou_Wang_Reuse_Finance_Role_Zhao_Direct_Employee()
    {
        await SeedTwiceAsync();
        var zhang = await _users.FindByNormalizedUserNameAsync("ZHANG.CTO");
        zhang.ShouldNotBeNull();
        var ousOfZhang = await _userManager.GetOrganizationUnitsAsync(zhang);
        ousOfZhang.Select(x => x.DisplayName).ShouldContain("技术中心");
        ousOfZhang.Select(x => x.DisplayName).ShouldContain("研发部");
        (await _userManager.IsInRoleAsync(zhang, "dept-manager")).ShouldBeTrue();

        var wang = await _users.FindByNormalizedUserNameAsync("WANG.FM");
        wang.ShouldNotBeNull();
        (await _userManager.IsInRoleAsync(wang, "fin-accountant")).ShouldBeTrue();

        var zhao = await _users.FindByNormalizedUserNameAsync("ZHAO.MKT");
        zhao.ShouldNotBeNull();
        (await _userManager.IsInRoleAsync(zhao, "employee")).ShouldBeTrue();
    }
}
