using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;

namespace AbpAdmin.Imaging;

/// <summary>
/// 流复制扩展。名字刻意与 BCL 的 CopyToAsync 区分开——
/// Stream.CopyToAsync(Stream, int) 的第二个参数是缓冲区大小而不是字节上限，
/// 把上限传给它能编译、能跑，但一个字节都拦不住（规格 T3.1 第 5 步的易错点）。
/// </summary>
public static class LimitedStreamExtensions
{
    /// <summary>
    /// 把 source 复制到 destination，读到的字节总数一旦超过 maxBytes 立刻抛
    /// BusinessException(AvatarTooLarge)（不会先把整个流读完再判断）。
    /// destination 中可能已写入不超过 maxBytes 的前缀内容，调用方应按失败丢弃。
    /// </summary>
    public static async Task LimitedCopyToAsync(
        this Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (total + read > maxBytes)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.AvatarTooLarge)
                    .WithData("MaxSize", maxBytes);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }
    }
}
