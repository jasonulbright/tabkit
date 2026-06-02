using Tabkit.Core.Loading;
using Tabkit.Core.Model;

namespace Tabkit.App.Services;

/// <summary>
/// Thin wrapper over <see cref="TwbLoader"/> so the UI layer doesn't take a
/// hard dependency on the static loader and can be mocked in tests later.
/// </summary>
public sealed class WorkbookService
{
    public Workbook Load(string path) => TwbLoader.Load(path);

    public Task<Workbook> LoadAsync(string path, CancellationToken ct = default)
        => Task.Run(() => TwbLoader.Load(path), ct);
}
