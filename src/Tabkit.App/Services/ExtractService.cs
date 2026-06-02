using System.IO;
using Tabkit.Core.Extract;

namespace Tabkit.App.Services;

/// <summary>
/// UI-layer wrapper over <see cref="Pipeline"/>. Validate is sync (just a YAML
/// parse + required-field check). Run is async because sinks can take a while.
/// </summary>
public sealed class ExtractService
{
    // Pipeline.Validate runs the full semantic check (source/sink/transform
    // construction, scalar + membership shape rules), matching what the CLI and
    // engine enforce. FromYaml only does the top-level shape parse, which let the
    // UI mark pipelines valid that the engine would reject at run time.
    public Pipeline Validate(string yamlText) => Pipeline.Validate(yamlText);

    public Task<PipelineResult> RunAsync(
        string yamlText,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            // Pipelines reference paths relative to wherever the user invokes
            // them. When run from a YAML file, the natural anchor is that file's
            // directory; for inline editing without a save path, fall back to
            // the user's working dir.
            var previousCwd = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                Environment.CurrentDirectory = workingDirectory;
            try
            {
                // Validate (not FromYaml) so a run on never-validated text still
                // gets the strict semantic checks instead of misbehaving silently.
                var pipeline = Pipeline.Validate(yamlText);
                ct.ThrowIfCancellationRequested();
                return pipeline.Run();
            }
            finally
            {
                Environment.CurrentDirectory = previousCwd;
            }
        }, ct);
    }
}
