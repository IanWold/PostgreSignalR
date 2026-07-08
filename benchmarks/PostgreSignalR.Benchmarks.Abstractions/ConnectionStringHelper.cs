using Npgsql;
using StackExchange.Redis;

namespace PostgreSignalR.Benchmarks.Abstractions;

public static class ConnectionStringHelper
{
    private static bool HasUriScheme(string value, params string[] schemes) =>
        schemes.Any(scheme => value.StartsWith(scheme + "://", StringComparison.OrdinalIgnoreCase));

    public static string NormalizePostgres(string value)
    {
        if (!HasUriScheme(value, "postgres", "postgresql"))
        {
            return value;
        }

        var uri = new Uri(value);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        var userInfo = uri.UserInfo.Split(':', 2);

        if (userInfo.Length > 0 && userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length > 1 && userInfo[1].Length > 0)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);

            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]).Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = Enum.Parse<SslMode>(Uri.UnescapeDataString(parts[1]), ignoreCase: true);
            }
        }

        return builder.ConnectionString;
    }

    public static string NormalizeRedis(string value)
    {
        if (!HasUriScheme(value, "redis", "rediss"))
        {
            return value;
        }

        var uri = new Uri(value);

        var options = new ConfigurationOptions
        {
            Ssl = uri.Scheme == "rediss",
        };

        options.EndPoints.Add(uri.Host, uri.IsDefaultPort ? 6379 : uri.Port);

        var userInfo = uri.UserInfo.Split(':', 2);

        if (userInfo.Length > 0 && userInfo[0].Length > 0)
        {
            options.User = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length > 1 && userInfo[1].Length > 0)
        {
            options.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        return options.ToString();
    }
}
