using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Localization;
using MiniExcelLibs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Identity;
using Volo.Abp.Identity.Settings;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Identity;

/* T2.6 Identity Pro 缺口：用户导入导出 + 周期改密集成测试。
 * 对应 03-batch2-pro-parity.md T2.6 验收标准：
 * - 导入的部分成功语义（逐行独立 UoW，成功行真实落库，失败行进明细文件）；
 * - 导入模板列名与解析列名同源（UserImportColumnNames）：直接按模板格式构造文件导入；
 * - 失败明细文件可解析，含原始行内容与具体失败原因；
 * - 周期改密：ShouldPeriodicallyChangePasswordAsync 的 未开启 / 0 值 / 过期 / 未过期 / 外部用户 五种情况。
 *
 * 导入导出用例原在 IdentityUserAdminAppServiceTests，按规格要求归拢到本文件。
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)。
 */
public abstract class UserImportExportTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIdentityUserAdminAppService _identityUserAdminAppService;
    private readonly IdentityUserManager _userManager;
    private readonly ISettingManager _settingManager;
    private readonly IBlobContainer<UserExportBlobContainer> _blobContainer;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    protected UserImportExportTests()
    {
        _identityUserAdminAppService = GetRequiredService<IIdentityUserAdminAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _settingManager = GetRequiredService<ISettingManager>();
        _blobContainer = GetRequiredService<IBlobContainer<UserExportBlobContainer>>();
        // 导入错误文案已本地化（重构报告问题 9），断言走同一本地化管线，不依赖宿主文化
        _localizer = GetRequiredService<IStringLocalizer<AbpAdminResource>>();
    }

    /// <summary>
    /// 创建测试用户并返回用户 ID。
    /// </summary>
    private async Task<Guid> CreateTestUserAsync(string userName, string email, string password)
    {
        var user = new IdentityUser(
            Guid.NewGuid(),
            userName,
            email);

        var result = await _userManager.CreateAsync(user, password);
        result.Succeeded.ShouldBeTrue(
            string.Join("; ", result.Errors.Select(e => e.Description)));

        return user.Id;
    }

    /// <summary>
    /// 清理测试用户。
    /// </summary>
    private async Task CleanupTestUserAsync(Guid userId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        });
    }

    /// <summary>
    /// 创建包含用户导入数据的 Excel 流（DTO 序列化，表头即 [ExcelColumnName] 中文列名）。
    /// </summary>
    private static MemoryStream CreateImportExcelStream(params UserImportRowDto[] rows)
    {
        var stream = new MemoryStream();
        MiniExcelLibs.MiniExcel.SaveAs(stream, rows);
        stream.Position = 0;
        return stream;
    }

    #region 导入测试

    [Fact]
    public async Task ImportAsync_Should_Import_Valid_User()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-user1",
                Email = "import-test1@test.com",
                Name = "Test",
                Surname = "User",
                Password = "Test@123456",
                IsActive = "是",
                IsExternal = "否"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);
        result.Errors.ShouldBeEmpty();
        result.FailureReportId.ShouldBeNull();

        // 验证用户已创建
        var user = await _userManager.FindByNameAsync("import-test-user1");
        user.ShouldNotBeNull();
        user.Email.ShouldBe("import-test1@test.com");
        user.Name.ShouldBe("Test");
        user.Surname.ShouldBe("User");
        user.IsActive.ShouldBeTrue();

        // Cleanup
        await CleanupTestUserAsync(user.Id);
    }

    /// <summary>
    /// 模板列名与解析列名同源验收：按导入模板的实际格式（UserImportColumnNames 中文表头 +
    /// 布尔列布尔值，与 UserExportController.GetImportTemplate 完全一致）构造文件，导入必须成功。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Should_Accept_Template_Format_File()
    {
        // Arrange - 与 UserExportController.GetImportTemplate 相同的构造方式
        var userName = $"import-tpl-{Guid.NewGuid():N}"[..20];
        var templateRows = new List<Dictionary<string, object>>
        {
            new()
            {
                [UserImportColumnNames.UserName] = userName,
                [UserImportColumnNames.Name] = "三",
                [UserImportColumnNames.Surname] = "张",
                [UserImportColumnNames.Email] = $"{userName}@test.com",
                [UserImportColumnNames.PhoneNumber] = "13800138000",
                [UserImportColumnNames.Password] = "P@ssw0rd123",
                [UserImportColumnNames.RoleNames] = "",
                [UserImportColumnNames.OrganizationUnitCodes] = "",
                [UserImportColumnNames.IsActive] = true,
                [UserImportColumnNames.IsExternal] = false
            }
        };

        using var stream = new MemoryStream();
        MiniExcelLibs.MiniExcel.SaveAs(stream, templateRows);
        stream.Position = 0;
        var content = new RemoteStreamContent(stream, "template.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);

        var user = await _userManager.FindByNameAsync(userName);
        user.ShouldNotBeNull();

        // Cleanup
        await CleanupTestUserAsync(user.Id);
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_UserName_Is_Empty()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "",
                Email = "import-test2@test.com",
                Password = "Test@123456",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:UserNameRequired", 1].Value);
        result.FailureReportId.ShouldNotBeNull();
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_Email_Is_Empty()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-user3",
                Email = "",
                Password = "Test@123456",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:EmailRequired", 1].Value);
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_UserName_Exists()
    {
        // Arrange - 先创建一个用户
        var existingUserId = await CreateTestUserAsync("import-test-dup", "import-dup@test.com", "Test@123456");

        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-dup",
                Email = "import-dup2@test.com",
                Password = "Test@123456",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:DuplicatedUserName", 1, "import-test-dup"].Value);

        // Cleanup
        await CleanupTestUserAsync(existingUserId);
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_Email_Exists()
    {
        // Arrange - 先创建一个用户
        var existingUserId = await CreateTestUserAsync("import-test-dup2", "import-dup2@test.com", "Test@123456");

        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-dup3",
                Email = "import-dup2@test.com",
                Password = "Test@123456",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:DuplicatedEmail", 1, "import-dup2@test.com"].Value);

        // Cleanup
        await CleanupTestUserAsync(existingUserId);
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_Local_User_Has_No_Password()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-nopw",
                Email = "import-nopw@test.com",
                Password = "", // 本地用户无密码
                IsActive = "是",
                IsExternal = "否"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:PasswordRequiredForLocalUser", 1].Value);
    }

    [Fact]
    public async Task ImportAsync_Should_Succeed_For_External_User_Without_Password()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-external",
                Email = "import-external@test.com",
                Password = "", // 外部用户无密码
                IsActive = "是",
                IsExternal = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);

        // 验证用户已创建且为外部用户（IsExternal = true 且无密码哈希）
        var user = await _userManager.FindByNameAsync("import-test-external");
        user.ShouldNotBeNull();
        user.IsExternal.ShouldBeTrue();
        user.PasswordHash.ShouldBeNull();

        // Cleanup
        await CleanupTestUserAsync(user.Id);
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_Role_Not_Exists()
    {
        // Arrange
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-role",
                Email = "import-role@test.com",
                Password = "Test@123456",
                RoleNames = "NonExistentRole",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(0);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(_localizer["UserImport:RoleNotFound", 1, "NonExistentRole"].Value);
    }

    [Fact]
    public async Task ImportAsync_Should_Support_Partial_Success()
    {
        // Arrange - 3 行数据，第 2 行失败
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-partial1",
                Email = "import-partial1@test.com",
                Password = "Test@123456",
                IsActive = "true"
            },
            new UserImportRowDto
            {
                UserName = "", // 用户名为空，应失败
                Email = "import-partial2@test.com",
                Password = "Test@123456",
                IsActive = "true"
            },
            new UserImportRowDto
            {
                UserName = "import-test-partial3",
                Email = "import-partial3@test.com",
                Password = "Test@123456",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(3);
        result.SucceededCount.ShouldBe(2);
        result.FailedCount.ShouldBe(1);
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].RowNumber.ShouldBe(2);
        result.FailureReportId.ShouldNotBeNull();

        // 验证成功行确实提交到了数据库（不是整体回滚）
        var user1 = await _userManager.FindByNameAsync("import-test-partial1");
        user1.ShouldNotBeNull();
        var user3 = await _userManager.FindByNameAsync("import-test-partial3");
        user3.ShouldNotBeNull();

        // Cleanup
        await CleanupTestUserAsync(user1.Id);
        await CleanupTestUserAsync(user3.Id);
    }

    /// <summary>
    /// 失败明细文件：可下载、含失败行的原始内容与具体失败原因。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Failure_Report_Should_Contain_Original_Rows_And_Reasons()
    {
        // Arrange - 第 1 行用户名为空，第 2 行成功，第 3 行角色不存在
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "",
                Email = "import-report1@test.com",
                Password = "Test@123456",
                IsActive = "true"
            },
            new UserImportRowDto
            {
                UserName = "import-report-ok",
                Email = "import-report-ok@test.com",
                Password = "Test@123456",
                IsActive = "true"
            },
            new UserImportRowDto
            {
                UserName = "import-report-role",
                Email = "import-report3@test.com",
                Password = "Test@123456",
                RoleNames = "NonExistentRole",
                IsActive = "true"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var content = new RemoteStreamContent(stream, "test.xlsx");

        // Act
        var result = await _identityUserAdminAppService.ImportAsync(content);

        // Assert
        result.TotalCount.ShouldBe(3);
        result.SucceededCount.ShouldBe(1);
        result.FailedCount.ShouldBe(2);
        result.FailureReportId.ShouldNotBeNull();

        // 下载并解析失败明细文件
        var bytes = await _blobContainer.GetAllBytesAsync(result.FailureReportId!.Value.ToString());
        using var reportStream = new MemoryStream(bytes);
        var reportRows = reportStream.Query(useHeaderRow: true)
            .Cast<IDictionary<string, object>>()
            .ToList();

        reportRows.Count.ShouldBe(2);
        foreach (var reportRow in reportRows)
        {
            reportRow.Keys.ShouldContain("失败原因");
            reportRow["失败原因"].ToString().ShouldNotBeNullOrEmpty();
            reportRow.Keys.ShouldContain("邮箱");
        }
        // 原始行内容保留
        reportRows.Select(x => x["邮箱"]?.ToString()).ShouldContain("import-report1@test.com");
        reportRows.Select(x => x["邮箱"]?.ToString()).ShouldContain("import-report3@test.com");
        // 失败原因具体（非原始异常消息）
        reportRows.First(x => x["邮箱"]?.ToString() == "import-report1@test.com")
            ["失败原因"].ToString()!.ShouldContain(_localizer["UserImport:UserNameRequired", 1].Value);
        reportRows.First(x => x["邮箱"]?.ToString() == "import-report3@test.com")
            ["失败原因"].ToString()!.ShouldContain(_localizer["UserImport:RoleNotFound", 3, "NonExistentRole"].Value);

        // Cleanup
        var okUser = await _userManager.FindByNameAsync("import-report-ok");
        okUser.ShouldNotBeNull();
        await CleanupTestUserAsync(okUser.Id);
    }

    /// <summary>
    /// 模板构建契约（对标 ruoyi 下拉模板）：
    /// 表头与 UserImportColumnNames 同源、隐藏「参考数据」sheet（角色/组织单元参考值），
    /// 布尔列硬校验（是/否）、角色/组织单元咨询式下拉（可多值手输，不弹错）。
    /// </summary>
    [Fact]
    public async Task Import_Template_Should_Contain_Dropdowns_And_Reference_Sheet()
    {
        var builder = GetRequiredService<IUserImportTemplateBuilder>();
        var bytes = await builder.BuildAsync();

        using var ms = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(ms);
        var sheet = workbook.Worksheet("用户导入");

        // 表头同源（列名常量驱动，防模板与解析漂移）
        sheet.Cell(1, 1).GetString().ShouldBe(UserImportColumnNames.UserName);
        sheet.Cell(1, 7).GetString().ShouldBe(UserImportColumnNames.RoleNames);
        sheet.Cell(1, 8).GetString().ShouldBe(UserImportColumnNames.OrganizationUnitCodes);
        sheet.Cell(1, 9).GetString().ShouldBe(UserImportColumnNames.IsActive);
        sheet.Cell(1, 10).GetString().ShouldBe(UserImportColumnNames.IsExternal);

        // 示例行布尔列为「是/否」文本（与导出端字典渲染对称）
        sheet.Cell(2, 9).GetString().ShouldBe("是");
        sheet.Cell(2, 10).GetString().ShouldBe("否");

        // 隐藏参考数据 sheet：角色参考值来自当前租户真实角色
        var reference = workbook.Worksheet("参考数据");
        reference.Visibility.ShouldBe(ClosedXML.Excel.XLWorksheetVisibility.Hidden);
        reference.Cell(1, 1).GetString().ShouldBe("可选角色");
        var roles = await WithUnitOfWorkAsync(() => GetRequiredService<IIdentityRoleRepository>().GetListAsync());
        roles.ShouldNotBeEmpty();
        var firstRoleName = roles.Select(r => r.Name!).First();
        var referenceRoles = new List<string>();
        var row = 2;
        while (!string.IsNullOrEmpty(reference.Cell(row, 1).GetString()))
        {
            referenceRoles.Add(reference.Cell(row, 1).GetString());
            row++;
        }
        referenceRoles.ShouldContain(firstRoleName);

        // 数据验证：布尔列 2 个硬校验（弹错），角色/组织单元 2 个咨询式（不弹错）。
        // 校验范围按列聚合（列字母 → 是否弹错）；相邻同规则列可能被合并成一个矩形范围，逐列展开。
        var columnErrorBehavior = new Dictionary<string, bool>();
        foreach (var validation in sheet.DataValidations)
        {
            foreach (var range in validation.Ranges)
            {
                for (var column = range.RangeAddress.FirstAddress.ColumnNumber;
                     column <= range.RangeAddress.LastAddress.ColumnNumber;
                     column++)
                {
                    columnErrorBehavior[sheet.Cell(1, column).Address.ColumnLetter] = validation.ShowErrorMessage;
                }
            }
        }

        columnErrorBehavior[sheet.Cell(1, 9).Address.ColumnLetter].ShouldBeTrue();   // 是否启用：硬校验
        columnErrorBehavior[sheet.Cell(1, 10).Address.ColumnLetter].ShouldBeTrue();  // 是否外部用户：硬校验
        columnErrorBehavior[sheet.Cell(1, 7).Address.ColumnLetter].ShouldBeFalse();  // 角色：咨询式
        columnErrorBehavior[sheet.Cell(1, 8).Address.ColumnLetter].ShouldBeFalse();  // 组织单元：咨询式
    }

    /// <summary>
    /// 布尔列内联列表必须是带引号的字符串字面量（OOXML 要求，Excel 自身生成 "是,否"）：
    /// 不加引号会被 Excel 当非法公式在加载时丢弃整个验证——下拉与拦截全部失效
    /// （审查实测回归）。ClosedXML 读回不保留引号语义差异，直接解包断言原始 XML。
    /// </summary>
    [Fact]
    public async Task Import_Template_Inline_List_Must_Be_Quoted_In_Raw_Xml()
    {
        var builder = GetRequiredService<IUserImportTemplateBuilder>();
        var bytes = await builder.BuildAsync();

        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
        var sheetXml = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml"))
            .Select(e => new { e.FullName, Content = new StreamReader(e.Open()).ReadToEnd() })
            .FirstOrDefault(x => x.Content.Contains("dataValidation"))
            .ShouldNotBeNull();

        // 原始 XML 里出现带引号的字面量（不依赖命名空间前缀的写法）；
        // 未加引号时这里是裸的 是,否，断言不匹配
        sheetXml.Content.ShouldContain($"\"{UserImportBooleanTexts.Yes},{UserImportBooleanTexts.No}\"");
    }

    /// <summary>行数硬上限：超出直接拒绝（防高压缩比 xlsx 的解压放大资源耗尽）。</summary>
    [Fact]
    public async Task ImportAsync_Should_Reject_Too_Many_Rows()
    {
        var rows = Enumerable.Range(0, AbpAdmin.Account.AbpAdminAccountConsts.ImportMaxRowCount + 1)
            .Select(i => new UserImportRowDto
            {
                UserName = $"row-cap-{i}",
                Email = $"row-cap-{i}@test.com",
                Password = "Test@123456",
                IsActive = "是",
            })
            .ToArray();

        using var stream = CreateImportExcelStream(rows);

        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _identityUserAdminAppService.ImportAsync(new RemoteStreamContent(stream, "too-many.xlsx")));
        exception.Message.ShouldContain(_localizer["UserImport:TooManyRows", AbpAdmin.Account.AbpAdminAccountConsts.ImportMaxRowCount].Value);
    }

    /// <summary>布尔文本列同义词与默认值：是/TRUE/1/空(默认启用)/停用。</summary>
    [Fact]
    public async Task ImportAsync_Should_Accept_Boolean_Synonyms_And_Defaults()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var rows = new[]
        {
            new UserImportRowDto { UserName = $"syn-yes-{suffix}", Email = $"syn-yes-{suffix}@test.com", Password = "Test@123456", IsActive = "是" },
            new UserImportRowDto { UserName = $"syn-true-{suffix}", Email = $"syn-true-{suffix}@test.com", Password = "Test@123456", IsActive = "TRUE" },
            new UserImportRowDto { UserName = $"syn-one-{suffix}", Email = $"syn-one-{suffix}@test.com", Password = "Test@123456", IsActive = "1" },
            new UserImportRowDto { UserName = $"syn-empty-{suffix}", Email = $"syn-empty-{suffix}@test.com", Password = "Test@123456", IsActive = null }, // 空默认启用
            new UserImportRowDto { UserName = $"syn-no-{suffix}", Email = $"syn-no-{suffix}@test.com", Password = "Test@123456", IsActive = "停用" },
        };

        using var stream = CreateImportExcelStream(rows);
        var result = await _identityUserAdminAppService.ImportAsync(new RemoteStreamContent(stream, "test.xlsx"));

        result.SucceededCount.ShouldBe(5);
        result.FailedCount.ShouldBe(0);

        (await _userManager.FindByNameAsync($"syn-yes-{suffix}"))!.IsActive.ShouldBeTrue();
        (await _userManager.FindByNameAsync($"syn-true-{suffix}"))!.IsActive.ShouldBeTrue();
        (await _userManager.FindByNameAsync($"syn-one-{suffix}"))!.IsActive.ShouldBeTrue();
        // 空值取默认（启用），与旧 bool 模板 default true 语义一致
        (await _userManager.FindByNameAsync($"syn-empty-{suffix}"))!.IsActive.ShouldBeTrue();
        (await _userManager.FindByNameAsync($"syn-no-{suffix}"))!.IsActive.ShouldBeFalse();

        foreach (var name in new[] { "syn-yes", "syn-true", "syn-one", "syn-empty", "syn-no" })
        {
            var user = await _userManager.FindByNameAsync($"{name}-{suffix}");
            await CleanupTestUserAsync(user.Id);
        }
    }

    /// <summary>非法布尔值 → 行级友好错误（Excel 下拉可被粘贴绕过，服务端是最终闸门）。</summary>
    [Fact]
    public async Task ImportAsync_Should_Fail_With_Friendly_Error_For_Invalid_Boolean()
    {
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = "import-test-invalidbool",
                Email = "import-invalidbool@test.com",
                Password = "Test@123456",
                IsActive = "maybe"
            }
        };

        using var stream = CreateImportExcelStream(rows);
        var result = await _identityUserAdminAppService.ImportAsync(new RemoteStreamContent(stream, "test.xlsx"));

        result.FailedCount.ShouldBe(1);
        result.Errors[0].ErrorMessage.ShouldContain(
            _localizer["UserImport:InvalidBoolean", 1, UserImportColumnNames.IsActive, "maybe"].Value);
    }

    #endregion

    #region 导出测试

    [Fact]
    public async Task EnqueueExportAsync_Should_Return_Queued_Status()
    {
        // Act
        var result = await _identityUserAdminAppService.EnqueueExportAsync(null);

        // Assert
        result.ShouldNotBeNull();
        result.IsQueued.ShouldBeTrue();
        result.Message.ShouldNotBeNullOrEmpty();
        result.Message.ShouldContain("已排队");
    }

    /// <summary>
    /// OU 列回归（代码审查 round1 修复）：用户查询改 includeDetails:false 后，
    /// OU 列由 IdentityUserOrganizationUnit 连接表批量查询提供——导出必须仍带出 OU 显示名。
    /// </summary>
    [Fact]
    public async Task BuildAsync_Should_Export_OrganizationUnit_DisplayNames()
    {
        var userName = $"export-ou-{Guid.NewGuid().ToString("N")[..8]}";
        var userId = await CreateTestUserAsync(userName, $"{userName}@test.com", "Test@123456");

        // UserManager 的写操作走 EF 仓储（含集合加载），必须显式开 UoW
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ou = new OrganizationUnit(Guid.NewGuid(), $"ExportOu-{Guid.NewGuid().ToString("N")[..6]}", parentId: null);
        await WithUnitOfWorkAsync(async () =>
        {
            await ouManager.CreateAsync(ou);
            var user = (await _userManager.FindByIdAsync(userId.ToString()))!;
            await _userManager.AddToOrganizationUnitAsync(user, ou);
        });

        try
        {
            var builder = GetRequiredService<IUserExcelBuilder>();
            // maskSensitive:false 保持原有明文断言语义；脱敏输出另有专测
            var bytes = await builder.BuildAsync(userName, maxResultCount: 10, maskSensitive: false);

            using var stream = new MemoryStream(bytes);
            var rows = stream.Query(useHeaderRow: true)
                .Cast<IDictionary<string, object>>()
                .ToList();
            rows.ShouldNotBeEmpty();

            var row = rows.Single(x => x[UserExportColumnNames.UserName]?.ToString() == userName);
            row[UserExportColumnNames.Email]?.ToString().ShouldBe($"{userName}@test.com");
            row[UserExportColumnNames.OrganizationUnits]?.ToString().ShouldContain(ou.DisplayName);
        }
        finally
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var user = await _userManager.FindByIdAsync(userId.ToString());
                if (user != null)
                {
                    await _userManager.RemoveFromOrganizationUnitAsync(user, ou);
                }
                await ouManager.DeleteAsync(ou.Id);
            });
            await CleanupTestUserAsync(userId);
        }
    }

    #endregion

    #region 周期改密测试（ShouldPeriodicallyChangePasswordAsync，框架方法直测）

    /// <summary>
    /// 创建用户并设置密码最后修改时间（内存态即可，该方法只读实体字段与设置项）。
    /// </summary>
    private async Task<IdentityUser> CreateUserWithLastPasswordChangeAsync(
        string userName, DateTimeOffset? lastPasswordChangeTime, bool isExternal = false)
    {
        var user = new IdentityUser(Guid.NewGuid(), userName, $"{userName}@test.com");
        if (isExternal)
        {
            user.IsExternal = true;
            (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        }
        else
        {
            (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();
        }
        user.SetLastPasswordChangeTime(lastPasswordChangeTime);
        return user;
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Feature_Disabled()
    {
        // Arrange - 默认设置（未开启强制周期改密），即使密码很旧也不应要求改密
        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-off-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-365));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Period_Is_Zero()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "0");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-zero-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-365));
        try
        {
            // 0 值 = 不启用周期判定
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_True_When_Password_Expired()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-exp-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-40));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeTrue();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_When_Password_Not_Expired()
    {
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-ok-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-10));
        try
        {
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    [Fact]
    public async Task PeriodicPasswordChange_Should_Be_False_For_External_User()
    {
        // 外部用户（IsExternal = true，无本地密码哈希）不受密码有效期影响——
        // 框架方法对无密码哈希的用户直接返回 false。
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "true");
        await _settingManager.SetGlobalAsync(
            IdentitySettingNames.Password.PasswordChangePeriodDays, "30");

        var user = await CreateUserWithLastPasswordChangeAsync(
            $"pwd-ext-{Guid.NewGuid():N}"[..20], DateTimeOffset.UtcNow.AddDays(-40), isExternal: true);
        try
        {
            user.PasswordHash.ShouldBeNull();
            (await _userManager.ShouldPeriodicallyChangePasswordAsync(user)).ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.ForceUsersToPeriodicallyChangePassword, "false");
            await _settingManager.SetGlobalAsync(
                IdentitySettingNames.Password.PasswordChangePeriodDays, "0");
        }
    }

    #endregion

    #region 组织单元导入测试（修复：导入模板有 OU 列但导入链路不落库）

    /// <summary>
    /// 建一个根组织单元并返回（调用方负责用 ouManager.DeleteAsync 清理）。
    /// </summary>
    private async Task<OrganizationUnit> CreateTestOuAsync(OrganizationUnitManager ouManager, string displayName)
    {
        var ou = new OrganizationUnit(Guid.NewGuid(), displayName, parentId: null);
        await WithUnitOfWorkAsync(async () => { await ouManager.CreateAsync(ou); });
        return ou;
    }

    [Fact]
    public async Task ImportAsync_Should_Assign_OrganizationUnits_By_Code()
    {
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ou = await CreateTestOuAsync(ouManager, $"导入OU-{Guid.NewGuid().ToString("N")[..6]}");
        var userName = $"import-ou-{Guid.NewGuid().ToString("N")[..8]}";

        try
        {
            var rows = new[]
            {
                new UserImportRowDto
                {
                    UserName = userName,
                    Email = $"{userName}@test.com",
                    Password = "Test@123456",
                    OrganizationUnitCodes = ou.Code,
                    IsActive = "是",
                    IsExternal = "否"
                }
            };
            using var stream = CreateImportExcelStream(rows);

            var result = await _identityUserAdminAppService.ImportAsync(
                new RemoteStreamContent(stream, "test.xlsx"));

            result.SucceededCount.ShouldBe(1, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));

            var user = (await _userManager.FindByNameAsync(userName))!;
            var userOus = await _userManager.GetOrganizationUnitsAsync(user);
            userOus.ShouldContain(x => x.Id == ou.Id);
        }
        finally
        {
            await CleanupTestUserAsync((await _userManager.FindByNameAsync(userName))?.Id ?? Guid.Empty);
            await WithUnitOfWorkAsync(async () => { await ouManager.DeleteAsync(ou.Id); });
        }
    }

    /// <summary>
    /// 分号多值解析主干：双 OU 一次挂接；重复 token 幂等（DistinctBy 只挂一次）；
    /// 编码与显示名可混填（编码优先于同名显示名）。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Should_Support_Multiple_Deduplicated_And_Mixed_Ou_Tokens()
    {
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ou1 = await CreateTestOuAsync(ouManager, $"多值OU-{Guid.NewGuid().ToString("N")[..6]}");
        var ou2 = await CreateTestOuAsync(ouManager, $"多值OU-{Guid.NewGuid().ToString("N")[..6]}");
        var userName = $"import-multiou-{Guid.NewGuid().ToString("N")[..8]}";

        try
        {
            // 双值 + 重复值 + 前后空格 + 编码/显示名混填，一条导入全覆盖
            var rows = new[]
            {
                new UserImportRowDto
                {
                    UserName = userName,
                    Email = $"{userName}@test.com",
                    Password = "Test@123456",
                    OrganizationUnitCodes = $" {ou1.Code} ;{ou2.Code}; {ou1.Code} ;{ou2.DisplayName}",
                    IsActive = "是",
                    IsExternal = "否"
                }
            };
            using var stream = CreateImportExcelStream(rows);

            var result = await _identityUserAdminAppService.ImportAsync(
                new RemoteStreamContent(stream, "test.xlsx"));

            result.SucceededCount.ShouldBe(1, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));

            var user = (await _userManager.FindByNameAsync(userName))!;
            var userOus = await _userManager.GetOrganizationUnitsAsync(user);
            userOus.Count(x => x.Id == ou1.Id || x.Id == ou2.Id).ShouldBe(2, "重复 token 与编码/显示名混填必须只挂接一次");
        }
        finally
        {
            await CleanupTestUserAsync((await _userManager.FindByNameAsync(userName))?.Id ?? Guid.Empty);
            await WithUnitOfWorkAsync(async () =>
            {
                await ouManager.DeleteAsync(ou1.Id);
                await ouManager.DeleteAsync(ou2.Id);
            });
        }
    }

    /// <summary>
    /// 导出列写的是 OU 显示名，导入必须对称接受显示名（导出产物可直接回导）；
    /// 显示名命中多个组织单元时报歧义错误并指向编码。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Should_Accept_Ou_DisplayName_And_Reject_Ambiguous_Name()
    {
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var uniqueName = $"导入OU-{Guid.NewGuid().ToString("N")[..6]}";
        var ou = await CreateTestOuAsync(ouManager, uniqueName);

        // ABP 校验同级显示名唯一：歧义场景构造为「不同父节点下的同名子单元」（如两地各自的“研发部”）
        var parent1 = await CreateTestOuAsync(ouManager, $"歧义父A-{Guid.NewGuid().ToString("N")[..6]}");
        var parent2 = await CreateTestOuAsync(ouManager, $"歧义父B-{Guid.NewGuid().ToString("N")[..6]}");
        Guid dup1Id = Guid.Empty, dup2Id = Guid.Empty;
        await WithUnitOfWorkAsync(async () =>
        {
            var dup1 = new OrganizationUnit(Guid.NewGuid(), "重名OU-导入", parent1.Id);
            var dup2 = new OrganizationUnit(Guid.NewGuid(), "重名OU-导入", parent2.Id);
            await ouManager.CreateAsync(dup1);
            await ouManager.CreateAsync(dup2);
            dup1Id = dup1.Id;
            dup2Id = dup2.Id;
        });
        var userName = $"import-ouname-{Guid.NewGuid().ToString("N")[..8]}";

        try
        {
            var rows = new[]
            {
                new UserImportRowDto
                {
                    UserName = userName,
                    Email = $"{userName}@test.com",
                    Password = "Test@123456",
                    OrganizationUnitCodes = uniqueName,
                    IsActive = "是",
                    IsExternal = "否"
                }
            };
            using var stream = CreateImportExcelStream(rows);
            var result = await _identityUserAdminAppService.ImportAsync(
                new RemoteStreamContent(stream, "test.xlsx"));
            result.SucceededCount.ShouldBe(1, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));

            var user = (await _userManager.FindByNameAsync(userName))!;
            (await _userManager.GetOrganizationUnitsAsync(user)).ShouldContain(x => x.Id == ou.Id);

            // 歧义显示名 → 行级失败，错误信息指向编码
            var ambiguousRows = new[]
            {
                new UserImportRowDto
                {
                    UserName = userName + "-amb",
                    Email = $"{userName}-amb@test.com",
                    Password = "Test@123456",
                    OrganizationUnitCodes = "重名OU-导入",
                    IsActive = "是",
                    IsExternal = "否"
                }
            };
            using var ambiguousStream = CreateImportExcelStream(ambiguousRows);
            var ambiguousResult = await _identityUserAdminAppService.ImportAsync(
                new RemoteStreamContent(ambiguousStream, "test.xlsx"));
            ambiguousResult.FailedCount.ShouldBe(1);
            ambiguousResult.Errors.ShouldContain(e =>
                e.ErrorMessage == _localizer["UserImport:OrganizationUnitAmbiguous", 1, "重名OU-导入"].Value);
        }
        finally
        {
            await CleanupTestUserAsync((await _userManager.FindByNameAsync(userName))?.Id ?? Guid.Empty);
            await WithUnitOfWorkAsync(async () =>
            {
                await ouManager.DeleteAsync(ou.Id);
                if (dup1Id != Guid.Empty)
                {
                    await ouManager.DeleteAsync(dup1Id);
                }
                if (dup2Id != Guid.Empty)
                {
                    await ouManager.DeleteAsync(dup2Id);
                }
                await ouManager.DeleteAsync(parent1.Id);
                await ouManager.DeleteAsync(parent2.Id);
            });
        }
    }

    [Fact]
    public async Task ImportAsync_Should_Fail_When_OrganizationUnit_Not_Found()
    {
        var missingUserName = $"import-oumiss-{Guid.NewGuid().ToString("N")[..8]}";
        var rows = new[]
        {
            new UserImportRowDto
            {
                UserName = missingUserName,
                Email = $"import-oumiss-{Guid.NewGuid().ToString("N")[..8]}@test.com",
                Password = "Test@123456",
                OrganizationUnitCodes = "no-such-ou-code",
                IsActive = "是",
                IsExternal = "否"
            }
        };
        using var stream = CreateImportExcelStream(rows);

        var result = await _identityUserAdminAppService.ImportAsync(
            new RemoteStreamContent(stream, "test.xlsx"));

        result.FailedCount.ShouldBe(1);
        result.Errors.ShouldContain(e =>
            e.ErrorMessage == _localizer["UserImport:OrganizationUnitNotFound", 1, "no-such-ou-code"].Value);
        // OU 解析前移到建用户之前：失败行不得留下半截用户（无用户、无角色、无 OU 挂接）
        (await _userManager.FindByNameAsync(missingUserName)).ShouldBeNull();
    }

    #endregion

    #region CSV 导入测试（修复：前端接受 .csv 但后端只按 xlsx 解析）

    [Fact]
    public async Task ImportAsync_Should_Accept_Csv_File()
    {
        var userName = $"import-csv-{Guid.NewGuid().ToString("N")[..8]}";
        // 表头与 UserImportColumnNames 中文列名同源（与模板一致），MiniExcel 按 [ExcelColumnName] 映射
        var csv = string.Join("\r\n",
            "用户名,名,姓,邮箱,手机号,密码,角色,组织单元,是否启用,是否外部用户",
            $"{userName},, ,{userName}@test.com,,Test@123456,,,是,否");
        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

        var result = await _identityUserAdminAppService.ImportAsync(
            new RemoteStreamContent(new MemoryStream(bytes), "users.csv", "text/csv"));

        result.TotalCount.ShouldBe(1);
        result.SucceededCount.ShouldBe(1, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
        result.FailedCount.ShouldBe(0);

        var user = await _userManager.FindByNameAsync(userName);
        user.ShouldNotBeNull();
        user.Email.ShouldBe($"{userName}@test.com");

        await CleanupTestUserAsync(user.Id);
    }

    /// <summary>
    /// Excel「另存为 CSV(UTF-8)」产物带 BOM 是中文环境的高频路径：BOM 不得破坏首列表头映射。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Should_Accept_Csv_With_Utf8_Bom()
    {
        var userName = $"import-csvbom-{Guid.NewGuid().ToString("N")[..8]}";
        var csv = string.Join("\r\n",
            "用户名,名,姓,邮箱,手机号,密码,角色,组织单元,是否启用,是否外部用户",
            $"{userName},, ,{userName}@test.com,,Test@123456,,,是,否");

        // UTF-8 BOM（EF BB BF）开头
        var payload = System.Text.Encoding.UTF8.GetPreamble()
                      .Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray();

        var result = await _identityUserAdminAppService.ImportAsync(
            new RemoteStreamContent(new MemoryStream(payload), "users-bom.csv", "text/csv"));

        result.SucceededCount.ShouldBe(1, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));

        var user = await _userManager.FindByNameAsync(userName);
        user.ShouldNotBeNull();

        try
        {
            user.UserName.ShouldBe(userName);
        }
        finally
        {
            await CleanupTestUserAsync(user.Id);
        }
    }

    /// <summary>
    /// .xls（BIFF 老格式）MiniExcel 不支持：必须在解析分派处给明确文案，
    /// 而不是落进 XLSX 分支报晦涩异常（前端 accept 已同步收紧，这里防 API 直调）。
    /// </summary>
    [Fact]
    public async Task ImportAsync_Should_Reject_Legacy_Xls_With_Clear_Message()
    {
        var bytes = new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }; // OLE2 魔数，内容不重要——分派处就该被拒

        var exception = await Should.ThrowAsync<UserFriendlyException>(async () =>
            await _identityUserAdminAppService.ImportAsync(
                new RemoteStreamContent(new MemoryStream(bytes), "users.xls")));

        exception.Message.ShouldBe(_localizer["UserImport:UnsupportedFormat", "users.xls"].Value);
    }

    #endregion
}
