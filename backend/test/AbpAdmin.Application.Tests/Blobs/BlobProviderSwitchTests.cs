using System.Text;
using System.Threading.Tasks;
using AbpAdmin.Blobs;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.BlobStoring;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Blobs;

/* 验证 BLOB 存储多 provider 改造（T0.2）。
 * 默认（未配置 Blob 节）回落 Database provider，存取应成功。
 */
public abstract class BlobProviderSwitchTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IBlobContainer _blobContainer;

    protected BlobProviderSwitchTests()
    {
        _blobContainer = GetRequiredService<IBlobContainer>();
    }

    [Fact]
    public async Task Should_Save_And_Get_Blob_With_Default_Database_Provider()
    {
        var blobName = "test-blob-" + System.Guid.NewGuid().ToString("N");
        var content = "hello blob"u8.ToArray();

        await _blobContainer.SaveAsync(blobName, content, overrideExisting: true);

        var retrieved = await _blobContainer.GetAllBytesAsync(blobName);
        retrieved.ShouldBe(content);

        (await _blobContainer.ExistsAsync(blobName)).ShouldBeTrue();

        await _blobContainer.DeleteAsync(blobName);
        (await _blobContainer.ExistsAsync(blobName)).ShouldBeFalse();
    }

    [Fact]
    public void Blob_Options_Should_Bind_Default_Provider()
    {
        var options = GetRequiredService<IOptions<AbpAdminBlobStorageOptions>>().Value;

        // 测试环境未配置 Blob 节，应回落到默认 Database
        options.Provider.ShouldBe("Database");
    }
}
