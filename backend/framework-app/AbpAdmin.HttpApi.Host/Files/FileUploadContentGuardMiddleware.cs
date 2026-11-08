using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace AbpAdmin.Files;

/// <summary>
/// 文件管理上传内容防线：EasyAbp FileManagement 的通用上传端点不做内容校验，
/// 「exe 改名 .png / SVG 含脚本改 .png / 0 字节文件」均可入库（magic-bytes 校验仅覆盖
/// 头像 ProfileAvatarAppService）。
///
/// 在 MVC 模型绑定之前解析请求体，校验每个文件内容：
/// 图片扩展名必须匹配文件签名（magic bytes），.svg/.html 文本段禁止内嵌脚本；
/// 不匹配直接 400 短路（ABP 错误格式 JSON，错误码 AbpAdmin:Files:ContentMismatch），
/// 请求不再进入端点，且每次拒绝打 Warning 日志（含文件名与拒绝原因，便于审计回溯）。
/// EnableBuffering 回卷请求体，后续管道照常解析。扩展名不在清单内的文件
/// （txt/pdf 等普通文档）放行，交由下载侧 Content-Disposition: attachment 兜底。
/// 有内容但缺少扩展名则拒绝（fail-closed）：MVC 绑定不要求扩展名，空扩展名曾放行走魔数绕过。
///
/// 受理面两条通道都要扫，按「基路由自身或其子路径」前缀收口（EasyAbp 5.12.0 FileController
/// 基路由 /api/file-management/file，包升级新增路由自动覆盖；下载路由是 GET 不受影响）：
/// ① multipart 文件段（''、many、with-stream、many/with-stream）；
/// ② JSON 字节通道（with-bytes/many/with-bytes：content/fileContents 以 base64 进 JSON、
///    fileName 可控——加压轮实证可绕过 multipart-only 门直送 blob 存储）。
/// 解析失败仅当请求未中止时放行给后续模型绑定；客户端断开导致的取消异常原样上抛。
/// </summary>
public class FileUploadContentGuardMiddleware
{
    /// <summary>图片扩展名 → 文件签名前缀。签名清单与 ImageMimeTypes 的主流格式对齐。</summary>
    private static readonly Dictionary<string, byte[][]> ImageSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".gif"] = new[] { new byte[] { 0x47, 0x49, 0x46, 0x38 } }, // GIF8
        [".webp"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } }, // RIFF（容器头）
        [".bmp"] = new[] { new byte[] { 0x42, 0x4D } },
    };

    /// <summary>可执行脚本的文本扩展名 → 内容黑名单标记（扫头部 8KB 足够覆盖典型注入）。</summary>
    private static readonly Dictionary<string, string[]> ScriptableTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".svg"] = new[] { "<script", "onload=", "javascript:" },
        [".html"] = new[] { "<script" },
        [".htm"] = new[] { "<script" },
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<FileUploadContentGuardMiddleware> _logger;

    public FileUploadContentGuardMiddleware(RequestDelegate next, ILogger<FileUploadContentGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 门限与受理同口径：基路由自身或其子路径前缀收口（逐条枚举会被包升级新增路由绕过），
        // 大小写不敏感 + 尾斜杠归一。
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var isFileUpload = HttpMethods.IsPost(context.Request.Method) &&
                           path.StartsWith("/api/file-management/file", StringComparison.OrdinalIgnoreCase) &&
                           (path.Length == "/api/file-management/file".Length ||
                            path["/api/file-management/file".Length] == '/');

        // chunked（ContentLength=null）同样要扫，否则分块传输可把改名文件直送模型绑定
        // （无体请求 ContentLength==0 跳过；解析本身受 Kestrel/FormOptions 上限约束不变）。
        var hasBody = context.Request.ContentLength is null || context.Request.ContentLength.Value > 0;
        var contentType = context.Request.ContentType;
        var isMultipart = contentType != null &&
                          contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase);
        var isJson = contentType != null &&
                     contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (isFileUpload && hasBody && isMultipart)
        {
            context.Request.EnableBuffering();
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(context.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException &&
                                       !context.RequestAborted.IsCancellationRequested)
            {
                // 非法 multipart 交由后续模型绑定报错；客户端断开导致的取消异常
                // （ConnectionAbortedException 继承自 OperationCanceledException）原样上抛，
                // 不吞掉后把死请求推进管道（否则深层中间件再读请求体会炸出费解堆栈）
                await _next(context);
                return;
            }

            foreach (var file in form.Files)
            {
                var reason = await FindRejectionReasonAsync(file, context.RequestAborted);
                if (reason != null)
                {
                    await RejectAsync(context, file.FileName, reason, path);
                    return;
                }
            }

            // 回卷请求体，让 MVC 端点重新解析
            context.Request.Body.Position = 0;
        }
        else if (isFileUpload && hasBody && isJson)
        {
            if (await ScanJsonUploadAndRejectIfNeededAsync(context, path))
            {
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// JSON 字节通道（with-bytes / many/with-bytes）的内容扫描。DTO 的 content/fileContents
    /// 字段是 base64 字节（System.Text.Json 原生绑定 byte[]），fileName 是扩展名权威——
    /// 与 multipart 走同一套签名/脚本判定。返回 true 表示本通道已收口（拒绝短路，或非法
    /// JSON 已交由后续模型绑定处理），调用方不得再进管道；返回 false 表示扫描通过。
    /// </summary>
    private async Task<bool> ScanJsonUploadAndRejectIfNeededAsync(HttpContext context, string path)
    {
        context.Request.EnableBuffering();

        // 有界读入：JSON DOM 对大体量请求体的内存放大显著（约 2-3 倍），且本守卫位于认证之前、
        // 匿名并发放大；超上限直接拒绝（fail-closed），既防放大也不给超大 JSON 留绕过面。
        // SPA 与管理端只用 multipart 通道，JSON 通道面向程序化小文件上传，64MiB 足够。
        const int MaxJsonScanBytes = 64 * 1024 * 1024;
        // await using：取消/拒绝/放行路径都释放缓冲；Parse 直接吃流，避免 ToArray 再拷一份 64MiB。
        await using var buffered = new MemoryStream();
        var buffer = new byte[81920];
        try
        {
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                if (read == 0)
                {
                    break;
                }

                buffered.Write(buffer, 0, read);
                if (buffered.Length > MaxJsonScanBytes)
                {
                    await RejectAsync(context, "(json body)", "JSON 上传通道超出大小上限", path);
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }

        buffered.Position = 0;
        JsonDocument? doc;
        try
        {
            doc = await JsonDocument.ParseAsync(buffered, cancellationToken: context.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException &&
                                   !context.RequestAborted.IsCancellationRequested)
        {
            // 非法 JSON 交由后续模型绑定报错
            context.Request.Body.Position = 0;
            await _next(context);
            return true;
        }

        using (doc)
        {
            // 深度优先扫描（安全审计 run-3）：对 DOM 里每一个 JSON 对象，把它自身的
            // fileName/fileNames 与 content/fileContents 配对送检——many/with-bytes 真实
            // DTO（EasyAbp CreateManyFileInput）把名字与字节嵌在 fileInfos[*] 一层之下，
            // 只看根对象会让该通道完全绕过内容防线（对照面 with-bytes 400、嵌套面 200）。
            // 保持 64MiB 预读上限与大小写不敏感属性匹配语义不变。
            //
            // 复审补丁（code review S1）：同一对象内大小写不敏感重复的同槽属性名
            // （fileName/fileName、FileName/fileName、content/content …）必须整单拒绝——
            // 收集器若只取第一个匹配而 System.Text.Json 模型绑定对重复键取最后一个，
            // 攻击者可用 {"fileName":"a.png","fileName":"evil.svg","content":"<png>",
            // "content":"<script svg>"} 让防线扫 benign 配对、绑定器落 malicious 配对。
            // 重复键在合法上传 JSON 中不存在（DTO 序列化不产生重复名），歧义即恶意。
            var pairs = new List<(string Name, byte[] Bytes)>();
            if (WalkJson(doc.RootElement, pairs, out var ambiguousSlot))
            {
                await RejectAsync(context, ambiguousSlot, "重复/歧义的上传属性名（同名槽多次出现）", path);
                return true;
            }

            foreach (var (fileName, content) in pairs)
            {
                var reason = FindRejectionReason(Path.GetExtension(fileName), content.LongLength, content,
                    content.Length);
                if (reason != null)
                {
                    await RejectAsync(context, fileName, reason, path);
                    return true;
                }
            }
        }

        // 回卷请求体，让 MVC 端点重新解析
        context.Request.Body.Position = 0;
        return false;
    }

    private async Task RejectAsync(HttpContext context, string fileName, string reason, string path)
    {
        // 拒绝必须留痕：文件名、扩展名、拒绝原因、来源路径，与 400 响应一一对应
        _logger.LogWarning(
            "上传被内容防线拒绝：{FileName}（扩展名 {Extension}，原因 {Reason}），路径 {Path}",
            fileName, Path.GetExtension(fileName), reason, path);

        var dispatcher = context.RequestServices?.GetService<ClientErrorHttpDispatcher>();
        if (dispatcher == null)
        {
            // 测试夹具或未注册写出器：仍 400 短路（fail-closed），不回显用户 fileName。
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await dispatcher.WriteKnownAsync(
            context,
            AbpAdminDomainErrorCodes.Files.ContentMismatch,
            StatusCodes.Status400BadRequest,
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["reason"] = reason,
            });
    }

    /// <summary>
    /// 深度优先遍历 JSON DOM：对每个对象做单趟属性扫描，按槽（名字槽 fileName/fileNames、
    /// 字节槽 content/fileContents）收集值并配对（多字节少名字时最后一个名字兜底配对，
    /// 与原根级配对语义一致），再递归子对象与数组元素。
    /// 单趟扫描同时计数每个槽的大小写不敏感命中次数：同一槽被多个属性命中（重复/歧义键）
    /// 即返回 true 整单拒绝——收集器取首值而模型绑定取末值，歧义键就是 benign-首值/
    /// malicious-末值的走私通道（code review S1）。单趟也顺带消除旧实现每对象 4 次线性
    /// 属性扫描与两个无条件 List 分配（code review P2：匿名 64MiB 载荷下的 GC 压力）。
    /// </summary>
    /// <returns>true = 检出歧义重复槽，<paramref name="ambiguousSlot"/> 给出槽名用于拒绝留痕。</returns>
    private static bool WalkJson(JsonElement element, List<(string Name, byte[] Bytes)> pairs, out string ambiguousSlot)
    {
        ambiguousSlot = string.Empty;
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = (List<string>?)null;
            var contents = (List<byte[]>?)null;
            var nameSlotHits = 0;
            var byteSlotHits = 0;

            foreach (var property in element.EnumerateObject())
            {
                var isNameSlot = IsSlot(property.Name, "fileName") || IsSlot(property.Name, "fileNames");
                var isByteSlot = IsSlot(property.Name, "content") || IsSlot(property.Name, "fileContents");
                if (!isNameSlot && !isByteSlot)
                {
                    continue;
                }

                names ??= new List<string>();
                contents ??= new List<byte[]>();
                if (isNameSlot)
                {
                    if (++nameSlotHits > 1)
                    {
                        ambiguousSlot = property.Name;
                        return true;
                    }

                    AddStringValues(property.Value, names);
                }
                else
                {
                    if (++byteSlotHits > 1)
                    {
                        ambiguousSlot = property.Name;
                        return true;
                    }

                    AddByteValues(property.Value, contents);
                }
            }

            if (contents is { Count: > 0 })
            {
                for (var i = 0; i < contents.Count; i++)
                {
                    pairs.Add((names is { Count: > 0 } ? names[Math.Min(i, names.Count - 1)] : string.Empty, contents[i]));
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (WalkJson(property.Value, pairs, out ambiguousSlot))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (WalkJson(item, pairs, out ambiguousSlot))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>槽名匹配（OrdinalIgnoreCase）——模型绑定同口径。</summary>
    private static bool IsSlot(string propertyName, string slotName) =>
        string.Equals(propertyName, slotName, StringComparison.OrdinalIgnoreCase);

    /// <summary>字符串槽值收集：单值或字符串数组（数组内多值合法，歧义只针对重复属性键）。</summary>
    private static void AddStringValues(JsonElement value, List<string> into)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            into.Add(value.GetString() ?? string.Empty);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    into.Add(item.GetString() ?? string.Empty);
                }
            }
        }
    }

    /// <summary>字节槽值收集：base64 字符串单值或数组；非 base64 串交由后续模型绑定报错。</summary>
    private static void AddByteValues(JsonElement value, List<byte[]> into)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            try
            {
                into.Add(value.GetBytesFromBase64());
            }
            catch (FormatException)
            {
                // 非 base64 串交由后续模型绑定报错
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    try
                    {
                        into.Add(item.GetBytesFromBase64());
                    }
                    catch (FormatException)
                    {
                        // 同上
                    }
                }
            }
        }
    }

    /// <summary>返回拒绝原因；null 表示放行。原因文案同时进入 Warning 日志与响应 data.reason。</summary>
    /// <param name="cancellationToken">请求中止令牌；探测读必须可取消，避免已断开连接继续占缓冲。</param>
    private static async Task<string?> FindRejectionReasonAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            return "缺少文件扩展名";
        }

        // 只读判定所需的有界前缀（文本样窗 8KB / 魔数 ≤4 字节），不整文件
        // CopyToAsync(MemoryStream)+ToArray——整拷会对每个文件段制造 2 倍文件大小的瞬时托管
        // 内存，且本守卫位于认证之前，匿名并发放大（每请求上限 201MiB × N 并发）。
        const int ProbeLength = 8192;
        var head = new byte[ProbeLength];
        var total = 0;
        await using (var stream = file.OpenReadStream())
        {
            int read;
            while (total < ProbeLength &&
                   (read = await stream.ReadAsync(head.AsMemory(total, ProbeLength - total), cancellationToken)) > 0)
            {
                total += read;
            }
        }

        return FindRejectionReason(extension, file.Length, head, total);
    }

    /// <summary>签名/脚本判定核心（multipart 文件段与 JSON 字节通道共用）。null=放行。</summary>
    private static string? FindRejectionReason(string extension, long length, byte[] head, int totalRead)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return "缺少文件扩展名";
        }

        if (length == 0)
        {
            return "零字节文件";
        }

        if (ImageSignatures.TryGetValue(extension, out var signatures))
        {
            var matches = signatures.Any(sig =>
                totalRead >= sig.Length && sig.SequenceEqual(head.Take(sig.Length)));
            if (!matches)
            {
                return "图片签名与扩展名不匹配（疑似改名文件）";
            }
        }

        if (ScriptableTextExtensions.TryGetValue(extension, out var markers))
        {
            // 只扫文本头部 8KB，足够覆盖典型脚本注入
            var sample = Encoding.UTF8.GetString(head, 0, Math.Min(totalRead, 8192));
            if (markers.Any(m => sample.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return "文本内容包含不允许的脚本标记";
            }
        }

        return null;
    }
}

public static class FileUploadContentGuardMiddlewareExtensions
{
    public static IApplicationBuilder UseFileUploadContentGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<FileUploadContentGuardMiddleware>();
    }
}
