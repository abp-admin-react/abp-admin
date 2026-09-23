using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AbpAdmin.Imaging;

/// <summary>
/// 图片内容校验门面（T3.1）。业务代码只依赖本接口，不直接碰 FileSignatures。
/// </summary>
public interface IImageContentValidator
{
    /// <summary>
    /// 校验流内容是否与扩展名一致。stream 必须可 Seek；方法返回时 Position 会被复位到调用前的值。
    /// 扩展名不在 allowedExtensions 抛 BusinessException(InvalidImageExtension)；
    /// 内容与扩展名不符（或无法识别）抛 BusinessException(InvalidImageContent)。
    /// </summary>
    Task ValidateAsync(Stream stream, string extension, IReadOnlyList<string> allowedExtensions);
}
