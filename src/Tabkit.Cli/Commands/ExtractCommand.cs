using System.CommandLine;
using Tabkit.Cli.Output;
using Tabkit.Core.Extract;

namespace Tabkit.Cli.Commands;

internal static class ExtractCommand
{
    public static Command Build()
    {
        var cmd = new Command("extract", "Run or validate a YAML extract pipeline.");
        cmd.Subcommands.Add(BuildRun());
        cmd.Subcommands.Add(BuildValidate());
        return cmd;
    }

    private static Command BuildRun()
    {
        var yamlArg = new Argument<FileInfo>("pipeline") { Description = "YAML pipeline file" };
        var run = new Command("run", "Read source, apply transforms, write sink. Runs once and exits.")
        {
            yamlArg,
        };
        run.SetAction(parse =>
        {
            var file = parse.GetValue(yamlArg)!;
            if (!file.Exists)
            {
                TextRenderer.RenderError("Not found", file.FullName);
                return 2;
            }

            var cwd = Environment.CurrentDirectory;
            try
            {
                var anchor = file.DirectoryName;
                if (!string.IsNullOrEmpty(anchor)) Environment.CurrentDirectory = anchor;
                var pipeline = Pipeline.FromFile(file.FullName);
                var result = pipeline.Run();
                Console.WriteLine(
                    $"OK: {result.Name} — {result.RowsIn} → {result.RowsOut} rows in {result.DurationSeconds:0.000}s");
                Console.WriteLine($"columns: {string.Join(", ", result.Columns)}");
                return 0;
            }
            catch (Exception ex)
            {
                TextRenderer.RenderError("Pipeline failed", ex.Message);
                return 2;
            }
            finally { Environment.CurrentDirectory = cwd; }
        });
        return run;
    }

    private static Command BuildValidate()
    {
        var yamlArg = new Argument<FileInfo>("pipeline") { Description = "YAML pipeline file" };
        var validate = new Command("validate", "Parse a YAML pipeline and report schema errors without running it.")
        {
            yamlArg,
        };
        validate.SetAction(parse =>
        {
            var file = parse.GetValue(yamlArg)!;
            if (!file.Exists)
            {
                TextRenderer.RenderError("Not found", file.FullName);
                return 2;
            }

            try
            {
                // Full semantic validation — Pipeline.Validate exercises every
                // source / transform / sink without running them, so type
                // mismatches and value-shape mistakes (e.g. filter `in: alice`
                // instead of `in: [alice]`) are caught here rather than at run
                // time as silently-wrong output.
                var pipeline = Pipeline.ValidateFile(file.FullName);
                var sourceType = pipeline.Source.TryGetValue("type", out var st) ? st?.ToString() : "?";
                var sinkType   = pipeline.Sink.TryGetValue("type", out var kt)   ? kt?.ToString() : "?";
                Console.WriteLine(
                    $"OK: '{pipeline.Name}' — source={sourceType}, " +
                    $"{pipeline.Transforms.Count} transform(s), sink={sinkType}");
                return 0;
            }
            catch (Exception ex)
            {
                TextRenderer.RenderError("Invalid pipeline", ex.Message);
                return 1;
            }
        });
        return validate;
    }
}
