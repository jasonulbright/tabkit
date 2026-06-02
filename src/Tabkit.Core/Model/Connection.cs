using System.Collections.Generic;
using System.Collections.Immutable;

namespace Tabkit.Core.Model;

/// <summary>
/// A connection block inside a data source.
/// <c>Attrs</c> holds attributes we don't model explicitly — keeps governance
/// rules open-ended (e.g. inspecting connection-specific attributes like
/// <c>dataserver-permissions</c>).
/// </summary>
public sealed record Connection(
    string? ConnectionClass,
    string? Server = null,
    string? Dbname = null,
    string? Username = null,
    string? Password = null, // presence is the governance signal, not the value
    string? Filename = null,
    string? Port = null,
    string? Authentication = null,
    IReadOnlyDictionary<string, string>? Attrs = null)
{
    public IReadOnlyDictionary<string, string> Attrs { get; init; } =
        Attrs ?? ImmutableDictionary<string, string>.Empty;
}
