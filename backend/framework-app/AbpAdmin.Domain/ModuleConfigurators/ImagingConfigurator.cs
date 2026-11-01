using AbpAdmin.Imaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Imaging;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 图片处理（SkiaSharp 压缩/缩放）配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class ImagingConfigurator
{
    public static void ConfigureImaging(this IServiceCollection services, IConfiguration configuration)
    {
        // 图片处理。provider = SkiaSharp（MIT），不用 ImageSharp（Split License）。
        // SkiaSharpResizerOptions 的成员名是 SKSamplingOptions（10.6.0 程序集反射核实），
        // Quality 默认 75；SkiaSharpCompressOptions 只有 Quality（默认 75）。
        services.Configure<SkiaSharpCompressOptions>(options =>
        {
            options.Quality = configuration.GetValue<int?>("Imaging:CompressQuality") ?? 80;
        });

        services.Configure<SkiaSharpResizerOptions>(options =>
        {
            options.Quality = configuration.GetValue<int?>("Imaging:ResizeQuality") ?? 80;
        });

        services.Configure<ImageResizeOptions>(options =>
        {
            options.DefaultResizeMode = ImageResizeMode.Max;
        });

        // 替换 ABP 自带的 Skia contributor：原实现的 SKCodec.Create(Stream)/SKBitmap.Decode(Stream)
        // 留下 GC 孤儿 SKManagedStream，外层先 Dispose 托管流后 finalizer 上的原生读回调命中
        // 已关闭流 → finalizer 线程未处理异常打崩进程（Windows 时序躲过、Linux 稳定触发；
        // 生产容器=Linux，头像上传即暴露面）。安全版语义逐项镜像、解码走原生 SKData，
        // 详见 SafeSkiaImageContributors 头注释。
        services.Replace(ServiceDescriptor.Transient<IImageResizerContributor, SafeSkiaSharpImageResizerContributor>());
        services.Replace(ServiceDescriptor.Transient<IImageCompressorContributor, SafeSkiaSharpImageCompressorContributor>());

        services.Configure<Imaging.AbpAdminImagingOptions>(configuration.GetSection(Imaging.AbpAdminImagingOptions.SectionName));
    }
}
