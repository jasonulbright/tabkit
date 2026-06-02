using System.IO;
using Microsoft.Win32;
using Tabkit.App.Services;
using Tabkit.Core.Extract;

namespace Tabkit.App.ViewModels.Pages;

public partial class ExtractViewModel : ObservableObject
{
    private const int RecentCap = 8;

    private readonly ExtractService _extract;
    private readonly SettingsService _settings;
    private readonly ISnackbarService _snackbar;
    private CancellationTokenSource? _runCts;

    public ExtractViewModel(ExtractService extract, SettingsService settings, ISnackbarService snackbar)
    {
        _extract = extract;
        _settings = settings;
        _snackbar = snackbar;
        HydrateRecentsFromSettings();
        _settings.Changed += (_, __) => HydrateRecentsFromSettings();
    }

    [ObservableProperty] private string _pipelinePath = string.Empty;
    [ObservableProperty] private string _yamlText = StarterYaml;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleLoadSaveValidate))]
    [NotifyPropertyChangedFor(nameof(IsVisibleRun))]
    private bool _isRunning;

    // Validation surface
    [ObservableProperty] private string _validationMessage = "Edit YAML or load a pipeline, then click Validate.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisibleRun))]
    private bool _isValid;

    // Visibility mirrors for hide-don't-disable. CancelRun stays visible-but-disabled
    // (Gallery convention exception for operation-control pairs).
    public bool IsVisibleLoadSaveValidate => CanInteract();
    public bool IsVisibleRun => CanRun();

    // Last run surface
    [ObservableProperty] private string _resultName = string.Empty;
    [ObservableProperty] private int _rowsIn;
    [ObservableProperty] private int _rowsOut;
    [ObservableProperty] private string _durationDisplay = string.Empty;
    [ObservableProperty] private string _columnsDisplay = string.Empty;
    [ObservableProperty] private int _transformsApplied;
    [ObservableProperty] private string _resultOutputPath = string.Empty;
    [ObservableProperty] private bool _hasResult;

    [ObservableProperty] private string _statusMessage = "Ready.";

    public ObservableCollection<string> RecentPipelines { get; } = new();

    /// <summary>Populated from <see cref="SettingsService"/> on construction so the dropdown survives restarts.</summary>
    public void HydrateRecentsFromSettings()
    {
        RecentPipelines.Clear();
        foreach (var p in _settings.Current.RecentPipelines)
            RecentPipelines.Add(p);
    }

    partial void OnYamlTextChanged(string value)
    {
        // Any edit clears the validated state until re-run.
        IsValid = false;
        ValidationMessage = "Edited — re-validate before running.";
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
        ValidateCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        OpenRecentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Load()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load pipeline YAML",
            Filter = "YAML (*.yml;*.yaml)|*.yml;*.yaml|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = string.IsNullOrWhiteSpace(_settings.Current.ExtractOutputDir)
                ? null
                : _settings.Current.ExtractOutputDir,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            YamlText = File.ReadAllText(dlg.FileName);
            PipelinePath = dlg.FileName;
            AddRecent(dlg.FileName);
            StatusMessage = $"Loaded {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            Snack("Load failed", ex.Message, danger: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Save()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save pipeline YAML",
            Filter = "YAML (*.yml;*.yaml)|*.yml;*.yaml|All files (*.*)|*.*",
            FileName = string.IsNullOrEmpty(PipelinePath)
                ? "pipeline.yml"
                : Path.GetFileName(PipelinePath),
            InitialDirectory = ResolveLoadSaveDir(),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, YamlText);
            PipelinePath = dlg.FileName;
            AddRecent(dlg.FileName);
            StatusMessage = $"Saved {Path.GetFileName(dlg.FileName)}.";
        }
        catch (Exception ex)
        {
            Snack("Save failed", ex.Message, danger: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Validate()
    {
        try
        {
            var p = _extract.Validate(YamlText);
            IsValid = true;
            var sourceType = p.Source.TryGetValue("type", out var st) ? st?.ToString() : "?";
            var sinkType   = p.Sink.TryGetValue("type",   out var kt) ? kt?.ToString() : "?";
            ValidationMessage =
                $"OK: '{p.Name}' — source={sourceType}, {p.Transforms.Count} transform(s), sink={sinkType}.";
            StatusMessage = ValidationMessage;
        }
        catch (Exception ex)
        {
            IsValid = false;
            ValidationMessage = $"Invalid: {ex.Message}";
            StatusMessage = "Pipeline failed validation.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        IsRunning = true;
        HasResult = false;
        StatusMessage = "Running pipeline...";
        _runCts = new CancellationTokenSource();

        try
        {
            // Snapshot the editor text once. The editor stays live during the
            // run, so re-reading YamlText afterward could parse a different
            // pipeline than the one that executed — wrong metadata, or a false
            // failure after output was already written.
            var yaml = YamlText;
            var workingDir = ResolveWorkingDir();
            var result = await _extract.RunAsync(yaml, workingDir, _runCts.Token);

            // Parse the same snapshot on the UI thread to surface transform count
            // and the resolved sink path in the result panel.
            var pipeline = _extract.Validate(yaml);
            var resolvedOutputPath = ResolveSinkPath(pipeline, workingDir);
            ShowResult(result, pipeline.Transforms.Count, resolvedOutputPath);

            StatusMessage =
                $"OK: {result.Name} — {result.RowsIn} → {result.RowsOut} rows in {result.DurationSeconds:0.000}s.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Pipeline run cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
            Snack("Run failed", ex.Message, danger: true);
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun() => _runCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            RemoveRecentEverywhere(path);
            return;
        }
        try
        {
            YamlText = File.ReadAllText(path);
            PipelinePath = path;
            AddRecent(path);
            StatusMessage = $"Loaded {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Snack("Load failed", ex.Message, danger: true);
        }
    }

    private bool CanInteract() => !IsRunning;
    private bool CanRun() => !IsRunning && IsValid;
    private bool CanCancelRun() => IsRunning;

    private void ShowResult(PipelineResult result, int transformsApplied, string outputPath)
    {
        ResultName = result.Name;
        RowsIn = result.RowsIn;
        RowsOut = result.RowsOut;
        DurationDisplay = $"{result.DurationSeconds:0.000}s";
        ColumnsDisplay = string.Join(", ", result.Columns);
        TransformsApplied = transformsApplied;
        ResultOutputPath = outputPath;
        HasResult = true;
    }

    private string? ResolveLoadSaveDir()
    {
        if (!string.IsNullOrEmpty(PipelinePath))
        {
            var dir = Path.GetDirectoryName(PipelinePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        var configured = _settings.Current.ExtractOutputDir;
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }

    private string ResolveWorkingDir()
    {
        // Priority: pipeline file's directory → Settings.ExtractOutputDir → cwd.
        if (!string.IsNullOrEmpty(PipelinePath))
        {
            var dir = Path.GetDirectoryName(PipelinePath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        var configured = _settings.Current.ExtractOutputDir;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;
        return Environment.CurrentDirectory;
    }

    private static string ResolveSinkPath(Pipeline pipeline, string anchorDir)
    {
        if (!pipeline.Sink.TryGetValue("path", out var raw) || raw is null)
            return string.Empty;
        var p = raw.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(p)) return string.Empty;
        return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(anchorDir, p));
    }

    private void AddRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        RecentPipelines.Remove(path);
        RecentPipelines.Insert(0, path);
        while (RecentPipelines.Count > RecentCap)
            RecentPipelines.RemoveAt(RecentPipelines.Count - 1);

        var s = _settings.Current;
        s.RecentPipelines.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        s.RecentPipelines.Insert(0, path);
        while (s.RecentPipelines.Count > RecentCap)
            s.RecentPipelines.RemoveAt(s.RecentPipelines.Count - 1);
        _settings.Save(s);
    }

    private void RemoveRecentEverywhere(string path)
    {
        RecentPipelines.Remove(path);
        var s = _settings.Current;
        var changed = s.RecentPipelines.RemoveAll(
            p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        if (changed) _settings.Save(s);
    }

    private void Snack(string title, string message, bool danger)
    {
        var appearance = danger ? ControlAppearance.Danger : ControlAppearance.Caution;
        var icon = danger ? SymbolRegular.ErrorCircle24 : SymbolRegular.Info24;
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(6));
    }

    private const string StarterYaml = """
        # Tabkit extract pipeline — replace this with your own definition.
        # Run with the Run button below, or via: tabkit extract run <this-file>

        name: starter

        source:
          type: csv
          path: ./input.csv
          has_header: true

        transforms:
          - type: select
            columns: [id, value]
          - type: filter
            where:
              - { col: value, op: ">", value: 0 }

        sink:
          type: csv
          path: ./output.csv
        """;
}
