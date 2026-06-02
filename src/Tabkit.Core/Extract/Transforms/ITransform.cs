using System.Data;

namespace Tabkit.Core.Extract.Transforms;

public interface ITransform
{
    DataTable Apply(DataTable table);
}
