using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Tabkit.Core.Loading;

/// <summary>
/// Bounded extraction helper for the <c>.twb</c> member inside a <c>.twbx</c>.
/// Every code path that reads a .twbx must go through this so we don't expose
/// a zip-bomb / memory-DoS surface: a malicious or corrupted .twbx can declare
/// a tiny compressed size with a multi-gigabyte uncompressed payload, or lie
/// about the declared uncompressed size entirely.
/// </summary>
/// <remarks>
/// Closes Codex R1 finding #5 (.twbx extraction has no compressed/uncompressed
/// size cap). 100 MB is well above any realistic Tableau workbook XML
/// (typical enterprise .twb is &lt; 10 MB) and 100:1 compression ratio is
/// well above the natural compressibility of Tableau's XML (~5-15x).
/// </remarks>
public static class TwbxSafety
{
    /// <summary>Hard cap on the uncompressed size of the .twb member, in bytes.</summary>
    public const long MaxUncompressedBytes = 100L * 1024 * 1024;

    /// <summary>Hard cap on declared compressed:uncompressed ratio. Real .twb XML compresses ~5-15x.</summary>
    public const long MaxCompressionRatio = 100;

    /// <summary>
    /// Open the single <c>.twb</c> member inside <paramref name="twbxPath"/>
    /// into an in-memory stream, enforcing size + ratio caps. Throws
    /// <see cref="InvalidOperationException"/> on missing / duplicate members
    /// and on size/ratio limit violations. The caller owns the returned stream.
    /// </summary>
    public static MemoryStream OpenTwbEntry(string twbxPath)
    {
        using var zip = ZipFile.OpenRead(twbxPath);
        var members = zip.Entries
            .Where(e => e.FullName.EndsWith(".twb", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (members.Count == 0)
            throw new InvalidOperationException(
                $"no .twb member inside {Path.GetFileName(twbxPath)}");
        if (members.Count > 1)
            throw new InvalidOperationException(
                $"multiple .twb members inside {Path.GetFileName(twbxPath)}: " +
                string.Join(", ", members.Select(m => m.FullName)));

        var entry = members[0];

        if (entry.Length > MaxUncompressedBytes)
            throw new InvalidOperationException(
                $"refusing to extract .twb member '{entry.FullName}': declared uncompressed size " +
                $"{entry.Length:N0} bytes exceeds the {MaxUncompressedBytes:N0}-byte cap.");

        if (entry.CompressedLength > 0 &&
            entry.Length / entry.CompressedLength > MaxCompressionRatio)
            throw new InvalidOperationException(
                $"refusing to extract .twb member '{entry.FullName}': compression ratio " +
                $"{entry.Length / entry.CompressedLength}:1 exceeds the {MaxCompressionRatio}:1 cap " +
                "(possible zip-bomb).");

        // Bounded read in case the central-directory size lied. CopyTo with a
        // running byte tally lets us bail out before allocating gigabytes.
        // Initial capacity = declared size (clamped to int) so the common case
        // does not re-grow the underlying buffer.
        var initialCapacity = entry.Length <= int.MaxValue ? (int)entry.Length : int.MaxValue;
        var ms = new MemoryStream(initialCapacity);
        using (var es = entry.Open())
        {
            var buf = new byte[81920];
            long total = 0;
            int n;
            while ((n = es.Read(buf, 0, buf.Length)) > 0)
            {
                total += n;
                if (total > MaxUncompressedBytes)
                {
                    ms.Dispose();
                    throw new InvalidOperationException(
                        $"refusing to extract .twb member '{entry.FullName}': actual size " +
                        $"exceeded {MaxUncompressedBytes:N0}-byte cap mid-stream.");
                }
                ms.Write(buf, 0, n);
            }
        }
        ms.Position = 0;
        return ms;
    }
}
