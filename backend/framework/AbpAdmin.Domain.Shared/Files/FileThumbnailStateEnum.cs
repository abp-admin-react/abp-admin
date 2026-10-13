using System.ComponentModel;
using AbpAdmin.DataDictionaries;

namespace AbpAdmin.Files;

/// <summary>
/// 缩略图生成状态机（T3.1）。
/// Failed 不会被回填自动重试（永久失败留痕，人工处置）；Pending 会被回填重新入队。
/// T3.4：从 Domain 层移到 Domain.Shared 并以 Enum 结尾——字典同步只扫
/// Domain.Shared 与 Application.Contracts 两个程序集，移过来才会物化成
/// 静态数据字典（字典编码 FileThumbnailState）。
/// </summary>
[Description("文件缩略图状态")]
public enum FileThumbnailStateEnum : byte
{
    [Description("待处理")]
    [DictionaryTag(DataDictionaryTagTypes.Processing)]
    Pending = 0,

    [Description("完成")]
    [DictionaryTag(DataDictionaryTagTypes.Success)]
    Done = 1,

    [Description("不支持")]
    [DictionaryTag(DataDictionaryTagTypes.Warning)]
    Unsupported = 2,

    [Description("失败")]
    [DictionaryTag(DataDictionaryTagTypes.Error)]
    Failed = 3
}
