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

    [Theory]
    [InlineData("/api/file-management/file/many")]
    [InlineData("/api/file-management/file/with-stream")]
    [InlineData("/api/file-management/file/many/with-stream")]
    [InlineData("/api/file-management/file/with-bytes")]
    [InlineData("/api/file-management/file/many/with-bytes")]
    [InlineData("/api/file-management/file/some-future-route")]
    public async Task Sibling_upload_routes_are_also_guarded(string path)
    {
        // sectest b02 / 审计 NV-08 残余：EasyAbp 5.12.0 FileController 有 5 条内容接收路由，
        // 逐条枚举会被包升级新增路由绕过——门限按「基路由 + 子路径」前缀收口后，
        // 任何 POST 子路径（含未来新增）一律进内容扫描。
        var body = MultipartBody("shell.jpg", "MZ"u8.ToArray());
        var (status, reached) = await InvokeAsync(path, body.Length, body);
        status.ShouldBe(400, $"{path} 必须进内容防线（前缀收口，包升级新增路由不再成为绕过面）");
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

    // ---- JSON 字节通道（with-bytes / many/with-bytes）：content/fileContents 以 base64 进 JSON，
    // ---- sectest 加压轮实证可把 MZ 改名文件直送 blob 存储绕过 multipart-only 门——与 multipart 同扫。

    private static async Task<(int Status, bool ReachedNext)> InvokeJsonAsync(
        string path, byte[] body, string? contentType = "application/json")
    {
        var reachedNext = false;
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        if (contentType != null)
        {
            context.Request.ContentType = contentType;
        }
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;

        var mw = new FileUploadContentGuardMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        }, new NullLogger());
        await mw.InvokeAsync(context);
        return (context.Response.StatusCode, reachedNext);
    }

    private static byte[] JsonBody(string json) => Encoding.UTF8.GetBytes(json);

    private static string B64(string content) => Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task With_bytes_json_mz_as_jpg_is_rejected()
    {
        var body = JsonBody(
            "{\"fileContainerName\":\"admin\",\"fileType\":2,\"generateUniqueFileName\":false," +
            $"\"fileName\":\"shell.jpg\",\"content\":\"{B64("MZ")}\"}}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body);
        status.ShouldBe(400, "JSON 字节通道的 MZ 改名文件必须与 multipart 同口径被拦");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Many_with_bytes_json_array_is_rejected()
    {
        var body = JsonBody(
            "{\"fileContainerName\":\"admin\",\"generateUniqueFileName\":false," +
            $"\"fileName\":[\"a.png\",\"shell.jpg\"],\"fileContents\":[\"{B64("MZ")}\"]}}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/many/with-bytes", body);
        status.ShouldBe(400, "数组形态的 fileContents 同样进内容扫描");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task With_bytes_json_legit_png_passes()
    {
        var png = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var body = JsonBody(
            "{\"fileContainerName\":\"admin\",\"fileType\":2,\"generateUniqueFileName\":false," +
            $"\"fileName\":\"pic.png\",\"content\":\"{png}\"}}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body);
        status.ShouldBe(200);
        reached.ShouldBeTrue("合法 PNG 走 JSON 通道放行（后续模型绑定照常）");
    }

    [Fact]
    public async Task With_bytes_json_scriptable_svg_is_rejected()
    {
        var body = JsonBody(
            "{\"fileContainerName\":\"admin\",\"fileType\":2,\"generateUniqueFileName\":false," +
            $"\"fileName\":\"x.svg\",\"content\":\"{B64("<script>alert(1)</script>")}\"}}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body);
        status.ShouldBe(400, "JSON 通道的含脚本 SVG 与 multipart 同口径被拦");
        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task Json_invalid_body_falls_through()
    {
        var body = JsonBody("this is not json");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body);
        status.ShouldBe(200);
        reached.ShouldBeTrue("非法 JSON 交由后续模型绑定报错，守卫不拦截");
    }

    [Fact]
    public async Task Json_non_upload_content_type_is_not_scanned()
    {
        // 守卫只覆盖「能承载文件内容」的两条通道（multipart 文件段 / JSON 字节），
        // 其余 Content-Type 不进扫描——受理/415 语义归端点，与 multipart 门同口径。
        var body = JsonBody("{\"fileName\":\"shell.jpg\",\"content\":\"" + B64("MZ") + "\"}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body,
            contentType: "text/plain");
        status.ShouldBe(200);
        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task Json_zero_byte_content_with_extension_is_rejected()
    {
        var body = JsonBody(
            "{\"fileContainerName\":\"admin\",\"fileType\":2,\"generateUniqueFileName\":false," +
            "\"fileName\":\"empty.jpg\",\"content\":\"\"}");
        var (status, reached) = await InvokeJsonAsync("/api/file-management/file/with-bytes", body);
        status.ShouldBe(400, "零字节内容与 multipart 口径一致被拦");
        reached.ShouldBeFalse();
    }

    private sealed class NullLogger : Microsoft.Extensions.Logging.ILogger<FileUploadContentGuardMiddleware>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
