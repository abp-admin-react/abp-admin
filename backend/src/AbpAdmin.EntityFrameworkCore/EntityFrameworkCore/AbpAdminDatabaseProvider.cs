using System;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.EntityFrameworkCore;

public static class AbpAdminDatabaseProvider
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";

    public static string Get(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        return string.IsNullOrWhiteSpace(provider) ? Sqlite : provider.Trim();
    }

    public static bool IsPostgreSql(IConfiguration configuration)
    {
        return Get(configuration).Equals(PostgreSql, StringComparison.OrdinalIgnoreCase);
    }
}
