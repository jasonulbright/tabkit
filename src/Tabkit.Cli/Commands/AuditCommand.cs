using System.CommandLine;
using Spectre.Console;
using Tabkit.Cli.Output;
using Tabkit.Core.Audit;
using Tabkit.Core.Diff;
using Tabkit.Core.Loading;
using Tabkit.Core.Output;

namespace Tabkit.Cli.Commands;

internal static class AuditCommand
{
    public static Command Build()
    {
        var cmd = new Command("audit", "Run audit rules against a Tableau workbook.");
        cmd.Subcommands.Add(BuildRun());
        cmd.Subcommands.Add(BuildDiff());
        cmd.Subcommands.Add(BuildInspect());
        return cmd;
    }

    private static Command BuildRun()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = ".twb or .twbx workbook to audit",
        };
        var packOpt = new Option<string>("--pack")
        {
            Description = "Rule pack to run (all | audit | governance)",
            DefaultValueFactory = _ => "all",
        };
        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Output format (text | json | sarif | html)",
            DefaultValueFactory = _ => "text",
        };
        var outOpt = new Option<FileInfo?>("--out", "-o")
        {
            Description = "Write output to this path (default: stdout for text/json, required for sarif/html)",
        };

        var run = new Command("run", "Run all selected rules against a single workbook.")
        {
            fileArg, packOpt, formatOpt, outOpt,
        };
        run.SetAction(parse =>
        {
            var file = parse.GetValue(fileArg)!;
            var pack = parse.GetValue(packOpt);
            var format = parse.GetValue(formatOpt) ?? "text";
            var outPath = parse.GetValue(outOpt);

            if (!file.Exists)
            {
                TextRenderer.RenderError("Not found", file.FullName);
                return 2;
            }

            var wb = TwbLoader.Load(file.FullName);
            var registry = RuleRegistry.Default();
            var packArg = string.Equals(pack, "all", StringComparison.OrdinalIgnoreCase) ? null : pack;
            IReadOnlyList<Tabkit.Core.Audit.Finding> findings;
            try
            {
                findings = registry.Run(wb, packArg);
            }
            catch (ArgumentException ex)
            {
                TextRenderer.RenderError("Unknown pack", ex.Message);
                return 2;
            }

            // Whitelist the format BEFORE the switch so typos like --format sariff
            // fail loudly instead of silently falling through to text and exit 0
            // (Codex R1 round-2 finding — quiet CI footgun).
            var knownFormats = new[] { "text", "json", "sarif", "html" };
            var formatLower = format.ToLowerInvariant();
            if (Array.IndexOf(knownFormats, formatLower) < 0)
            {
                TextRenderer.RenderError(
                    "Unknown format",
                    $"'{format}' is not one of: {string.Join(", ", knownFormats)}");
                return 2;
            }

            switch (formatLower)
            {
                case "json":
                    WriteOrPrint(JsonOutput.FindingsToJson(findings), outPath);
                    break;
                case "sarif":
                    WriteOrPrint(SarifOutput.FindingsToSarif(findings, registry.Rules), outPath);
                    break;
                case "html":
                    if (outPath is null)
                    {
                        TextRenderer.RenderError("Missing --out", "html format requires --out <path>");
                        return 2;
                    }
                    File.WriteAllText(outPath.FullName,
                        HtmlOutput.FindingsToHtml(findings, registry.Rules,
                            title: $"tabkit audit — {file.Name}"));
                    break;
                default: // "text"
                    TextRenderer.RenderFindings(findings);
                    break;
            }

            return TextRenderer.ExitCodeFor(findings);
        });
        return run;
    }

    private static Command BuildDiff()
    {
        var v1Arg = new Argument<FileInfo>("v1") { Description = "Earlier workbook (.twb/.twbx)" };
        var v2Arg = new Argument<FileInfo>("v2") { Description = "Later workbook (.twb/.twbx)" };

        var diff = new Command("diff", "Show semantic + canonical-XML differences between two workbook versions.")
        {
            v1Arg, v2Arg,
        };
        diff.SetAction(parse =>
        {
            var v1 = parse.GetValue(v1Arg)!;
            var v2 = parse.GetValue(v2Arg)!;
            if (!v1.Exists || !v2.Exists)
            {
                TextRenderer.RenderError("Not found",
                    !v1.Exists ? v1.FullName : v2.FullName);
                return 2;
            }
            var a = TwbLoader.Load(v1.FullName);
            var b = TwbLoader.Load(v2.FullName);
            var report = DiffEngine.DiffWorkbooks(a, b);
            foreach (var change in report.Changes)
                Console.WriteLine($"{change.Kind,-8} {change.Category}/{change.Identifier}: {change.Detail}");
            Console.WriteLine($"\n{report.Changes.Count} change(s).");
            return report.Changes.Count == 0 ? 0 : 1;
        });
        return diff;
    }

    private static Command BuildInspect()
    {
        var packOpt = new Option<string>("--pack")
        {
            Description = "Inspect rules belonging to this pack (all | audit | governance)",
            DefaultValueFactory = _ => "all",
        };
        var inspect = new Command("inspect", "List shipped rules with their IDs, severities, and descriptions.")
        {
            packOpt,
        };
        inspect.SetAction(parse =>
        {
            var pack = parse.GetValue(packOpt);
            var registry = RuleRegistry.Default();
            IReadOnlyList<IRule> rules;
            try
            {
                rules = string.Equals(pack, "all", StringComparison.OrdinalIgnoreCase)
                    ? registry.Rules
                    : registry.ForPack(pack);
            }
            catch (ArgumentException ex)
            {
                TextRenderer.RenderError("Unknown pack", ex.Message);
                return 2;
            }
            var table = new Table
            {
                Border = TableBorder.Rounded,
                Title = new TableTitle("[bold]rules[/]"),
            };
            table.AddColumn("ID");
            table.AddColumn("Severity");
            table.AddColumn("Pack");
            table.AddColumn("Title");
            foreach (var r in rules.OrderBy(r => r.Meta.Id, StringComparer.Ordinal))
            {
                table.AddRow(
                    Markup.Escape(r.Meta.Id),
                    Markup.Escape(r.Meta.Severity.ToString().ToLowerInvariant()),
                    Markup.Escape(r.Meta.Pack ?? "-"),
                    Markup.Escape(r.Meta.Title));
            }
            AnsiConsole.Write(table);
            return 0;
        });
        return inspect;
    }

    private static void WriteOrPrint(string text, FileInfo? path)
    {
        if (path is not null)
            File.WriteAllText(path.FullName, text);
        else
            Console.WriteLine(text);
    }
}
