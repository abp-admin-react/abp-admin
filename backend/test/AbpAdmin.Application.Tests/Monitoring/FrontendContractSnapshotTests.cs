using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbpAdmin.Monitoring;
using Xunit;

namespace AbpAdmin.Monitoring;

/* 前端契约形状快照（全局设计见 web/AGENTS.md 服务层约定）。
 *
 * 背景：前端 openapi 生成器已退役，web/src/abp 手写镜像层以本仓库 Contracts 为事实源；
 * 生成器（无论 umi 还是 ABP 官方 Angular 代理）都不忠实传递可空性
 * （参见 abp#22798/#25176），因此 C# 的 T? ⇔ TS 的 T | null 这层对应关系
 * 必须靠「人改后端契约时被机器叫住」来维持，而不是靠任何生成器。
 *
 * 本测试把前端消费的 Contracts DTO 形状（属性集 / 类型 / 可空性 / 枚举成员）
 * 反射为快照文件 Contracts/Snapshots/frontend-contract-shapes.json：
 *   - 任何契约变更（改名/改类型/去 ?）都会让本测试变红，强制变更者
 *     1) 确认是有意变更；2) 同步 web/src/abp 手写镜像（C# 的 T? ⇔ TS 的 T | null）；
 *     3) 刷新快照并与镜像变更同一提交：
 *          FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1 dotnet test test/AbpAdmin.Application.Tests \
 *            --filter "FullyQualifiedName~FrontendContractSnapshot"
 *     （首次生成快照时测试会失败一次并在磁盘上落盘，重跑即绿。）
 *   - 扩展方式：前端新消费一个 Contracts DTO 时，把类型加进 SeedTypes；
 *     嵌套 DTO / 枚举自动递归纳入，无需手工登记。
 */
public class FrontendContractSnapshotTests
{
    private static readonly string SnapshotPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Contracts", "Snapshots",
        "frontend-contract-shapes.json");

    /// <summary>web/src/abp 手写镜像层消费的契约 DTO 种子（nullability 关键字段所在的 DTO 必须列入）。</summary>
    private static readonly Type[] SeedTypes =
    {
        typeof(ServerMonitorDto),
        typeof(CacheMonitorInfoDto),
    };

    private static readonly NullabilityInfoContext NullabilityCtx = new();

    private static readonly Dictionary<string, string> PrimitiveNames = new()
    {
        ["String"] = "string",
        ["Int32"] = "int",
        ["Int64"] = "long",
        ["Int16"] = "short",
        ["Byte"] = "byte",
        ["Boolean"] = "bool",
        ["Double"] = "double",
        ["Single"] = "float",
        ["Decimal"] = "decimal",
    };

    [Fact]
    public void Frontend_consumed_contract_shapes_should_match_snapshot()
    {
        var shapes = SnapshotShapes(SeedTypes);
        var json = JsonSerializer.Serialize(shapes, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // 行尾固定 LF：序列化默认用 Environment.NewLine（Windows 上是 CRLF），而仓库
            // .gitattributes 强制 LF——编码规范化后快照文件是 LF、比较串是 CRLF，全文比对
            // 永远失败（与本测试要抓的契约变更无关的假红）。写入与比较两端都归一到 LF。
            NewLine = "\n",
        });

        if (Environment.GetEnvironmentVariable("FRONTEND_CONTRACT_SNAPSHOT_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, json, new UTF8Encoding(false));
            return;
        }

        if (!File.Exists(SnapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, json, new UTF8Encoding(false));
            Assert.Fail(
                "快照不存在，已按当前契约生成。请重跑本测试确认通过，"
                + "并把 frontend-contract-shapes.json 一并提交。");
        }

        // 读端再归一一次：即使有人绕过 .gitattributes 在磁盘上留下 CRLF 也不误报
        var expected = File.ReadAllText(SnapshotPath).Replace("\r\n", "\n");
        if (expected != json)
        {
            Assert.Fail(
                "前端消费的契约 DTO 形状与快照不一致（改名/改类型/可空性变化）。"
                + "请确认这是有意的契约变更，同步 web/src/abp 手写镜像"
                + "（C# 的 T? ⇔ TS 的 T | null），"
                + "再以 FRONTEND_CONTRACT_SNAPSHOT_UPDATE=1 重跑本测试刷新快照并一并提交。");
        }
    }

    /// <summary>种子类型出发，按「Contracts 程序集内的类/枚举」规则递归发现全部被消费的契约类型。</summary>
    private static SortedDictionary<string, object> SnapshotShapes(Type[] seeds)
    {
        var contractsAssembly = seeds[0].Assembly;
        var pending = new Queue<Type>(seeds);
        var shapes = new SortedDictionary<string, object>();

        while (pending.Count > 0)
        {
            var type = pending.Dequeue();
            var key = type.FullName!;
            if (shapes.ContainsKey(key))
            {
                continue;
            }

            if (type.IsEnum)
            {
                shapes[key] = new Dictionary<string, object>
                {
                    ["kind"] = "enum",
                    ["members"] = Enum.GetNames(type),
                };
                continue;
            }

            var properties = new SortedDictionary<string, object>();
            foreach (var property in type
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                properties[property.Name] = new Dictionary<string, object>
                {
                    ["type"] = DescribeType(property.PropertyType),
                    ["nullable"] = IsNullable(property),
                };
                foreach (var nested in ContractTypesOf(property.PropertyType, contractsAssembly))
                {
                    pending.Enqueue(nested);
                }
            }

            shapes[key] = new Dictionary<string, object>
            {
                ["kind"] = "dto",
                ["properties"] = properties,
            };
        }

        return shapes;
    }

    /// <summary>属性类型中的（Contracts 程序集内的）DTO / 枚举：含数组与泛型参数的递归展开。</summary>
    private static IEnumerable<Type> ContractTypesOf(Type type, Assembly contractsAssembly)
    {
        if (type.IsArray)
        {
            return ContractTypesOf(type.GetElementType()!, contractsAssembly);
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return ContractTypesOf(underlying, contractsAssembly);
        }

        if (!type.IsGenericType)
        {
            return IsContractType(type, contractsAssembly) ? new[] { type } : Array.Empty<Type>();
        }

        return type.GetGenericArguments()
            .SelectMany(arg => ContractTypesOf(arg, contractsAssembly));
    }

    private static bool IsContractType(Type type, Assembly contractsAssembly)
    {
        return type.Assembly == contractsAssembly && (type.IsClass || type.IsEnum);
    }

    /// <summary>可空性：值类型看 Nullable&lt;T&gt;，引用类型读 NRT 元数据（C# 的 string? / = default! 均正确区分）。</summary>
    private static bool IsNullable(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (Nullable.GetUnderlyingType(type) != null)
        {
            return true;
        }

        if (type.IsValueType)
        {
            return false;
        }

        return NullabilityCtx.Create(property).WriteState == NullabilityState.Nullable;
    }

    /// <summary>类型描述用 C# 惯用名（string/List&lt;T&gt;/T?[]…），保证快照可读、diff 稳定。</summary>
    private static string DescribeType(Type type)
    {
        if (type.IsArray)
        {
            return DescribeType(type.GetElementType()!) + "[]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return DescribeType(underlying) + "?";
        }

        if (type.IsGenericType)
        {
            var args = string.Join(
                ", ",
                type.GetGenericArguments().Select(DescribeType));
            return $"{type.Name.Split('`')[0]}<{args}>";
        }

        if (PrimitiveNames.TryGetValue(type.Name, out var alias))
        {
            return alias;
        }

        return type.Name;
    }
}
