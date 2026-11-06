using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class PublicErrorContractSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        NewLine = "\n",
    };

    [RequiresHostDatabaseFact]
    public async Task Assembled_Catalog_Should_Match_Frontend_Snapshot()
    {
        var app = await HostTestBed.HostTask;
        var catalog = app.Services.GetRequiredService<IPublicErrorCatalog>();
        var snapshot = BuildSnapshot(catalog);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions).Replace("\r\n", "\n");
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        var path = ResolveCatalogPath();
        if (Environment.GetEnvironmentVariable("UPDATE_PUBLIC_ERROR_CONTRACT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return;
        }

        File.Exists(path).ShouldBeTrue($"公开契约快照缺失：{path}");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!expected.EndsWith('\n'))
        {
            expected += "\n";
        }

        json.ShouldBe(expected,
            "公开错误目录与 web/src/abp/customer/public-error-catalog.json 不一致。"
            + "确认是有意变更后设置 UPDATE_PUBLIC_ERROR_CONTRACT=1 刷新快照。");
        json.ShouldNotContain("InternalCode");
        json.ShouldNotContain("INTERNAL_ONLY_MARKER");
        snapshot.Entries.ShouldNotContain(e => e.Code.Contains("SELECT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dual_Anchor_Path_Finder_Should_Reject_Incomplete_Candidates()
    {
        Should.NotThrow(() => ResolveCatalogPath());
    }

    private static PublicErrorCatalogSnapshot BuildSnapshot(IPublicErrorCatalog catalog)
    {
        var grouped = catalog.Definitions
            .GroupBy(d => d.PublicCode, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var definition = g.First();
                var dataFields = new SortedSet<string>(StringComparer.Ordinal) { "correlationId" };
                foreach (var parameter in g.SelectMany(d => d.AllowedParameters).Where(p => p.CanReturnToClient))
                {
                    dataFields.Add(parameter.Name);
                }

                var explicitStatuses = g.SelectMany(d => d.PublicHttpStatuses).Distinct().ToArray();
                var statuses = (explicitStatuses.Length > 0
                        ? explicitStatuses
                        : DefaultStatuses(definition.Kind))
                    .OrderBy(s => s)
                    .ToArray();

                return new PublicErrorCatalogEntry
                {
                    Code = g.Key,
                    Kind = ToKindName(definition.Kind),
                    HttpStatuses = statuses,
                    DataFields = dataFields.ToArray(),
                };
            })
            .ToArray();

        return new PublicErrorCatalogSnapshot
        {
            SchemaVersion = 1,
            Entries = grouped,
        };
    }

    private static int[] DefaultStatuses(PublicErrorKind kind)
    {
        return kind switch
        {
            PublicErrorKind.Authentication => [401],
            PublicErrorKind.Forbidden => [403],
            PublicErrorKind.Validation => [400],
            PublicErrorKind.NotFound => [404],
            PublicErrorKind.RateLimited => [429],
            PublicErrorKind.Conflict => [409],
            PublicErrorKind.Server => [500],
            _ => [400],
        };
    }

    private static string ToKindName(PublicErrorKind kind)
    {
        return char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
    }

    private static string ResolveCatalogPath()
    {
        var candidates = new List<string>();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var sln = Path.Combine(directory.FullName, "backend", "AbpAdmin.slnx");
            var package = Path.Combine(directory.FullName, "web", "package.json");
            if (File.Exists(sln) && File.Exists(package))
            {
                return Path.Combine(directory.FullName, "web", "src", "abp", "customer", "public-error-catalog.json");
            }

            candidates.Add(directory.FullName);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "无法同时命中 backend/AbpAdmin.slnx 与 web/package.json。候选：" + string.Join(" | ", candidates));
    }

    private sealed class PublicErrorCatalogSnapshot
    {
        public int SchemaVersion { get; set; }
        public PublicErrorCatalogEntry[] Entries { get; set; } = [];
    }

    private sealed class PublicErrorCatalogEntry
    {
        public string Code { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int[] HttpStatuses { get; set; } = [];
        public string[] DataFields { get; set; } = [];
    }
}
