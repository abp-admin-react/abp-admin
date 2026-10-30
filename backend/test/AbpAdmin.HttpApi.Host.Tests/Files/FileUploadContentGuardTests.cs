using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AbpAdmin.Files;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AbpAdmin.Files;

/// <summary>
/// 回归锁（中间件级，不启动宿主）：
/// 1. 路径门归一化——尾斜杠受理（与 MVC 路由同口径）、大小写不敏感、非守卫路径不拦。
/// 2. chunked（ContentLength=null）请求同样进扫——不得整体跳过，否则匿名主体可用
///    分块传输把改名文件直送模型绑定。
/// 3. 魔数判定照常生效（改名可执行文件拒、真 PNG 放行）。
/// </summary>
public class FileUploadContentGuardTests
{
    private static async Task<(int Status, bool ReachedNext)> InvokeAsync(
        string path, long? contentLength, byte[] body)
    {
        var reachedNext = false;
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.ContentType = "multipart/form-data; boundary=b";
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = contentLength;

        var mw = new FileUploadContentGuardMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }, new NullLogger());
        await mw.InvokeAsync(context);
        return (context.Response.StatusCode, reachedNext);
    }

    private static byte[] MultipartBody(string fileName, byte[] content)
    {
        using var ms = new MemoryStream();
        ms.Write(Encoding.UTF8.GetBytes(
            $"--b\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n" +
            "Content-Type: application/octet-stream\r\n\r\n"));
        ms.Write(content);
        ms.Write(Encoding.UTF8.GetBytes("\r\n--b--\r\n"));
        return ms.ToArray();
    }

    [Fact]
    public async Task Renamed_executable_as_jpg_is_rejected()
    {
        var body = MultipartBody("shell.jpg", "MZ"u8.ToArray());
        var (status, reached) = await InvokeAsync("/api/file-management/file", body.Length, body);
        status.ShouldBe(400, "EXE 魔数冒充 jpg 必须被内容防线拦下");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Chunked_request_is_also_scanned()
    {
        // chunked（ContentLength=null）同样进扫——分块传输可把改名文件直送模型绑定。
        var body = MultipartBody("shell.jpg", "MZ"u8.ToArray());
        var (status, reached) = await InvokeAsync("/api/file-management/file", contentLength: null, body);
        status.ShouldBe(400, "chunked 请求必须同样进内容防线");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Legit_png_passes_and_request_continues()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var body = MultipartBody("pic.png", png);
        var (status, reached) = await InvokeAsync("/api/file-management/file", body.Length, body);
        status.ShouldBe(200);
        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task Trailing_slash_path_is_still_guarded()
    {
        var body = MultipartBody("shell.jpg", "MZ"u8.ToArray());
        var (status, reached) = await InvokeAsync("/api/file-management/file/", body.Length, body);
        status.ShouldBe(400, "NV-16：尾斜杠变体必须同样进守卫（TV-06b 修复面）");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Non_upload_paths_are_not_scanned()
    {
        var body = MultipartBody("shell.jpg", "MZ"u8.ToArray());
        var (status, reached) = await InvokeAsync("/api/other/upload", body.Length, body);
        status.ShouldBe(200);
        reached.ShouldBeTrue("非守卫路径不拦截（400 语义归各端点）");
    }

    private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger<FileUploadContentGuardMiddleware>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
