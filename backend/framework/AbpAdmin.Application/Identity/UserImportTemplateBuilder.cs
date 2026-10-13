using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导入模板构建器（对标 ruoyi EasyExcel 的 @ExcelColumnSelect 下拉模板）。
/// MiniExcel 1.45 无数据验证能力（github.com/mini-software/MiniExcel#845），
/// 模板生成单独用 ClosedXML；导入解析/导出主链路仍走 MiniExcel。
/// 注意：Excel 数据验证只拦手工输入，粘贴可绕过——服务端 ParseImportBoolean 才是最终闸门。
/// </summary>
public class UserImportTemplateBuilder : IUserImportTemplateBuilder, ITransientDependency
{
    /// <summary>下拉/校验覆盖的行数。超出部分靠服务端校验兜底（Excel 验证本身可被粘贴绕过）。</summary>
    private const int ValidationRowCount = 1000;

    private const string SheetName = "用户导入";
    private const string ReferenceSheetName = "参考数据";

    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;

    public UserImportTemplateBuilder(
        IIdentityRoleRepository roleRepository,
        IOrganizationUnitRepository organizationUnitRepository)
    {
        _roleRepository = roleRepository;
        _organizationUnitRepository = organizationUnitRepository;
    }

    public virtual async Task<byte[]> BuildAsync()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        var headers = new[]
        {
            UserImportColumnNames.UserName,
            UserImportColumnNames.Name,
            UserImportColumnNames.Surname,
            UserImportColumnNames.Email,
            UserImportColumnNames.PhoneNumber,
            UserImportColumnNames.Password,
            UserImportColumnNames.RoleNames,
            UserImportColumnNames.OrganizationUnitCodes,
            UserImportColumnNames.IsActive,
            UserImportColumnNames.IsExternal,
        };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
        }

        // 示例行（与表头同一份列常量）
        sheet.Cell(2, 1).Value = "zhangsan";
        sheet.Cell(2, 2).Value = "三";
        sheet.Cell(2, 3).Value = "张";
        sheet.Cell(2, 4).Value = "zhangsan@example.com";
        sheet.Cell(2, 5).Value = "13800138000";
        sheet.Cell(2, 6).Value = "P@ssw0rd123";
        sheet.Cell(2, 7).Value = "";
        sheet.Cell(2, 8).Value = "";
        sheet.Cell(2, 9).Value = "是";
        sheet.Cell(2, 10).Value = "否";
        sheet.SheetView.FreezeRows(1);

        var roles = (await _roleRepository.GetListAsync())
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var ouCodes = (await _organizationUnitRepository.GetListAsync())
            .Select(x => x.Code)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        AddBooleanValidation(sheet, ColumnIndex(headers, UserImportColumnNames.IsActive));
        AddBooleanValidation(sheet, ColumnIndex(headers, UserImportColumnNames.IsExternal));

        var referenceSheet = workbook.Worksheets.Add(ReferenceSheetName);
        var roleRange = WriteReferenceColumn(referenceSheet, 1, "可选角色", roles);
        var ouRange = WriteReferenceColumn(referenceSheet, 2, "可选组织单元编码", ouCodes);
        referenceSheet.Hide();

        // 角色/组织单元支持分号分隔多值：硬校验会挡住合法组合，用咨询式下拉（可从下拉选、也可手输，不弹错）
        AddAdvisoryListValidation(sheet, ColumnIndex(headers, UserImportColumnNames.RoleNames), roleRange);
        AddAdvisoryListValidation(sheet, ColumnIndex(headers, UserImportColumnNames.OrganizationUnitCodes), ouRange);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static int ColumnIndex(string[] headers, string name) => Array.IndexOf(headers, name) + 1;

    /// <summary>布尔列硬校验：只能手输/选择「是/否」，其它值 Excel 直接拒绝。</summary>
    private static void AddBooleanValidation(IXLWorksheet sheet, int column)
    {
        var validation = sheet.Range(2, column, ValidationRowCount, column).CreateDataValidation();
        validation.AllowedValues = XLAllowedValues.List;
        // OOXML 内联列表必须是带引号的字符串字面量（Excel 自己生成的 XML 是 "是,否"）；
        // 不加引号会被 Excel 当非法公式在加载时丢弃整个验证（审查实测 ClosedXML 原样落盘）
        validation.List($"\"{UserImportBooleanTexts.Yes},{UserImportBooleanTexts.No}\"");
        validation.ShowErrorMessage = true;
        validation.ErrorStyle = XLErrorStyle.Stop;
        validation.ErrorTitle = "无效的取值";
        validation.ErrorMessage = $"本列只允许填写：{UserImportBooleanTexts.Yes} / {UserImportBooleanTexts.No}";
        validation.ShowInputMessage = true;
        validation.InputTitle = "布尔列";
        validation.InputMessage = $"请从下拉选择：{UserImportBooleanTexts.Yes} / {UserImportBooleanTexts.No}";
    }

    /// <summary>咨询式下拉：下拉可选参考值，但允许手输任意值（多值场景），不弹错。</summary>
    private static void AddAdvisoryListValidation(IXLWorksheet sheet, int column, string referenceRange)
    {
        var validation = sheet.Range(2, column, ValidationRowCount, column).CreateDataValidation();
        validation.AllowedValues = XLAllowedValues.List;
        validation.List(referenceRange);
        validation.ShowErrorMessage = false;
        validation.ShowInputMessage = true;
        validation.InputTitle = "参考值";
        validation.InputMessage = "可从下拉选择；多值用分号(;)分隔。参考数据见隐藏 sheet「参考数据」。";
    }

    /// <summary>隐藏 sheet 写一列参考值，返回数据验证可引用的 A1 引用式范围。</summary>
    private static string WriteReferenceColumn(IXLWorksheet sheet, int column, string title, List<string> values)
    {
        sheet.Cell(1, column).Value = title;
        for (var i = 0; i < values.Count; i++)
        {
            sheet.Cell(i + 2, column).Value = values[i];
        }

        var columnLetter = sheet.Cell(1, column).Address.ColumnLetter;
        // 空列表也引用到第 2 行（空单元格），保证引用式合法
        var lastRow = Math.Max(values.Count + 1, 2);
        return $"{ReferenceSheetName}!${columnLetter}$2:${columnLetter}${lastRow}";
    }
}
