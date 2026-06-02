using System.CommandLine;
using Spectre.Console;
using Tabkit.Cli.Output;
using Tabkit.Core.Inventory;

namespace Tabkit.Cli.Commands;

internal static class InventoryCommand
{
    private static string DefaultDbPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tabkit", "inventory.sqlite");

    public static Command Build()
    {
        var cmd = new Command("inventory", "Scan a tree of Tableau workbooks into a SQLite index and query it.");
        cmd.Subcommands.Add(BuildScan());
        cmd.Subcommands.Add(BuildFind());
        cmd.Subcommands.Add(BuildStats());
        return cmd;
    }

    private static Option<FileInfo> DbOption() => new("--db")
    {
        Description = "Path to the SQLite inventory (default: %LOCALAPPDATA%/Tabkit/inventory.sqlite)",
        DefaultValueFactory = _ => new FileInfo(DefaultDbPath()),
    };

    private static Command BuildScan()
    {
        var rootArg = new Argument<DirectoryInfo>("root") { Description = "Directory to walk for .twb / .twbx" };
        var dbOpt = DbOption();
        var scan = new Command("scan", "Walk a directory tree and index every workbook into the SQLite store.")
        {
            rootArg, dbOpt,
        };
        scan.SetAction(parse =>
        {
            var root = parse.GetValue(rootArg)!;
            var db = parse.GetValue(dbOpt)!;
            if (!root.Exists)
            {
                TextRenderer.RenderError("Not found", root.FullName);
                return 2;
            }
            EnsureDbDir(db.FullName);

            using var store = new InventoryStore(db.FullName);
            var result = InventoryScanner.ScanTree(root.FullName, store);
            Console.WriteLine(
                $"scanned {result.Scanned}, indexed {result.Indexed}, " +
                $"skipped {result.Skipped}, errors {result.Errors.Count}");
            foreach (var (path, message) in result.Errors)
                Console.WriteLine($"  {path}: {message}");
            return result.Errors.Count == 0 ? 0 : 1;
        });
        return scan;
    }

    private static Command BuildFind()
    {
        var dbOpt = DbOption();
        var tableOpt = new Option<string?>("--references-table")
        {
            Description = "List workbooks that reference this table name (substring match).",
        };
        var credsOpt = new Option<bool>("--with-embedded-creds")
        {
            Description = "List workbooks whose connections carry embedded credentials.",
        };
        var usernameOpt = new Option<bool>("--uses-username")
        {
            Description = "List workbooks with USERNAME()-style ad-hoc row-level security calcs.",
        };

        var find = new Command("find", "Query the inventory store.")
        {
            dbOpt, tableOpt, credsOpt, usernameOpt,
        };
        find.SetAction(parse =>
        {
            var db = parse.GetValue(dbOpt)!;
            if (!db.Exists)
            {
                TextRenderer.RenderError("DB not found",
                    $"{db.FullName} — run 'tabkit inventory scan <root>' first");
                return 2;
            }

            using var store = new InventoryStore(db.FullName);
            var table = parse.GetValue(tableOpt);
            var creds = parse.GetValue(credsOpt);
            var username = parse.GetValue(usernameOpt);

            // Only one query mode at a time; if multiple flags are set, error out
            // rather than silently picking one.
            var flags = (table is not null ? 1 : 0) + (creds ? 1 : 0) + (username ? 1 : 0);
            if (flags != 1)
            {
                TextRenderer.RenderError("Invalid query",
                    "Pass exactly one of --references-table, --with-embedded-creds, --uses-username");
                return 2;
            }

            if (table is not null)
                TextRenderer.RenderInventoryQuery(
                    $"workbooks referencing '{table}'",
                    InventoryQueries.WorkbooksReferencingTable(store, table));
            else if (creds)
                TextRenderer.RenderInventoryQuery(
                    "workbooks with embedded credentials",
                    InventoryQueries.WorkbooksWithEmbeddedCredentials(store));
            else if (username)
                TextRenderer.RenderInventoryQuery(
                    "workbooks using USERNAME() / ISMEMBEROF()",
                    InventoryQueries.WorkbooksUsingUsername(store));

            return 0;
        });
        return find;
    }

    private static Command BuildStats()
    {
        var dbOpt = DbOption();
        var stats = new Command("stats", "Print summary counts (workbooks, datasources, connections, fields).")
        {
            dbOpt,
        };
        stats.SetAction(parse =>
        {
            var db = parse.GetValue(dbOpt)!;
            if (!db.Exists)
            {
                TextRenderer.RenderError("DB not found",
                    $"{db.FullName} — run 'tabkit inventory scan <root>' first");
                return 2;
            }
            using var store = new InventoryStore(db.FullName);
            var s = store.Stats();
            var table = new Table
            {
                Border = TableBorder.Rounded,
                Title = new TableTitle("[bold]inventory stats[/]"),
            };
            table.AddColumn("metric");
            table.AddColumn(new TableColumn("count").RightAligned());
            foreach (var (k, v) in s)
                table.AddRow(Markup.Escape(k), v.ToString());
            AnsiConsole.Write(table);
            return 0;
        });
        return stats;
    }

    private static void EnsureDbDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
