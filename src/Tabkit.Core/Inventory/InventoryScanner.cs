using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Tabkit.Core.Loading;

namespace Tabkit.Core.Inventory;

/// <summary>Walk a tree, parse Tableau artifacts, populate an <see cref="InventoryStore"/>.</summary>
public static class InventoryScanner
{
    private static readonly string[] ScannableSuffixes = { ".twb", ".twbx" };

    public static IEnumerable<string> IterWorkbookFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => ScannableSuffixes.Contains(Path.GetExtension(p).ToLowerInvariant()));
    }

    public static ScanResult ScanTree(
        string root,
        InventoryStore store,
        Action<string>? onProgress = null)
    {
        var scanned = 0;
        var indexed = 0;
        var skipped = 0;
        var errors = new List<(string Path, string Message)>();

        foreach (var path in IterWorkbookFiles(root))
        {
            scanned++;
            onProgress?.Invoke(path);
            try
            {
                var info = new FileInfo(path);
                var sha = Sha256Of(path);
                var wb = TwbLoader.Load(path);
                store.UpsertWorkbook(
                    path: path,
                    sha256: sha,
                    mtime: ((DateTimeOffset)info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000.0,
                    sizeBytes: info.Length,
                    wb: wb);
                indexed++;
            }
            catch (TwbParseException ex)
            {
                errors.Add((path, "parse: " + ex.Message));
                skipped++;
            }
            catch (Exception ex)
            {
                errors.Add((path, $"{ex.GetType().Name}: {ex.Message}"));
                skipped++;
            }
        }

        return new ScanResult(scanned, indexed, skipped, errors);
    }

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
