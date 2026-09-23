using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Localization;

public interface ILanguageAppService : IApplicationService
{
    Task<ListResultDto<LanguageDto>> GetListAsync();

    Task<LanguageDto> GetAsync(Guid id);

    Task<LanguageDto> CreateAsync(CreateLanguageDto input);

    Task<LanguageDto> UpdateAsync(Guid id, UpdateLanguageDto input);

    Task DeleteAsync(Guid id);

    Task SetAsDefaultAsync(Guid id);
}
