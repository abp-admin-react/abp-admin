using System;
using System.Threading.Tasks;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Files.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Files;

/* 验证 EasyAbp.FileManagement 搬运（T1.1）的三条核心路径：
 * 上传 -> 列表 -> download-info -> download 内容一致；
 * 扩展名白名单拒绝（.exe）；
 * configuration 端点返回容器约束供前端预校验。
 */
public abstract class FileManagementIntegrationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ContainerName = "admin";

    private readonly IFileAppService _fileAppService;

    protected FileManagementIntegrationTests()
    {
        _fileAppService = GetRequiredService<IFileAppService>();
    }

    [Fact]
    public async Task Should_Upload_List_And_Download_File()
    {
        var content = "hello file management"u8.ToArray();

        // 上传
        var created = await _fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "test-note.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = content
        });

        created.FileInfo.ShouldNotBeNull();
        created.FileInfo.FileName.ShouldBe("test-note.txt");

        // 列表
        var list = await _fileAppService.GetListAsync(new GetFileListInput
        {
            FileContainerName = ContainerName
        });
        list.Items.ShouldContain(f => f.Id == created.FileInfo.Id);

        // download-info 拿 token
        var downloadInfo = await _fileAppService.GetDownloadInfoAsync(created.FileInfo.Id);
        downloadInfo.Token.ShouldNotBeNullOrWhiteSpace();

        // download 内容一致
        var downloaded = await _fileAppService.DownloadAsync(created.FileInfo.Id, downloadInfo.Token);
        downloaded.Content.ShouldBe(content);
        downloaded.FileName.ShouldBe("test-note.txt");

        // 清理
        await _fileAppService.DeleteAsync(created.FileInfo.Id);
    }

    [Fact]
    public async Task Should_Reject_Not_Allowed_File_Extension()
    {
        // .exe 不在白名单内，应被拒绝
        await Should.ThrowAsync<BusinessException>(async () =>
        {
            await _fileAppService.CreateAsync(new CreateFileInput
            {
                FileContainerName = ContainerName,
                FileName = "evil.exe",
                MimeType = "application/octet-stream",
                FileType = FileType.RegularFile,
                Content = "MZ"u8.ToArray()
            });
        });
    }

    [Fact]
    public async Task Should_Return_Container_Configuration_For_PreValidation()
    {
        var configuration = await _fileAppService.GetConfigurationAsync(ContainerName, null);

        configuration.ShouldNotBeNull();
        configuration.MaxByteSizeForEachFile.ShouldBeGreaterThan(0);
        configuration.AllowOnlyConfiguredFileExtensions.ShouldBeTrue();
        configuration.FileExtensionsConfiguration.ShouldContainKey(".txt");
        configuration.FileExtensionsConfiguration.ShouldNotContainKey(".exe");
    }
}
