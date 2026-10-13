using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Imaging;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 图片处理（SkiaSharp 压缩/缩放）配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class ImagingConfigurator
{
    public static void ConfigureImaging(this IServiceCollection services, IConfiguration configuration)
    {
        // T3.1: 图片处理。provider = SkiaSharp（MIT），不用 ImageSharp（Split License）。
        // 已核实（10.6.0 程序集反射）：SkiaSharpResizerOptions 的成员名是 SKSamplingOptions，
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

        services.Configure<Imaging.AbpAdminImagingOptions>(configuration.GetSection(Imaging.AbpAdminImagingOptions.SectionName));
    }
}
