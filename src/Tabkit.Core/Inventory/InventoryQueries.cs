using System.Collections.Generic;

namespace Tabkit.Core.Inventory;

/// <summary>
/// High-level queries against an <see cref="InventoryStore"/>.
/// </summary>
public static class InventoryQueries
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WorkbooksWithEmbeddedCredentials(
        InventoryStore store)
    {
        return store.Execute(@"
            SELECT DISTINCT w.path, w.version, ds.name AS datasource, c.server, c.dbname
            FROM workbooks w
            JOIN datasources ds ON ds.workbook_id = w.id
            JOIN connections c  ON c.datasource_id = ds.id
            WHERE c.has_password = 1
            ORDER BY w.path");
    }

    /// <summary>
    /// Match by exact dbname, substring of dbname, substring of datasource
    /// name, or substring inside a calculated-field formula. Closing the
    /// docstring-vs-reality gap Codex flagged in the Python port.
    /// </summary>
    /// <remarks>
    /// User input is parameter-bound (no injection risk) but escaping LIKE
    /// metacharacters <c>%</c> and <c>_</c> means searching for a table named
    /// e.g. <c>customer_data</c> matches that literal substring rather than
    /// also matching <c>customerXdata</c> (Codex R1 finding #10). The
    /// <c>ESCAPE '\'</c> clause names the literal escape character.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WorkbooksReferencingTable(
        InventoryStore store, string table)
    {
        var escaped = EscapeLikePattern(table);
        var like = "%" + escaped + "%";
        return store.Execute(@"
            SELECT DISTINCT w.path, ds.name AS datasource, c.server, c.dbname,
                   CASE
                       WHEN c.dbname = $exact OR c.dbname LIKE $like ESCAPE '\' THEN 'connection'
                       WHEN ds.name  LIKE $like ESCAPE '\'                       THEN 'datasource'
                       ELSE                                                'formula'
                   END AS matched_via
            FROM workbooks w
            JOIN datasources ds ON ds.workbook_id = w.id
            LEFT JOIN connections c ON c.datasource_id = ds.id
            WHERE
                  c.dbname = $exact
               OR c.dbname LIKE $like ESCAPE '\'
               OR ds.name  LIKE $like ESCAPE '\'
               OR EXISTS (
                    SELECT 1
                    FROM fields f
                    WHERE f.datasource_id = ds.id
                      AND f.is_calculated = 1
                      AND f.calc_formula LIKE $like ESCAPE '\'
                  )
            ORDER BY w.path",
            ("$exact", table),
            ("$like", like));
    }

    private static string EscapeLikePattern(string input) =>
        input.Replace("\\", "\\\\")
             .Replace("%",  "\\%")
             .Replace("_",  "\\_");

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WorkbooksUsingUsername(
        InventoryStore store)
    {
        return store.Execute(@"
            SELECT DISTINCT w.path, ds.name AS datasource, f.name AS field
            FROM workbooks w
            JOIN datasources ds ON ds.workbook_id = w.id
            JOIN fields f       ON f.datasource_id = ds.id
            WHERE f.references_username = 1
            ORDER BY w.path");
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ServerSummary(
        InventoryStore store)
    {
        return store.Execute(@"
            SELECT c.server, COUNT(DISTINCT w.id) AS workbook_count, COUNT(*) AS connection_count
            FROM connections c
            JOIN datasources ds ON ds.id = c.datasource_id
            JOIN workbooks w    ON w.id = ds.workbook_id
            WHERE c.server IS NOT NULL
            GROUP BY c.server
            ORDER BY workbook_count DESC, c.server");
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ListWorkbooks(
        InventoryStore store)
    {
        return store.Execute(@"
            SELECT w.path, w.version, w.source_build, w.source_platform,
                   w.size_bytes, w.mtime, w.scanned_at,
                   (SELECT COUNT(*) FROM datasources WHERE workbook_id = w.id) AS datasource_count,
                   (SELECT COUNT(*) FROM worksheets  WHERE workbook_id = w.id) AS worksheet_count,
                   (SELECT COUNT(*) FROM dashboards  WHERE workbook_id = w.id) AS dashboard_count,
                   (SELECT COUNT(*) FROM datasources ds JOIN connections c ON c.datasource_id = ds.id
                    WHERE ds.workbook_id = w.id AND c.has_password = 1) AS embedded_cred_count
            FROM workbooks w
            ORDER BY w.path");
    }
}
