using System.Data;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Tabkit.Core.Extract.Sources;

public sealed class CsvSource : ISource
{
    public string Path { get; init; } = "";
    public bool HasHeader { get; init; } = true;
    public string Separator { get; init; } = ",";
    public string Encoding { get; init; } = "utf8";

    public DataTable Read()
    {
        using var reader = new StreamReader(Path);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = HasHeader,
            Delimiter = Separator,
        };
        using var csv = new CsvReader(reader, config);
        using var dr = new CsvDataReader(csv);
        var dt = new DataTable();
        dt.Load(dr);
        return dt;
    }
}
