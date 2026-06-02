using System;
using System.Data;

namespace Tabkit.Core.Extract.Sources;

/// <summary>
/// SQL Server source. Uses Microsoft.Data.SqlClient if available — tabkit
/// does NOT take a hard dependency on that package, so users who need MSSQL
/// must install it in their host (e.g. via the CLI / App that references this
/// library). The source loads the type reflectively to avoid the runtime
/// dependency on hosts that never touch MSSQL.
/// </summary>
public sealed class MssqlSource : ISource
{
    public string Query { get; init; } = "";
    public string? ConnectionString { get; init; }
    public string? ConnectionStringEnv { get; init; }

    public DataTable Read()
    {
        var conn = ResolveConnectionString();
        var sqlConnType = Type.GetType(
            "Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient", throwOnError: false);
        sqlConnType ??= Type.GetType(
            "System.Data.SqlClient.SqlConnection, System.Data.SqlClient", throwOnError: false);

        if (sqlConnType is null)
            throw new InvalidOperationException(
                "MSSQL source requires Microsoft.Data.SqlClient (recommended) or " +
                "System.Data.SqlClient. Install one of those NuGet packages in the host project.");

        using var sqlConn = (IDbConnection)Activator.CreateInstance(sqlConnType, conn)!;
        sqlConn.Open();
        using var cmd = sqlConn.CreateCommand();
        cmd.CommandText = Query;
        using var reader = cmd.ExecuteReader();
        var dt = new DataTable();
        dt.Load(reader);
        return dt;
    }

    private string ResolveConnectionString()
    {
        if (!string.IsNullOrEmpty(ConnectionString))
            return ConnectionString;
        if (!string.IsNullOrEmpty(ConnectionStringEnv))
        {
            var val = Environment.GetEnvironmentVariable(ConnectionStringEnv);
            if (string.IsNullOrEmpty(val))
                throw new InvalidOperationException(
                    $"env var '{ConnectionStringEnv}' is unset or empty");
            return val;
        }
        throw new InvalidOperationException(
            "mssql source needs either 'ConnectionString' or 'ConnectionStringEnv'");
    }
}
