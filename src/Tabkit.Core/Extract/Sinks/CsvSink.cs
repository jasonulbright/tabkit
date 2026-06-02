using System.Data;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Tabkit.Core.Extract.Sinks;

public sealed class CsvSink : ISink
{
    public string Path { get; init; } = "";
    public string Separator { get; init; } = ",";

    public void Write(DataTable table)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = Separator,
        };
        using var writer = new StreamWriter(Path);
        using var csv = new CsvWriter(writer, config);

        foreach (DataColumn col in table.Columns)
            csv.WriteField(col.ColumnName);
        csv.NextRecord();

        foreach (DataRow row in table.Rows)
        {
            foreach (var cell in row.ItemArray)
                csv.WriteField(cell);
            csv.NextRecord();
        }
    }
}
