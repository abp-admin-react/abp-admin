using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace AbpAdmin.Files;

/// <summary>
/// 文件管理上传内容防线（GUI 全链路测试 T-FM-02 发现的缺口）：
/// EasyAbp FileManagement 的通用上传端点不做内容校验，「exe 改名 .png / SVG 含脚本改 .png /
/// 0 字节文件」均可入库；magic-bytes 校验此前只覆盖头像（ProfileAvatarAppService）。
///
/// 在 MVC 模型绑定之前用 FormFeature 解析 multipart，校验每个文件段：
/// 图片扩展名必须匹配文件签名（magic bytes），.svg/.html 文本段禁止内嵌脚本；
/// 不匹配直接 400 短路（ABP 错误格式 JSON，错误码 AbpAdmin:Files:ContentMismatch），
/// 请求不再进入端点，且每次拒绝打 Warning 日志（含文件名与拒绝原因，便于审计回溯）。
/// EnableBuffering 回卷请求体，后续管道照常解析。扩展名不在清单内的文件
/// （txt/pdf 等普通文档）放行，交由下载侧 Content-Disposition: attachment 兜底。
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
        var path = context.Request.Path.Value ?? string.Empty;
        var isFileUpload = HttpMethods.IsPost(context.Request.Method) &&
                           (path.Equals("/api/file-management/file", StringComparison.OrdinalIgnoreCase) ||
                            path.Equals("/api/file-management/file/many", StringComparison.OrdinalIgnoreCase));

        if (isFileUpload &&
            context.Request.ContentType != null &&
            context.Request.ContentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) &&
            context.Request.ContentLength > 0)
        {
            context.Request.EnableBuffering();
            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(context.RequestAborted);
            }
            catch
            {
                // 非法 multipart 交由后续模型绑定报错
                await _next(context);
                return;
            }

            foreach (var file in form.Files)
            {
                var reason = await FindRejectionReasonAsync(file);
                if (reason != null)
                {
                    // 拒绝必须留痕：文件名、扩展名、拒绝原因、来源路径，与 400 响应一一对应
                    _logger.LogWarning(
                        "上传被内容防线拒绝：{FileName}（扩展名 {Extension}，原因 {Reason}），路径 {Path}",
                        file.FileName, Path.GetExtension(file.FileName), reason, path);

                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = new
                        {
                            code = "AbpAdmin:Files:ContentMismatch",
                            message = "上传被拒绝：文件内容与其扩展名不匹配，或包含不允许的脚本内容。",
                            details = (string?)null,
                            data = new { fileName = file.FileName, reason },
                            validationErrors = Array.Empty<object>(),
                        },
                    });
                    return;
                }
            }

            // 回卷请求体，让 MVC 端点重新解析
            context.Request.Body.Position = 0;
        }

        await _next(context);
    }

    /// <summary>返回拒绝原因；null 表示放行。原因文案同时进入 Warning 日志与响应 data.reason。</summary>
    private static async Task<string?> FindRejectionReasonAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var head = stream.ToArray();

        if (head.Length == 0)
        {
            return "零字节文件";
        }

        if (ImageSignatures.TryGetValue(extension, out var signatures))
        {
            var matches = signatures.Any(sig =>
                head.Length >= sig.Length && sig.SequenceEqual(head.Take(sig.Length)));
            if (!matches)
            {
                return "图片签名与扩展名不匹配（疑似改名文件）";
            }
        }

        if (ScriptableTextExtensions.TryGetValue(extension, out var markers))
        {
            // 只扫文本头部 8KB，足够覆盖典型脚本注入
            var sample = Encoding.UTF8.GetString(head, 0, Math.Min(head.Length, 8192));
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
