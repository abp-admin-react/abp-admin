using System;
using System.Threading.Tasks;
using AbpAdmin.Storage.Tests.TestInfrastructure;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Xunit;
using Xunit.Abstractions;

namespace AbpAdmin.Storage.Tests.Storage;

/// <summary>
/// 真实 Elasticsearch 集成测试(Ping → 写入文档 → 断言响应 → 清理)。
/// 环境变量 ABPADMIN_TEST_ES_URL 未配置时用例显示为 Skipped(见 RequiresEnvFactAttribute)。
/// </summary>
[Trait("Requires", "Elasticsearch")]
public class ElasticsearchClientIntegrationTests
{
    private const string TestIndex = "abpadmin-it-test";

    private readonly ITestOutputHelper _output;

    public ElasticsearchClientIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [RequiresEnvFact("ABPADMIN_TEST_ES_URL")]
    public async Task Client_Should_Ping_And_Index_Document()
    {
        var client = new ElasticsearchClient(new ElasticsearchClientSettings(
                new Uri(Environment.GetEnvironmentVariable("ABPADMIN_TEST_ES_URL")))
            .Authentication(new BasicAuthentication(
                Environment.GetEnvironmentVariable("ABPADMIN_TEST_ES_USER") ?? string.Empty,
                Environment.GetEnvironmentVariable("ABPADMIN_TEST_ES_PASS") ?? string.Empty)));

        var ping = await client.PingAsync();
        Assert.True(ping.IsValidResponse, $"Ping 失败: {ping.ElasticsearchServerError?.Error?.Reason}");

        try
        {
            var index = await client.IndexAsync(
                new { message = "abpadmin integration test", at = DateTime.UtcNow },
                descriptor => descriptor.Index(TestIndex));
            Assert.True(index.IsValidResponse, $"写入失败: {index.ElasticsearchServerError?.Error?.Reason}");
            Assert.False(string.IsNullOrEmpty(index.Id));
        }
        finally
        {
            // 清理测试索引,反复运行不残留;清理失败不影响测试结果
            try
            {
                await client.Indices.DeleteAsync(
                    new Elastic.Clients.Elasticsearch.IndexManagement.DeleteIndexRequest(TestIndex));
            }
            catch (Exception deleteEx)
            {
                _output.WriteLine($"清理测试索引失败(忽略): {deleteEx.Message}");
            }
        }
    }
}
