namespace AbpAdmin;

/// <summary>
/// 电子表格公式注入加固（OWASP CSV Injection 的 xlsx 对应物，安全审计 L-4）：
/// 对以 = + - @ Tab CR 开头的单元格值加 ' 前缀，让危险字符以字面量呈现。
/// 导出的文本单元格（用户名/邮箱/URL/客户端 IP/异常消息等）多来自用户可控输入；
/// MiniExcel 当前写 inline string 不可利用，但输出格式或写入方式一旦变更（如改 CSV）
/// 即成公式注入面——所有导出构建器统一走本助手（首个使用者
/// IdentityUserAdminAppService 的同名私有方法收拢于此）。
/// </summary>
public static class SpreadsheetTextSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }
}
