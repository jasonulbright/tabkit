using System.CommandLine;
using System.Text;
using Tabkit.Cli.Commands;

namespace Tabkit.Cli;

internal static class Program
{
    /// <summary>
    /// Process exit codes (mirror the Python POC contract):
    ///   0 - no findings, or pure info / warnings of severity below the threshold
    ///   1 - at least one warning-severity finding
    ///   2 - at least one error-severity finding (highest wins)
    /// Extract / inventory commands return 0 on success, non-zero on failure.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        // Spectre.Console uses box-drawing characters that mojibake on
        // Windows consoles that default to legacy code pages.
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected stream may reject */ }

        var root = new RootCommand("tabkit - Tableau workbook governance and inventory toolkit")
        {
            AuditCommand.Build(),
            ExtractCommand.Build(),
            InventoryCommand.Build(),
        };
        return await root.Parse(args).InvokeAsync();
    }
}
