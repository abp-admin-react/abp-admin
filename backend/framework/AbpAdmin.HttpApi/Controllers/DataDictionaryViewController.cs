using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Controllers;

/// <summary>
/// 字典管理面控制器（T3.4 第 12 步，后经收口扩展为列表/创建/删除）。
/// 应用服务的方法标记了 [RemoteService(false)]，路由由这里钉死：
/// ABP 常规路由只会把名为 id 的参数放进路径（ConventionalRouteBuilder），
/// 规格要求的 /api/app/data-dictionary-view/* 路由只能靠显式 Controller。
/// EasyAbp 模块的控制器（api/data-dictionary/data-dictionary，全量替换、无静态结构锁）
/// 已随模块 HttpApi 依赖移除而整体下线，字典读写只有这里的路由。
/// </summary>
[Route("api/app/data-dictionary-view")]
public class DataDictionaryViewController : AbpAdminController
{
    private readonly IDataDictionaryViewAppService _dataDictionaryViewAppService;

    public DataDictionaryViewController(IDataDictionaryViewAppService dataDictionaryViewAppService)
    {
        _dataDictionaryViewAppService = dataDictionaryViewAppService;
    }

    /// <summary>字典分页列表（管理页左列；不含 Items）。按 Code 排序，需要 DataDictionary.Default 权限。</summary>
    [HttpGet]
    public async Task<PagedResultDto<DataDictionaryListItemDto>> GetListAsync(
        [FromQuery] PagedResultRequestDto input)
    {
        return await _dataDictionaryViewAppService.GetListAsync(input);
    }

    /// <summary>按字典编码取合并视图（带 isStatic 与 tagType）。所有登录用户可读。</summary>
    [HttpGet("{code}")]
    public async Task<DataDictionaryViewDto?> GetAsync(string code)
    {
        return await _dataDictionaryViewAppService.GetAsync(code);
    }

    /// <summary>创建非静态字典（静态字典只能由代码定义，API 不开放 IsStatic）。需要 Create 权限。</summary>
    [HttpPost]
    public async Task<DataDictionaryViewDto> CreateAsync([FromBody] CreateDataDictionaryInput input)
    {
        return await _dataDictionaryViewAppService.CreateAsync(input);
    }

    /// <summary>
    /// 删除字典（静态字典拒绝；非静态字典的展示元数据行随删）。需要 Delete 权限。
    /// </summary>
    [HttpDelete("{code}")]
    public async Task DeleteAsync(string code)
    {
        await _dataDictionaryViewAppService.DeleteAsync(code);
    }

    /// <summary>
    /// 原子保存字典显示信息与全部字典项（含展示元数据，同一工作单元提交）：
    /// 字典写入的唯一入口。Items 是全量替换语义（漏传即删除），静态字典有服务端结构锁，整单拒绝。
    /// </summary>
    [HttpPut("{dictionaryCode}/items")]
    public async Task<DataDictionaryViewDto> SaveItemsAsync(
        string dictionaryCode,
        [FromBody] SaveDataDictionaryItemsInput input)
    {
        return await _dataDictionaryViewAppService.SaveItemsAsync(dictionaryCode, input);
    }
}
