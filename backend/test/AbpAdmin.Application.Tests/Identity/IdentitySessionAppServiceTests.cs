using System;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Identity;

public abstract class IdentitySessionAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    [Fact]
    public async Task Should_List_Session_After_Create()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var appService = GetRequiredService<IIdentitySessionAppService>();

        await WithUnitOfWorkAsync(async () =>
        {
            await manager.CreateAsync(
                AdminUserId,
                Guid.NewGuid().ToString("N"),
                "Web",
                "test-agent",
                "AbpAdmin_App",
                "127.0.0.1");
        });

        var list = await appService.GetListAsync(new GetIdentitySessionListInput
        {
            UserId = AdminUserId,
            MaxResultCount = 20
        });

        list.TotalCount.ShouldBeGreaterThan(0);
        list.Items.ShouldContain(x => x.UserId == AdminUserId);
    }

    [Fact]
    public async Task Should_Revoke_Session()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var appService = GetRequiredService<IIdentitySessionAppService>();
        var sessionId = Guid.NewGuid().ToString("N");

        var session = await WithUnitOfWorkAsync(() =>
            manager.CreateAsync(AdminUserId, sessionId, "Web", null, "AbpAdmin_App", "127.0.0.1"));

        await appService.RevokeAsync(session.Id);

        (await manager.FindBySessionIdAsync(sessionId)).ShouldBeNull();
    }

    [Fact]
    public async Task Manager_Find_Should_Work_Without_Ambient_UnitOfWork()
    {
        // round4 实证锚（function F2）：IdentitySessionValidationMiddleware 在 UseUnitOfWork
        // 之后调用 FindBySessionIdAsync/TouchIfStaleAsync，那个位置没有环境 UoW（Reserve 未 Begin）。
        // 管理器若不受 UnitOfWork 拦截保护，裸调用会抛
        // "A DbContext can only be created inside a unit of work!"（每个带 session_id 的请求 500）。
        // 中间件现已自带 requiresNew UoW（不依赖拦截兜底），本用例锚定管理器裸调用的行为基线：
        // 将来 ABP 升级改变约定拦截时，这里会先红，暴露对隐式拦截的依赖已断。
        var manager = GetRequiredService<IdentitySessionManager>();

        // 不包 WithUnitOfWorkAsync：无环境 UoW 下直接查库
        var session = await manager.FindBySessionIdAsync("no-ambient-uow-probe");
        session.ShouldBeNull();
    }

    #region 按用户吊销全部会话

    [Fact]
    public async Task RevokeAllByUserAsync_Should_Revoke_Only_That_Users_Sessions()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var appService = GetRequiredService<IIdentitySessionAppService>();
        var otherUserId = Guid.NewGuid();

        var adminSessions = new[]
        {
            await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1")),
            await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Api", null, "AbpAdmin_App", "127.0.0.1"))
        };
        var otherSession = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            otherUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

        try
        {
            await appService.RevokeAllByUserAsync(AdminUserId);

            foreach (var session in adminSessions)
            {
                (await manager.FindBySessionIdAsync(session.SessionId)).ShouldBeNull();
            }
            // 其它用户会话不受影响
            (await manager.FindBySessionIdAsync(otherSession.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(otherUserId));
        }
    }

    /// <summary>无会话用户调用必须幂等空操作（仓储租户过滤后为空即静默返回）。</summary>
    [Fact]
    public async Task RevokeAllByUserAsync_Should_Be_Idempotent_For_Unknown_User()
    {
        var appService = GetRequiredService<IIdentitySessionAppService>();
        await appService.RevokeAllByUserAsync(Guid.NewGuid());
    }

    #endregion

    #region 防并发登录（对标 ABP Identity Pro Prevent Concurrent Login）

    [Fact]
    public async Task CreateAsync_Should_Keep_All_Sessions_By_Default()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        await settingManager.SetGlobalAsync(AbpAdminSettings.Account.PreventConcurrentLoginMode, "Disabled");

        var first = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
        var second = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

        try
        {
            (await manager.FindBySessionIdAsync(first.SessionId)).ShouldNotBeNull();
            (await manager.FindBySessionIdAsync(second.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    /// <summary>脏值（手改库/迁移/绕过 UI 写设置）必须按 Disabled 回退且有告警可查，不得抛异常。</summary>
    [Fact]
    public async Task CreateAsync_Should_Treat_Invalid_Setting_Value_As_Disabled()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        await settingManager.SetGlobalAsync(AbpAdminSettings.Account.PreventConcurrentLoginMode, "Bogus");

        var first = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
        var second = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

        try
        {
            (await manager.FindBySessionIdAsync(first.SessionId)).ShouldNotBeNull();
            (await manager.FindBySessionIdAsync(second.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await settingManager.SetGlobalAsync(
                AbpAdminSettings.Account.PreventConcurrentLoginMode, "Disabled");
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Logout_All_Devices_When_Configured()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        await settingManager.SetGlobalAsync(
            AbpAdminSettings.Account.PreventConcurrentLoginMode, "LogoutFromAllDevices");

        try
        {
            var first = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
            var second = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Api", null, "AbpAdmin_App", "127.0.0.1"));

            // 新会话建立时，其它设备会话被踢
            (await manager.FindBySessionIdAsync(first.SessionId)).ShouldBeNull();
            (await manager.FindBySessionIdAsync(second.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await settingManager.SetGlobalAsync(
                AbpAdminSettings.Account.PreventConcurrentLoginMode, "Disabled");
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    [Fact]
    public async Task CreateAsync_Should_Logout_Only_Same_Device_Type_When_Configured()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        await settingManager.SetGlobalAsync(
            AbpAdminSettings.Account.PreventConcurrentLoginMode, "LogoutFromSameTypeDevices");

        try
        {
            var webFirst = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
            var apiSession = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Api", null, "AbpAdmin_App", "127.0.0.1"));
            var webSecond = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

            // 同为 Web 的旧会话被踢；其它设备类型保留
            (await manager.FindBySessionIdAsync(webFirst.SessionId)).ShouldBeNull();
            (await manager.FindBySessionIdAsync(apiSession.SessionId)).ShouldNotBeNull();
            (await manager.FindBySessionIdAsync(webSecond.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await settingManager.SetGlobalAsync(
                AbpAdminSettings.Account.PreventConcurrentLoginMode, "Disabled");
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    #endregion

    #region 刷新类授权的续期语义（被踢会话不得复活——六透镜审查 H1 的回归锚）

    /// <summary>行还在：Renew 刷新 LastAccessed/IP 并返回会话（refresh token 静默续期的正常路径）。</summary>
    [Fact]
    public async Task RenewAsync_Should_Touch_Existing_Session()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var session = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

        try
        {
            // 新建会话 LastAccessed 为 null（首次 touch 才写入）：Renew 后必须落值，
            // 这是最强的 touch 证明——若 Renew 回归为 no-op，CleanupWorker 会把活跃用户当僵尸删掉
            session.LastAccessed.ShouldBeNull();
            var renewed = await WithUnitOfWorkAsync(() => manager.RenewAsync(
                AdminUserId, session.SessionId, "10.0.0.9"));

            renewed.ShouldNotBeNull();
            renewed.Id.ShouldBe(session.Id);
            renewed.LastAccessed.ShouldNotBeNull();
            renewed.IpAddresses.ShouldContain("10.0.0.9");
            (await manager.FindBySessionIdAsync(session.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    /// <summary>
    /// 行已删（被吊销/互踢）：Renew 必须返回 null 且不得重建行——
    /// 否则持有 refresh token 的客户端被踢后可原地复活，吊销与互踢语义全部失效。
    /// handler 拿到 null 后不重建，旧 sessionId 留在票里，由中间件 401 逼客户端重新登录。
    /// </summary>
    [Fact]
    public async Task RenewAsync_Should_Not_Revive_Revoked_Session()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var session = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
        await WithUnitOfWorkAsync(() => manager.RevokeAsync(session.Id));

        var renewed = await WithUnitOfWorkAsync(() => manager.RenewAsync(
            AdminUserId, session.SessionId, "127.0.0.1"));

        renewed.ShouldBeNull();
        (await manager.FindBySessionIdAsync(session.SessionId)).ShouldBeNull();
    }

    /// <summary>
    /// 归属校验：票里携带的 session_id 指向他人会话时不得续期他人会话（fail-closed 与不复活同向）。
    /// </summary>
    [Fact]
    public async Task RenewAsync_Should_Not_Renew_Another_Users_Session()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var otherUserId = Guid.NewGuid();
        var session = await WithUnitOfWorkAsync(() => manager.CreateAsync(
            otherUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

        try
        {
            var renewed = await WithUnitOfWorkAsync(() => manager.RenewAsync(
                AdminUserId, session.SessionId, "127.0.0.1"));

            renewed.ShouldBeNull();
            // 他人会话原样保留（不被续期也不被删除）
            (await manager.FindBySessionIdAsync(session.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(otherUserId));
        }
    }

    #endregion

    #region 设备类型解析（三档互踢的区分轴：客户端 device 参数 → Web/Mobile）

    [Theory]
    [InlineData(null, "Web")]
    [InlineData("", "Web")]
    [InlineData("   ", "Web")]
    [InlineData("Web", "Web")]
    [InlineData("Mobile", "Mobile")]
    [InlineData("mobile", "Mobile")]
    [InlineData(" Mobile ", "Mobile")]
    [InlineData("Bogus", "Web")]
    public void DeviceResolver_Should_Fall_Back_To_Web_For_Unknown_Values(
        string? deviceParameter, string expected)
    {
        IdentitySessionDeviceResolver.Resolve(deviceParameter).ShouldBe(expected);
    }

    /// <summary>
    /// 端到端语义锚：Mobile 会话与 Web 会话在 LogoutFromSameTypeDevices 下互不影响——
    /// 此前 Device 硬编码 Web，该档位与 AllDevices 等效（遗留项修复的行为基线）。
    /// </summary>
    [Fact]
    public async Task SameType_Mode_Should_Distinguish_Mobile_From_Web()
    {
        var manager = GetRequiredService<IdentitySessionManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        await settingManager.SetGlobalAsync(
            AbpAdminSettings.Account.PreventConcurrentLoginMode, "LogoutFromSameTypeDevices");

        try
        {
            var web = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));
            var mobile = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"),
                IdentitySessionDeviceResolver.Resolve("Mobile"),
                null, "AbpAdmin_App", "127.0.0.1"));
            var webSecond = await WithUnitOfWorkAsync(() => manager.CreateAsync(
                AdminUserId, Guid.NewGuid().ToString("N"), "Web", null, "AbpAdmin_App", "127.0.0.1"));

            (await manager.FindBySessionIdAsync(web.SessionId)).ShouldBeNull("同类型 Web 互踢");
            (await manager.FindBySessionIdAsync(mobile.SessionId)).ShouldNotBeNull("Mobile 是独立设备类型，不被 Web 登录踢掉");
            (await manager.FindBySessionIdAsync(webSecond.SessionId)).ShouldNotBeNull();
        }
        finally
        {
            await settingManager.SetGlobalAsync(
                AbpAdminSettings.Account.PreventConcurrentLoginMode, "Disabled");
            await WithUnitOfWorkAsync(() => manager.RevokeAllForUserAsync(AdminUserId));
        }
    }

    #endregion
}
