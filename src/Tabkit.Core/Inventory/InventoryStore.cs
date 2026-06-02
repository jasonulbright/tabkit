using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Tabkit.Core.Audit;
using Tabkit.Core.Model;

namespace Tabkit.Core.Inventory;

/// <summary>
/// SQLite store for the workbook inventory. Schema deliberately simple —
/// answers the "where does workbook X reference table Y" questions that
/// don't have any other answer in a Tableau shop.
/// </summary>
public sealed class InventoryStore : IDisposable
{
    public const string Schema = @"
CREATE TABLE IF NOT EXISTS workbooks (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    path            TEXT NOT NULL UNIQUE,
    sha256          TEXT NOT NULL,
    mtime           REAL NOT NULL,
    size_bytes      INTEGER NOT NULL,
    version         TEXT,
    source_build    TEXT,
    source_platform TEXT,
    scanned_at      TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE TABLE IF NOT EXISTS datasources (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    workbook_id     INTEGER NOT NULL REFERENCES workbooks(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    caption         TEXT,
    version         TEXT
);
CREATE TABLE IF NOT EXISTS connections (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    datasource_id   INTEGER NOT NULL REFERENCES datasources(id) ON DELETE CASCADE,
    conn_class      TEXT,
    server          TEXT,
    dbname          TEXT,
    username        TEXT,
    has_password    INTEGER NOT NULL DEFAULT 0,
    filename        TEXT,
    port            TEXT,
    authentication  TEXT
);
CREATE TABLE IF NOT EXISTS fields (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    datasource_id   INTEGER NOT NULL REFERENCES datasources(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    caption         TEXT,
    datatype        TEXT,
    role            TEXT,
    is_calculated   INTEGER NOT NULL DEFAULT 0,
    calc_formula    TEXT,
    references_username INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS worksheets (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    workbook_id     INTEGER NOT NULL REFERENCES workbooks(id) ON DELETE CASCADE,
    name            TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS dashboards (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    workbook_id     INTEGER NOT NULL REFERENCES workbooks(id) ON DELETE CASCADE,
    name            TEXT NOT NULL,
    referenced_worksheets TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_connections_server       ON connections(server);
CREATE INDEX IF NOT EXISTS idx_connections_has_password ON connections(has_password);
CREATE INDEX IF NOT EXISTS idx_connections_dbname       ON connections(dbname);
CREATE INDEX IF NOT EXISTS idx_fields_calculated        ON fields(is_calculated);
CREATE INDEX IF NOT EXISTS idx_fields_username          ON fields(references_username);
CREATE INDEX IF NOT EXISTS idx_workbooks_sha            ON workbooks(sha256);
CREATE INDEX IF NOT EXISTS idx_datasources_workbook     ON datasources(workbook_id);
CREATE INDEX IF NOT EXISTS idx_connections_datasource   ON connections(datasource_id);
CREATE INDEX IF NOT EXISTS idx_fields_datasource        ON fields(datasource_id);
CREATE INDEX IF NOT EXISTS idx_worksheets_workbook      ON worksheets(workbook_id);
CREATE INDEX IF NOT EXISTS idx_dashboards_workbook      ON dashboards(workbook_id);
";

    public string DatabasePath { get; }
    internal SqliteConnection Connection { get; }

    /// <summary>Open / create an inventory store. Pass <c>null</c> for in-memory.</summary>
    public InventoryStore(string? path = null)
    {
        DatabasePath = path ?? ":memory:";
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true,
        };
        Connection = new SqliteConnection(csb.ConnectionString);
        Connection.Open();
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();

    /// <summary>Insert (or refresh) a workbook and all its derived rows. Returns workbook id.</summary>
    public long UpsertWorkbook(
        string path, string sha256, double mtime, long sizeBytes, Workbook wb)
    {
        using var tx = Connection.BeginTransaction();
        try
        {
            using (var del = Connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM workbooks WHERE path = $path";
                del.Parameters.AddWithValue("$path", path);
                del.ExecuteNonQuery();
            }

            long wbId;
            using (var ins = Connection.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT INTO workbooks
                      (path, sha256, mtime, size_bytes, version, source_build, source_platform)
                    VALUES ($path, $sha, $mtime, $size, $ver, $build, $plat);
                    SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("$path",  path);
                ins.Parameters.AddWithValue("$sha",   sha256);
                ins.Parameters.AddWithValue("$mtime", mtime);
                ins.Parameters.AddWithValue("$size",  sizeBytes);
                ins.Parameters.AddWithValue("$ver",   (object?)wb.Version          ?? DBNull.Value);
                ins.Parameters.AddWithValue("$build", (object?)wb.SourceBuild      ?? DBNull.Value);
                ins.Parameters.AddWithValue("$plat",  (object?)wb.SourcePlatform   ?? DBNull.Value);
                wbId = (long)(ins.ExecuteScalar() ?? 0L);
            }

            foreach (var ds in wb.DataSources)
            {
                long dsId;
                using (var dsCmd = Connection.CreateCommand())
                {
                    dsCmd.Transaction = tx;
                    dsCmd.CommandText = @"
                        INSERT INTO datasources (workbook_id, name, caption, version)
                        VALUES ($wb, $name, $caption, $version);
                        SELECT last_insert_rowid();";
                    dsCmd.Parameters.AddWithValue("$wb",      wbId);
                    dsCmd.Parameters.AddWithValue("$name",    ds.Name);
                    dsCmd.Parameters.AddWithValue("$caption", (object?)ds.Caption ?? DBNull.Value);
                    dsCmd.Parameters.AddWithValue("$version", (object?)ds.Version ?? DBNull.Value);
                    dsId = (long)(dsCmd.ExecuteScalar() ?? 0L);
                }

                foreach (var c in ds.Connections)
                {
                    using var cn = Connection.CreateCommand();
                    cn.Transaction = tx;
                    cn.CommandText = @"
                        INSERT INTO connections
                          (datasource_id, conn_class, server, dbname, username,
                           has_password, filename, port, authentication)
                        VALUES ($ds, $cls, $srv, $db, $un, $pw, $fn, $port, $auth);";
                    cn.Parameters.AddWithValue("$ds",   dsId);
                    cn.Parameters.AddWithValue("$cls",  (object?)c.ConnectionClass ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$srv",  (object?)c.Server          ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$db",   (object?)c.Dbname          ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$un",   (object?)c.Username        ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$pw",   !string.IsNullOrEmpty(c.Password) ? 1 : 0);
                    cn.Parameters.AddWithValue("$fn",   (object?)c.Filename        ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$port", (object?)c.Port            ?? DBNull.Value);
                    cn.Parameters.AddWithValue("$auth", (object?)c.Authentication  ?? DBNull.Value);
                    cn.ExecuteNonQuery();
                }

                foreach (var f in ds.Fields)
                {
                    var formula = f.Calculation?.Formula;
                    var refsUser =
                        !string.IsNullOrEmpty(formula) &&
                        FormulaDetectors.FormulaReferencesUserIdentity(formula);

                    using var fc = Connection.CreateCommand();
                    fc.Transaction = tx;
                    fc.CommandText = @"
                        INSERT INTO fields
                          (datasource_id, name, caption, datatype, role,
                           is_calculated, calc_formula, references_username)
                        VALUES ($ds, $name, $cap, $dt, $role, $calc, $form, $refu);";
                    fc.Parameters.AddWithValue("$ds",   dsId);
                    fc.Parameters.AddWithValue("$name", f.Name);
                    fc.Parameters.AddWithValue("$cap",  (object?)f.Caption  ?? DBNull.Value);
                    fc.Parameters.AddWithValue("$dt",   (object?)f.Datatype ?? DBNull.Value);
                    fc.Parameters.AddWithValue("$role", (object?)f.Role     ?? DBNull.Value);
                    fc.Parameters.AddWithValue("$calc", f.Calculation is not null ? 1 : 0);
                    fc.Parameters.AddWithValue("$form", (object?)formula    ?? DBNull.Value);
                    fc.Parameters.AddWithValue("$refu", refsUser ? 1 : 0);
                    fc.ExecuteNonQuery();
                }
            }

            foreach (var ws in wb.Worksheets)
            {
                using var w = Connection.CreateCommand();
                w.Transaction = tx;
                w.CommandText = "INSERT INTO worksheets (workbook_id, name) VALUES ($wb, $name);";
                w.Parameters.AddWithValue("$wb",   wbId);
                w.Parameters.AddWithValue("$name", ws.Name);
                w.ExecuteNonQuery();
            }

            foreach (var d in wb.Dashboards)
            {
                using var dc = Connection.CreateCommand();
                dc.Transaction = tx;
                dc.CommandText = @"
                    INSERT INTO dashboards (workbook_id, name, referenced_worksheets)
                    VALUES ($wb, $name, $refs);";
                var sortedRefs = new List<string>(d.ReferencedWorksheets);
                sortedRefs.Sort(StringComparer.Ordinal);
                dc.Parameters.AddWithValue("$wb",   wbId);
                dc.Parameters.AddWithValue("$name", d.Name);
                dc.Parameters.AddWithValue("$refs", string.Join(",", sortedRefs));
                dc.ExecuteNonQuery();
            }

            tx.Commit();
            return wbId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Run a parameterized query and return rows as dicts.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Execute(
        string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Index-wide counts.</summary>
    public IReadOnlyDictionary<string, long> Stats()
    {
        long Count(string sql)
        {
            using var c = Connection.CreateCommand();
            c.CommandText = sql;
            return (long)(c.ExecuteScalar() ?? 0L);
        }

        return new Dictionary<string, long>
        {
            ["workbooks"]   = Count("SELECT COUNT(*) FROM workbooks"),
            ["datasources"] = Count("SELECT COUNT(*) FROM datasources"),
            ["connections"] = Count("SELECT COUNT(*) FROM connections"),
            ["fields"]      = Count("SELECT COUNT(*) FROM fields"),
            ["worksheets"]  = Count("SELECT COUNT(*) FROM worksheets"),
            ["dashboards"]  = Count("SELECT COUNT(*) FROM dashboards"),
            ["with_embedded_creds"] = Count(@"
                SELECT COUNT(DISTINCT workbook_id) FROM datasources ds
                JOIN connections c ON c.datasource_id = ds.id
                WHERE c.has_password = 1"),
        };
    }
}
