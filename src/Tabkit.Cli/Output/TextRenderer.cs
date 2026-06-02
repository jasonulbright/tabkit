using Spectre.Console;
using Tabkit.Core.Audit;

namespace Tabkit.Cli.Output;

/// <summary>
/// Renders audit / inventory / extract output to the terminal via Spectre.Console.
/// Plain text fallback when redirected (Spectre handles this internally via
/// AnsiConsole, but we expose simple helpers so callers don't reach for
/// Console.WriteLine ad-hoc.)
/// </summary>
internal static class TextRenderer
{
    public static void RenderFindings(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Clean.[/] No findings.");
            return;
        }

        var table = new Table
        {
            Border = TableBorder.Rounded,
            Title = new TableTitle("[bold]tabkit audit[/]"),
        };
        table.AddColumn(new TableColumn("") { NoWrap = true, Width = 2 });
        table.AddColumn(new TableColumn("ID") { NoWrap = true });
        table.AddColumn(new TableColumn("Severity") { NoWrap = true });
        table.AddColumn(new TableColumn("Title"));
        table.AddColumn(new TableColumn("Message"));

        foreach (var f in findings.OrderByDescending(f => f.Severity).ThenBy(f => f.RuleId))
        {
            table.AddRow(
                new Markup(GlyphFor(f.Severity)),
                new Markup(Markup.Escape(f.RuleId)),
                new Markup(Markup.Escape(f.Severity.ToString().ToLowerInvariant())),
                new Markup(Markup.Escape(f.Title)),
                new Markup(Markup.Escape(f.Message)));
        }
        AnsiConsole.Write(table);

        var error = findings.Count(f => f.Severity == Severity.Error);
        var warn  = findings.Count(f => f.Severity == Severity.Warn);
        var info  = findings.Count(f => f.Severity == Severity.Info);
        AnsiConsole.MarkupLine($"\n{findings.Count} finding(s): {error} error, {warn} warn, {info} info.");
    }

    public static void RenderInventoryQuery(
        string title,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(title)}:[/] no rows.");
            return;
        }

        var table = new Table
        {
            Border = TableBorder.Rounded,
            Title = new TableTitle($"[bold]{Markup.Escape(title)}[/]"),
        };

        var columns = rows[0].Keys.ToList();
        foreach (var col in columns)
            table.AddColumn(col);

        foreach (var row in rows)
            table.AddRow(columns.Select(c => Markup.Escape(row[c]?.ToString() ?? "")).ToArray());

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n{rows.Count} row(s).");
    }

    public static void RenderError(string title, string message)
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(title)}:[/] {Markup.Escape(message)}");
    }

    private static string GlyphFor(Severity s) => s switch
    {
        Severity.Error => "[red]✗[/]",
        Severity.Warn  => "[yellow]⚠[/]",
        Severity.Info  => "[blue]ⓘ[/]",
        _              => "·",
    };

    /// <summary>
    /// Map a finding set to the documented exit code:
    /// 2 = any error, 1 = any warning (and no error), 0 = clean / info-only.
    /// </summary>
    public static int ExitCodeFor(IReadOnlyList<Finding> findings)
    {
        if (findings.Any(f => f.Severity == Severity.Error)) return 2;
        if (findings.Any(f => f.Severity == Severity.Warn))  return 1;
        return 0;
    }
}
