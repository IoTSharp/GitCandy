using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GitCandy.Data.Configuration;

/// <summary>
/// 从 ASP.NET Core 配置读取数据库选项。
/// </summary>
public static class GitCandyDatabaseOptionsReader
{
    /// <summary>
    /// 读取 GitCandy 数据库配置。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <returns>解析后的数据库配置。</returns>
    public static GitCandyDatabaseOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(GitCandyDatabaseOptions.SectionName);
        var options = new GitCandyDatabaseOptions();

        var providerName = FirstValue(
            section["Provider"],
            section["DataBase"],
            section["Database"],
            configuration["DataBase"],
            configuration["Database"]);

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            if (!GitCandyDatabaseProviderParser.TryParse(providerName, out var provider))
            {
                throw new NotSupportedException(
                    $"GitCandy database provider '{providerName}' is not supported.");
            }

            options.Provider = provider;
        }

        options.ConnectionStringName = FirstValue(
                section["ConnectionStringName"],
                configuration["GitCandy:ConnectionStringName"])
            ?? GitCandyDatabaseOptions.DefaultConnectionStringName;

        if (TryReadPositiveInt(section["DbContextPoolSize"], out var poolSize)
            || TryReadPositiveInt(section["PoolSize"], out poolSize)
            || TryReadPositiveInt(configuration["DbContextPoolSize"], out poolSize))
        {
            options.DbContextPoolSize = poolSize;
        }

        if (bool.TryParse(section["EnableSensitiveDataLogging"], out var enableSensitiveDataLogging))
        {
            options.EnableSensitiveDataLogging = enableSensitiveDataLogging;
        }

        options.ConnectionString = ResolveConnectionString(configuration, section, options.ConnectionStringName);

        return options;
    }

    private static string ResolveConnectionString(
        IConfiguration configuration,
        IConfiguration section,
        string connectionStringName)
    {
        var configured = FirstValue(
            section["ConnectionString"],
            configuration.GetConnectionString(connectionStringName),
            configuration.GetConnectionString(GitCandyDatabaseOptions.DefaultConnectionStringName),
            configuration.GetConnectionString(GitCandyDatabaseOptions.LegacyConnectionStringName));

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return "Data Source=App_Data/GitCandy.db";
    }

    private static string? FirstValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadPositiveInt(string? value, out int result)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result)
            && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }
}
