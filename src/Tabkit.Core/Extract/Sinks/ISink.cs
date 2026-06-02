using System.Data;

namespace Tabkit.Core.Extract.Sinks;

public interface ISink
{
    void Write(DataTable table);
}
